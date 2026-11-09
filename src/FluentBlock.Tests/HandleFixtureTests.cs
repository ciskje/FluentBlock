using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using FluentBlock.Services;
using Xunit;
using Xunit.Abstractions;

namespace FluentBlock.Tests;

/// <summary>
/// Golden test: a file locked by this process must be reported both by
/// HandleEnumerator and by Sysinternals handle64.exe (-v CSV).
/// handle64 path: HANDLE64_PATH env, else C:\Temp\opencode\handle64.exe.
/// </summary>
public sealed class HandleFixtureTests
{
    private readonly ITestOutputHelper _out;

    public HandleFixtureTests(ITestOutputHelper output) => _out = output;

    [Fact(Timeout = 300000)]
    public async Task LockedFileAppearsInBothEnumerators()
    {
        string fixture = Path.Combine(Path.GetTempPath(), $"fluentBlock-golden-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(fixture, "golden");
        // Fresh files may be held exclusively by AV for a while: wait until
        // the file is freely openable, otherwise both our scan and handle64
        // (and even cleanup) race a third party instead of testing the code.
        await WaitForExclusiveAccessAsync(fixture, _out);
        using var locked = new FileStream(fixture, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        // Name queries can stall past the 100 ms app default under AV/load:
        // wait until AV releases the fresh file (probe above), then test with
        // the exact app default so the test proves what the app does.
        int prevTimeout = HandleEnumerator.QueryNameTimeoutMs;
        HandleEnumerator.QueryNameTimeoutMs = 100;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            var ours = await HandleEnumerator.GetHoldersForPathAsync(fixture, null, cts.Token);
            _out.WriteLine($"enumerator rows for fixture: {ours.Count}");
            Assert.Contains(ours, h => h.FilePath.Equals(fixture, StringComparison.OrdinalIgnoreCase));

            string handle64 = Environment.GetEnvironmentVariable("HANDLE64_PATH")
                ?? @"C:\Temp\opencode\handle64.exe";
            if (!File.Exists(handle64))
            {
                _out.WriteLine("handle64 missing, cross-check skipped");
                return;
            }
            int pid = Environment.ProcessId;
            var psi = new ProcessStartInfo(handle64, $"-p {pid} -v -nobanner")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi)!;
            string csv = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync(cts.Token);
            _out.WriteLine($"handle64 exit={proc.ExitCode}");
            bool found = csv.Split('\n').Any(line =>
                line.Split(',').LastOrDefault()?.Trim().Trim('"')
                    .Equals(fixture, StringComparison.OrdinalIgnoreCase) == true);
            Assert.True(found, "handle64.exe did not list the locked fixture file");
        }
        finally
        {
            HandleEnumerator.QueryNameTimeoutMs = prevTimeout;
            locked.Dispose();
            DeleteQuietly(fixture, _out);
        }
    }

    [Fact(Timeout = 300000)]
    public async Task WorkerPipeProtocolRoundtripFindsLockedFile()
    {
        string fixture = Path.Combine(Path.GetTempPath(), $"fluentBlock-worker-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(fixture, "worker-test");
        await WaitForExclusiveAccessAsync(fixture, _out);
        using var locked = new FileStream(fixture, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        string pipeName = $"FluentBlock-Test-{Guid.NewGuid():N}";
        try
        {
            ElevationService.WorkerMessage? finalMsg = null;
            Task workerTask;
            using (var server = new NamedPipeServerStream(
                pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
            {
                var waitTask = server.WaitForConnectionAsync();
                workerTask = Task.Run(() => ElevationService.RunWorker(pipeName, Environment.ProcessId));
                await waitTask;

                var utf8 = new UTF8Encoding(false);
                using var reader = new StreamReader(server, utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
                using var writer = new StreamWriter(server, utf8, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };

                var req = new ElevationService.WorkerRequest("path", fixture, 100, "it");
                await writer.WriteLineAsync(JsonSerializer.Serialize(req));

                while (await reader.ReadLineAsync() is { } line)
                {
                    var msg = JsonSerializer.Deserialize<ElevationService.WorkerMessage>(line);
                    if (msg?.Kind == "result")
                    {
                        finalMsg = msg;
                        break;
                    }
                }
            }
            await workerTask;

            Assert.NotNull(finalMsg);
            Assert.Null(finalMsg!.Error);
            Assert.NotNull(finalMsg.Holders);
            Assert.Contains(finalMsg.Holders!, h =>
                h.Pid == Environment.ProcessId &&
                h.FilePath.Equals(fixture, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(h.User));
        }
        finally
        {
            locked.Dispose();
            DeleteQuietly(fixture, _out);
        }
    }

    /// <summary>
    /// Waits until a fresh file is freely openable (up to ~60 s): AV often
    /// holds new temp files exclusively right after creation, which would
    /// otherwise stall name queries and cleanup for reasons unrelated to
    /// the code under test.
    /// </summary>
    private static async Task WaitForExclusiveAccessAsync(string path, ITestOutputHelper log)
    {
        for (int i = 0; i < 120; i++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open,
                    FileAccess.ReadWrite, FileShare.None);
                return;
            }
            catch (IOException) { await Task.Delay(500); }
        }
        log.WriteLine("exclusive probe timed out, proceeding best-effort");
    }

    /// <summary>Best-effort temp cleanup: AV may still hold fresh files exclusively.</summary>
    private static void DeleteQuietly(string path, ITestOutputHelper log)
    {
        for (int i = 0; i < 10 && File.Exists(path); i++)
        {
            try { File.Delete(path); return; }
            catch (IOException) { Thread.Sleep(500); }
        }
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { log.WriteLine($"cleanup skipped, file still held: {ex.Message}"); }
    }
}
