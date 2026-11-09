using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using FluentBlock.Models;

namespace FluentBlock.Services;

/// <summary>
/// Manages an on-demand, headless elevated worker process (--worker) over a
/// local Named Pipe so the WPF UI always stays at standard-user integrity
/// (preserving Explorer OLE drag &amp; drop and window state).
/// </summary>
public static class ElevationService
{
    private static readonly SemaphoreSlim PipeLock = new(1, 1);
    private static NamedPipeServerStream? _pipe;
    private static StreamReader? _reader;
    private static StreamWriter? _writer;
    private static Process? _workerProc;

    public static event Action? StateChanged;

    public static bool IsElevatedOrWorkerActive =>
        ThemeService.IsElevated()
        || (_pipe is { IsConnected: true } && _workerProc is { HasExited: false });

    private static string ExePath =>
        Environment.ProcessPath
        ?? Process.GetCurrentProcess().MainModule?.FileName
        ?? "";

    public sealed record WorkerRequest(string Op, string Target, int TimeoutMs, string Lang);

    public sealed record WorkerMessage(
        string Kind,
        string? Text,
        List<LockHolder>? Holders,
        EjectService.EjectResult? Eject,
        string? Error,
        List<HandleEnumerator.SkippedProcess>? Protected = null);

    private sealed class SyncProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }

    /// <summary>Starts the background elevated worker if not already running.</summary>
    public static async Task<bool> EnsureWorkerAsync(CancellationToken ct = default)
    {
        if (ThemeService.IsElevated()) return true;
        await PipeLock.WaitAsync(ct);
        try
        {
            return await EnsureConnectedCoreAsync(ct);
        }
        finally
        {
            PipeLock.Release();
        }
    }

    public static async Task<List<LockHolder>?> TryScanDriveElevatedAsync(
        string driveLetter, IProgress<string>? progress, CancellationToken ct)
    {
        if (ThemeService.IsElevated())
            return await HandleEnumerator.GetHoldersForDriveAsync(driveLetter, progress, ct);
        var msg = await SendRequestAsync("drive", driveLetter, progress, ct);
        return msg?.Holders;
    }

    /// <summary>Drive scan with unopenable (protected) processes included.</summary>
    public static async Task<HandleEnumerator.DriveScanResult?> TryScanDriveDetailedElevatedAsync(
        string driveLetter, IProgress<string>? progress, CancellationToken ct)
    {
        if (ThemeService.IsElevated())
            return await HandleEnumerator.GetDriveScanDetailedAsync(driveLetter, progress, ct);
        var msg = await SendRequestAsync("drive", driveLetter, progress, ct);
        if (msg?.Holders == null) return null;
        return new HandleEnumerator.DriveScanResult(msg.Holders, msg.Protected ?? []);
    }

    public static async Task<List<LockHolder>?> TryScanPathElevatedAsync(
        string path, IProgress<string>? progress, CancellationToken ct)
    {
        if (ThemeService.IsElevated())
            return await HandleEnumerator.GetHoldersForPathAsync(path, progress, ct);
        var msg = await SendRequestAsync("path", path, progress, ct);
        return msg?.Holders;
    }

    public static async Task<EjectService.EjectResult?> TryEjectElevatedAsync(
        string driveLetter, CancellationToken ct)
    {
        if (ThemeService.IsElevated())
            return await EjectService.TryEjectAsync(driveLetter, ct);
        var msg = await SendRequestAsync("eject", driveLetter, null, ct);
        return msg?.Eject;
    }

    private static async Task<WorkerMessage?> SendRequestAsync(
        string op, string target, IProgress<string>? progress, CancellationToken ct)
    {
        await PipeLock.WaitAsync(ct);
        try
        {
            if (!await EnsureConnectedCoreAsync(ct) || _writer == null || _reader == null)
                return null;

            var req = new WorkerRequest(
                op,
                target,
                HandleEnumerator.QueryNameTimeoutMs,
                CultureInfo.CurrentUICulture.Name);

            await _writer.WriteLineAsync(JsonSerializer.Serialize(req));
            while (await _reader.ReadLineAsync() is { } line)
            {
                var msg = JsonSerializer.Deserialize<WorkerMessage>(line);
                if (msg == null) continue;
                if (msg.Kind == "progress")
                {
                    if (msg.Text != null) progress?.Report(msg.Text);
                    continue;
                }
                if (msg.Kind == "result")
                {
                    ct.ThrowIfCancellationRequested();
                    if (!string.IsNullOrEmpty(msg.Error))
                        throw new InvalidOperationException(msg.Error);
                    return msg;
                }
            }
            ResetWorkerCore();
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Info($"worker ipc error: {ex.Message}");
            ResetWorkerCore();
            return null;
        }
        finally
        {
            PipeLock.Release();
        }
    }

    private static async Task<bool> EnsureConnectedCoreAsync(CancellationToken ct)
    {
        if (_pipe is { IsConnected: true } && _workerProc is { HasExited: false })
            return true;

        ResetWorkerCore();
        string pipeName = $"FluentBlock-{Environment.ProcessId}-{Guid.NewGuid():N}";
        var server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        try
        {
            var waitTask = server.WaitForConnectionAsync(ct);
            var psi = new ProcessStartInfo(ExePath, $"--worker {pipeName} {Environment.ProcessId}")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            var proc = Process.Start(psi);
            if (proc == null)
            {
                server.Dispose();
                return false;
            }
            proc.EnableRaisingEvents = true;
            proc.Exited += (_, _) => RaiseStateChanged();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
            await waitTask.WaitAsync(timeoutCts.Token);

            var utf8 = new UTF8Encoding(false);
            _pipe = server;
            _workerProc = proc;
            _reader = new StreamReader(server, utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
            _writer = new StreamWriter(server, utf8, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
            Log.Info($"elevated worker connected pid={proc.Id}");
            RaiseStateChanged();
            return true;
        }
        catch (Exception ex)
        {
            Log.Info($"elevated worker start declined/failed: {ex.Message}");
            try { server.Dispose(); } catch { }
            return false;
        }
    }

    public static void ShutdownWorker()
    {
        ResetWorkerCore();
    }

    private static void ResetWorkerCore()
    {
        bool hadWorker = _pipe != null || _workerProc != null;
        try { _writer?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }
        try { _pipe?.Dispose(); } catch { }
        try
        {
            if (_workerProc is { HasExited: false })
                _workerProc.Kill();
        }
        catch { }
        try { _workerProc?.Dispose(); } catch { }
        _writer = null;
        _reader = null;
        _pipe = null;
        _workerProc = null;
        if (hadWorker) RaiseStateChanged();
    }

    private static void RaiseStateChanged()
    {
        try { StateChanged?.Invoke(); } catch { }
    }

    /// <summary>Headless entry point for the elevated --worker process.</summary>
    public static void RunWorker(string pipeName, int parentPid)
    {
        // Auto-terminate immediately if the UI parent exits.
        _ = Task.Run(() =>
        {
            try
            {
                using var parent = Process.GetProcessById(parentPid);
                parent.WaitForExit();
            }
            catch { }
            Environment.Exit(0);
        });

        HandleEnumerator.EnableDebugPrivilege();

        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            client.Connect(15000);
            var utf8 = new UTF8Encoding(false);
            using var reader = new StreamReader(client, utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
            using var writer = new StreamWriter(client, utf8, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
            var writeSync = new object();

            while (reader.ReadLine() is { } line)
            {
                WorkerRequest? req;
                try { req = JsonSerializer.Deserialize<WorkerRequest>(line); }
                catch { continue; }
                if (req == null) continue;

                HandleEnumerator.QueryNameTimeoutMs = req.TimeoutMs;
                if (!string.IsNullOrEmpty(req.Lang))
                    L10n.SetLanguage(req.Lang);

                var progress = new SyncProgress(text =>
                {
                    lock (writeSync)
                    {
                        writer.WriteLine(JsonSerializer.Serialize(
                            new WorkerMessage("progress", text, null, null, null)));
                    }
                });

                WorkerMessage reply;
                try
                {
                    reply = req.Op switch
                    {
                        "drive" => ScanDriveDetailed(req.Target, progress),
                        "path" => new WorkerMessage(
                            "result", null,
                            HandleEnumerator.GetHoldersForPathAsync(req.Target, progress, CancellationToken.None).GetAwaiter().GetResult(),
                            null, null),
                        "eject" => new WorkerMessage(
                            "result", null, null,
                            EjectService.TryEjectAsync(req.Target, CancellationToken.None).GetAwaiter().GetResult(),
                            null),
                        _ => new WorkerMessage("result", null, null, null, $"Unknown op: {req.Op}")
                    };
                }
                catch (Exception ex)
                {
                    reply = new WorkerMessage("result", null, null, null, ex.Message);
                }

                lock (writeSync)
                {
                    writer.WriteLine(JsonSerializer.Serialize(reply));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Info($"worker exit: {ex.Message}");
        }
    }

    private static WorkerMessage ScanDriveDetailed(string target, SyncProgress progress)
    {
        var r = HandleEnumerator.GetDriveScanDetailedAsync(
            target, progress, CancellationToken.None).GetAwaiter().GetResult();
        return new WorkerMessage("result", null, r.Holders, null, null, r.Unopenable);
    }
}
