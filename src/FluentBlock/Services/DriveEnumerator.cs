using System.IO;
using System.Runtime.InteropServices;
using FluentBlock.Models;

namespace FluentBlock.Services;

/// <summary>Enumerates ejectable drives: DRIVE_REMOVABLE + USB-external DRIVE_FIXED (e.g. SSD in USB enclosure).</summary>
public static class DriveEnumerator
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetDriveTypeW(string lpRootPathName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformationW(
        string lpRootPathName,
        System.Text.StringBuilder lpVolumeNameBuffer, uint nVolumeNameSize,
        out uint lpVolumeSerialNumber, out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        System.Text.StringBuilder lpFileSystemNameBuffer, uint nFileSystemNameSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode,
        IntPtr lpInBuffer, uint nInBufferSize,
        IntPtr lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint DRIVE_REMOVABLE = 2;
    private const uint DRIVE_FIXED = 3;
    private const uint FILE_SHARE_READ = 1;
    private const uint FILE_SHARE_WRITE = 2;
    private const uint OPEN_EXISTING = 3;
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x2D1400;
    private const uint IOCTL_STORAGE_GET_HOTPLUG_INFO = 0x2D0C14;
    private const int BusTypeUsb = 7;

    public static List<RemovableDrive> GetRemovableDrives()
    {
        var list = new List<RemovableDrive>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            string root = drive.RootDirectory.FullName; // "E:\\"
            uint type;
            try { type = GetDriveTypeW(root); }
            catch { continue; }
            bool include = type == DRIVE_REMOVABLE;
            if (!include && type == DRIVE_FIXED)
            {
                string letterOnly = root.TrimEnd('\\'); // "E:"
                include = IsUsbOrHotplug(letterOnly) || EjectService.HasRemovableAncestor(letterOnly);
            }
            if (!include)
                continue;

            string label = "";
            try
            {
                var vol = new System.Text.StringBuilder(261);
                var fsb = new System.Text.StringBuilder(261);
                if (GetVolumeInformationW(root, vol, 261, out _, out _, out _, fsb, 261))
                    label = vol.ToString();
            }
            catch { /* keep empty */ }

            string letter = root.TrimEnd('\\'); // "E:"
            list.Add(new RemovableDrive(letter, label));
        }
        return list;
    }

    /// <summary>True if the volume sits on a USB-bus or hotplug device (external enclosure).</summary>
    public static bool IsUsbOrHotplug(string driveLetter) // "E:"
    {
        IntPtr h = IntPtr.Zero;
        try
        {
            string device = @"\\.\" + driveLetter; // "\\.\E:"
            // No read access requested: property queries work without it, so this
            // also succeeds for non-admin users.
            h = CreateFileW(device, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == IntPtr.Zero || h.ToInt64() == -1)
                return false;

            if (IsUsbBus(h))
                return true;
            return IsHotplug(h);
        }
        catch { return false; }
        finally
        {
            if (h != IntPtr.Zero && h.ToInt64() != -1)
            {
                try { CloseHandle(h); } catch { }
            }
        }
    }

    private static bool IsUsbBus(IntPtr h)
    {
        IntPtr inBuf = IntPtr.Zero;
        IntPtr outBuf = IntPtr.Zero;
        try
        {
            inBuf = Marshal.AllocHGlobal(12); // STORAGE_PROPERTY_QUERY zeroed = PropertyStandardQuery/DeviceProperty
            for (int i = 0; i < 12; i++) Marshal.WriteByte(inBuf, i, 0);
            outBuf = Marshal.AllocHGlobal(1024);
            for (int i = 0; i < 1024; i++) Marshal.WriteByte(outBuf, i, 0);
            bool ok = DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY,
                inBuf, 12, outBuf, 1024, out uint ret, IntPtr.Zero);
            if (!ok || ret < 32)
                return false;
            int busType = Marshal.ReadInt32(outBuf, 28); // offset of BusType in STORAGE_DEVICE_DESCRIPTOR
            return busType == BusTypeUsb;
        }
        catch { return false; }
        finally
        {
            if (inBuf != IntPtr.Zero) Marshal.FreeHGlobal(inBuf);
            if (outBuf != IntPtr.Zero) Marshal.FreeHGlobal(outBuf);
        }
    }

    private static bool IsHotplug(IntPtr h)
    {
        IntPtr outBuf = IntPtr.Zero;
        try
        {
            outBuf = Marshal.AllocHGlobal(8);
            for (int i = 0; i < 8; i++) Marshal.WriteByte(outBuf, i, 0);
            Marshal.WriteInt32(outBuf, 0, 8);
            bool ok = DeviceIoControl(h, IOCTL_STORAGE_GET_HOTPLUG_INFO,
                IntPtr.Zero, 0, outBuf, 8, out uint ret, IntPtr.Zero);
            if (!ok || ret < 8)
                return false;
            byte mediaRemovable = Marshal.ReadByte(outBuf, 4);
            byte deviceHotplug = Marshal.ReadByte(outBuf, 6);
            return mediaRemovable != 0 || deviceHotplug != 0;
        }
        catch { return false; }
        finally
        {
            if (outBuf != IntPtr.Zero) Marshal.FreeHGlobal(outBuf);
        }
    }
}
