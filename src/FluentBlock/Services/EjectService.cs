using System.Runtime.InteropServices;
using System.Text;

namespace FluentBlock.Services;

/// <summary>
/// Safe removal via cfgmgr32 CM_Request_Device_EjectW.
/// Resolves drive letter -&gt; disk devnode -&gt; removable ancestors, then ejects
/// bottom-up (disk, parent, grandparent). Refused (vetoed) attempts have no
/// side effects. PreviewEjectTarget resolves without ejecting (safe to test).
/// </summary>
public static class EjectService
{
    private static readonly Guid GUID_DEVINTERFACE_DISK = new(0x53f56307, 0xb6bf, 0x11d0, 0x94, 0xf2, 0x00, 0xa0, 0xc9, 0x1e, 0xfb, 0x8b);
    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_DEVICEINTERFACE = 0x00000010;
    private const uint IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x2D1080;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_SHARE_READ = 1;
    private const uint FILE_SHARE_WRITE = 2;
    private const uint SPDRP_DEVICEDESC = 0;
    private const uint DN_DISABLEABLE = 0x400;
    private const uint DN_REMOVABLE = 0x4000;
    private const uint CR_SUCCESS = 0;
    private const uint CR_REMOVE_VETOED = 0x17;
    private const uint CM_DRP_DEVICEDESC = 0x01;
    private const uint CM_DRP_FRIENDLYNAME = 0x0D;
    private const int MAX_PATH = 260;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    private enum PNP_VETO_TYPE
    {
        Unknown, LegacyDevice, PendingClose, WindowsApp, WindowsService,
        OutstandingOpen, Device, Driver, IllegalDeviceRequest, InsufficientPower,
        NonDisableable, LegacyDriver, InsufficientRights
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_DEVICE_NUMBER
    {
        public uint DeviceType;
        public uint DeviceNumber;
        public uint PartitionNumber;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode,
        IntPtr lpInBuffer, uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, string? enumerator,
        IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr devInfo, IntPtr devInfoData,
        ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA ifData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr devInfo, ref SP_DEVICE_INTERFACE_DATA ifData,
        IntPtr detail, uint detailSize, out uint requiredSize, ref SP_DEVINFO_DATA devInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr devInfo);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_Parent(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_DevNode_Status(out uint pulStatus, out uint pulProblemNumber,
        uint dnDevInst, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Request_Device_EjectW(uint dnDevInst, out PNP_VETO_TYPE pVetoType,
        StringBuilder pszVetoName, uint ulNameLength, uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_DevNode_Registry_PropertyW(uint dnDevInst, uint ulProperty,
        IntPtr pulRegDataType, byte[] buffer, ref uint pulLength, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_IDW(uint dnDevInst, StringBuilder buffer, uint bufferLen, uint ulFlags);

    public sealed record EjectResult(bool Ejected, string? VetoType, string? VetoName, uint ErrorCode);

    public static Task<EjectResult> TryEjectAsync(string driveLetter, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var chain = ResolveEjectChain(driveLetter);
            string? vetoType = null, vetoName = null;
            uint lastRc = CR_SUCCESS;
            foreach (var (dev, _) in chain)
            {
                ct.ThrowIfCancellationRequested();
                if (CM_Get_DevNode_Status(out uint status, out _, dev, 0) == CR_SUCCESS
                    && (status & (DN_DISABLEABLE | DN_REMOVABLE)) == 0)
                    continue; // not ejectable: try the parent
                var vetoBuf = new StringBuilder(MAX_PATH);
                lastRc = CM_Request_Device_EjectW(dev, out var veto, vetoBuf, MAX_PATH, 0);
                if (lastRc == CR_SUCCESS)
                    return new EjectResult(true, null, null, 0);
                if (lastRc == CR_REMOVE_VETOED && vetoType == null)
                {
                    vetoType = veto.ToString();
                    vetoName = vetoBuf.ToString();
                }
            }
            return new EjectResult(false, vetoType, vetoName, lastRc);
        }, ct);
    }

    /// <summary>Resolves the eject chain without ejecting. For diagnostics and tests.</summary>
    public static string PreviewEjectTarget(string driveLetter)
    {
        var chain = ResolveEjectChain(driveLetter);
        return string.Join(" <- ", chain.Select(c => c.Desc));
    }

    /// <summary>True if any node in the drive's device chain is removable (USB-HDD check).</summary>
    public static bool HasRemovableAncestor(string driveLetter)
    {
        try
        {
            foreach (var (dev, _) in ResolveEjectChain(driveLetter))
            {
                if (CM_Get_DevNode_Status(out uint st, out _, dev, 0) == CR_SUCCESS
                    && (st & DN_REMOVABLE) != 0)
                    return true;
            }
        }
        catch { }
        return false;
    }

    private static List<(uint DevInst, string Desc)> ResolveEjectChain(string driveLetter)
    {
        string letter = driveLetter.TrimEnd('\\');
        uint diskNumber = GetVolumeDiskNumber(letter);
        uint diskDev = FindDiskDevInst(diskNumber);
        var chain = new List<(uint, string)> { (diskDev, DescribeDevNode(diskDev)) };
        uint dev = diskDev;
        for (int i = 0; i < 2; i++)
        {
            if (CM_Get_Parent(out uint parent, dev, 0) != CR_SUCCESS || parent == dev) break;
            dev = parent;
            chain.Add((dev, DescribeDevNode(dev)));
        }
        return chain;
    }

    private static uint GetVolumeDiskNumber(string letter)
    {
        IntPtr h = CreateFileW(@"\\.\" + letter, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        int openErr = Marshal.GetLastWin32Error(); // capture before any other call clobbers it
        if (h == IntPtr.Zero || h == INVALID_HANDLE_VALUE)
            throw new InvalidOperationException(
                string.Format(L10n.Get("EjectError"), openErr) + $" (open \\\\.\\{letter})");
        try
        {
            int size = Marshal.SizeOf<STORAGE_DEVICE_NUMBER>();
            IntPtr outBuf = Marshal.AllocHGlobal(size);
            try
            {
                bool ok = DeviceIoControl(h, IOCTL_STORAGE_GET_DEVICE_NUMBER,
                        IntPtr.Zero, 0, outBuf, (uint)size, out _, IntPtr.Zero);
                int ioctlErr = Marshal.GetLastWin32Error();
                if (!ok)
                    throw new InvalidOperationException(
                        string.Format(L10n.Get("EjectError"), ioctlErr) + " (device-number ioctl)");
                return (uint)Marshal.ReadInt32(outBuf, 4); // DeviceNumber
            }
            finally { Marshal.FreeHGlobal(outBuf); }
        }
        finally { CloseHandle(h); }
    }

    private static uint FindDiskDevInst(uint diskNumber)
    {
        Guid guid = GUID_DEVINTERFACE_DISK;
        IntPtr set = SetupDiGetClassDevsW(ref guid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        int setupErr = Marshal.GetLastWin32Error();
        if (set == IntPtr.Zero || set == INVALID_HANDLE_VALUE)
            throw new InvalidOperationException(string.Format(L10n.Get("EjectError"), setupErr));
        int seen = 0, opened = 0;
        try
        {
            for (uint i = 0; ; i++)
            {
                var ifData = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref ifData))
                    break; // no more interfaces
                seen++;
                var devData = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
                if (!TryGetInterfacePath(set, ref ifData, ref devData, out string? path) || path == null)
                    continue;
                IntPtr h = CreateFileW(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == IntPtr.Zero || h == INVALID_HANDLE_VALUE) continue;
                opened++;
                try
                {
                    int size = Marshal.SizeOf<STORAGE_DEVICE_NUMBER>();
                    IntPtr outBuf = Marshal.AllocHGlobal(size);
                    try
                    {
                        if (!DeviceIoControl(h, IOCTL_STORAGE_GET_DEVICE_NUMBER,
                                IntPtr.Zero, 0, outBuf, (uint)size, out _, IntPtr.Zero))
                            continue;
                        if ((uint)Marshal.ReadInt32(outBuf, 4) == diskNumber)
                            return devData.DevInst;
                    }
                    finally { Marshal.FreeHGlobal(outBuf); }
                }
                finally { CloseHandle(h); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        throw new InvalidOperationException(
            string.Format(L10n.Get("EjectError"), 0) + $" (no disk #{diskNumber} among {seen} interfaces, {opened} opened)");
    }

    private static bool TryGetInterfacePath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA ifData,
        ref SP_DEVINFO_DATA devData, out string? path)
    {
        path = null;
        if (!SetupDiGetDeviceInterfaceDetailW(set, ref ifData, IntPtr.Zero, 0, out uint req, ref devData) && req == 0)
            return false;
        IntPtr buf = Marshal.AllocHGlobal((int)req);
        try
        {
            // cbSize = struct size (8 on x64); the DevicePath text always
            // follows the DWORD, i.e. at offset 4 on both platforms.
            Marshal.WriteInt32(buf, 0, IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize);
            if (!SetupDiGetDeviceInterfaceDetailW(set, ref ifData, buf, req, out _, ref devData))
                return false;
            path = Marshal.PtrToStringUni(buf + 4);
            return path != null;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static string DescribeDevNode(uint dev)
    {
        // Friendly name first, then description, then raw device ID.
        foreach (uint prop in new[] { CM_DRP_FRIENDLYNAME, CM_DRP_DEVICEDESC })
        {
            try
            {
                byte[] buf = new byte[2048];
                uint len = (uint)buf.Length;
                if (CM_Get_DevNode_Registry_PropertyW(dev, prop, IntPtr.Zero, buf, ref len, 0) == CR_SUCCESS && len > 2)
                {
                    string s = Encoding.Unicode.GetString(buf, 0, (int)len).TrimEnd('\0');
                    if (s.Length > 0) return s;
                }
            }
            catch { }
        }
        try
        {
            var sb = new StringBuilder(MAX_PATH);
            if (CM_Get_Device_IDW(dev, sb, MAX_PATH, 0) == CR_SUCCESS)
                return sb.ToString();
        }
        catch { }
        return $"devinst:{dev}";
    }
}
