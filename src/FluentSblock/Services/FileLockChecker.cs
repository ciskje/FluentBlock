using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using FluentSblock.Models;

namespace FluentSblock.Services;

/// <summary>
/// Answers "who locks this file?" via Restart Manager (official API).
/// Falls back to handle scan when RM finds nothing.
/// </summary>
public static class FileLockChecker
{
    [DllImport("rstrmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint pSessionHandle,
        uint nFiles, string[]? rgsFilenames,
        uint nApplications, IntPtr rgApplications,
        uint nServices, string[]? rgsServiceNames);

    [DllImport("rstrmgr.dll")]
    private static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded,
        ref uint pnProcInfo, [In, Out] RM_PROCESS_INFO[]? rgAffectedApps, ref uint lpdwRebootReasons);

    [DllImport("rstrmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    private const int ERROR_MORE_DATA = 234;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string strServiceShortName;
        public uint ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public uint dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    public static async Task<List<LockHolder>> WhoLocksAsync(string path,
        IProgress<string>? progress, CancellationToken ct)
    {
        // Restart Manager calls are synchronous; run them on a worker so the
        // token can abort the wait (an abandoned worker ends its own session).
        var rmTask = Task.Run(() => TryRestartManager(path), ct);
        var completed = await Task.WhenAny(rmTask, Task.Delay(Timeout.InfiniteTimeSpan, ct));
        if (completed == rmTask && rmTask.Result.Count > 0) return rmTask.Result;
        ct.ThrowIfCancellationRequested();
        progress?.Report(L10n.Get("RmFallback"));

        if (ElevationService.IsElevatedOrWorkerActive)
        {
            var elevated = await ElevationService.TryScanPathElevatedAsync(path, progress, ct);
            if (elevated != null) return elevated;
        }

        var userHolders = await HandleEnumerator.GetHoldersForPathAsync(path, progress, ct);
        if (userHolders.Count > 0)
            return userHolders;

        // Nothing found at user integrity: if the target file/folder exists,
        // automatically query the elevated background worker to inspect system/admin processes.
        if (File.Exists(path) || Directory.Exists(path))
        {
            var elevated = await ElevationService.TryScanPathElevatedAsync(path, progress, ct);
            if (elevated != null) return elevated;
        }
        return userHolders;
    }

    private static List<LockHolder> TryRestartManager(string path)
    {
        var result = new List<LockHolder>();
        if (Directory.Exists(path)) return result; // RM only registers files, not directories
        uint session = 0;
        try
        {
            string key = Guid.NewGuid().ToString();
            if (RmStartSession(out session, 0, key) != 0) return result;
            string full = Path.GetFullPath(path);
            // RM also accepts a non-existent path as a probe.
            string[] files = new[] { full };
            if (RmRegisterResources(session, (uint)files.Length, files, 0, IntPtr.Zero, 0, null) != 0)
                return result;

            uint needed = 0, count = 0, reboot = 0;
            int rc = RmGetList(session, out needed, ref count, null, ref reboot);
            if (rc == ERROR_MORE_DATA && needed > 0)
            {
                var arr = new RM_PROCESS_INFO[needed];
                count = needed;
                rc = RmGetList(session, out _, ref count, arr, ref reboot);
                if (rc == 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        uint pid = arr[i].Process.dwProcessId;
                        var (name, user) = HandleEnumerator.ResolveProcessIdentity(pid, arr[i].strAppName);
                        result.Add(new LockHolder(name, (int)pid, user, "", full, ""));
                    }
                }
            }
        }
        catch { /* fallback to handle scan */ }
        finally
        {
            if (session != 0) { try { RmEndSession(session); } catch { } }
        }
        return result;
    }
}
