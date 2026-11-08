using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using FluentSblock.Services;
using Xunit;
using Xunit.Abstractions;

namespace FluentSblock.Tests;

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
        string fixture = Path.Combine(Path.GetTempPath(), $"fluentSblock-golden-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(fixture, "golden");
        using var locked = new FileStream(fixture, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        // Name queries can stall past the 100 ms app default under AV/load
        // (fresh temp file); give the test headroom, restore afterwards.
        int prevTimeout = HandleEnumerator.QueryNameTimeoutMs;
        HandleEnumerator.QueryNameTimeoutMs = 250;
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
            File.Delete(fixture);
        }
    }

    [Fact(Timeout = 180000)]
    public async Task WorkerPipeProtocolRoundtripFindsLockedFile()
    {
        string fixture = Path.Combine(Path.GetTempPath(), $"fluentSblock-worker-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(fixture, "worker-test");
        using var locked = new FileStream(fixture, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        string pipeName = $"FluentSblock-Test-{Guid.NewGuid():N}";
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

                var req = new ElevationService.WorkerRequest("path", fixture, 200, "it");
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
            File.Delete(fixture);
        }
    }
}
