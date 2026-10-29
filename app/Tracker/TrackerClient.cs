using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace app.Tracker;

/// <summary>
/// Owns the Python tracker process. There is one instance for the whole app (App.Tracker).
/// Call RunAsync / RestartAsync from the UI thread: MessageReceived and Failed then fire on the UI thread.
/// LogReceived can fire on a background thread, so don't touch UI from it.
/// Subscribers stay subscribed across a restart.
/// </summary>
public sealed class TrackerClient : IDisposable
{
    public event Action<TrackerMessage>? MessageReceived;
    public event Action<string>? LogReceived;
    public event Action<string>? Failed;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private CancellationTokenSource? _runCts; // one per run, so stopping one run can't cancel the next
    private Task? _run;
    private Process? _proc;
    private volatile string _lastLog = "";
    private bool _disposed;

    /// <summary>Starts the tracker and reads messages until it exits or Stop() is called.
    /// Does nothing if it's already been started (use RestartAsync to change the args).</summary>
    public async Task RunAsync(string[] trackerArgs)
    {
        if (_run is not null || _disposed) return;
        await (_run = ReadLoopAsync(trackerArgs));
    }

    /// <summary>Stops the tracker and starts it again with new args, e.g. after the camera changed.
    /// Returns once the old process is gone, the new one keeps running in the background.</summary>
    public async Task RestartAsync(string[] trackerArgs)
    {
        if (_disposed) return;
        var previous = _run;
        StopProcess();
        if (previous is not null) await previous; // ReadLoopAsync doesn't throw
        if (_disposed) return;
        _run = ReadLoopAsync(trackerArgs);
    }

    private async Task ReadLoopAsync(string[] trackerArgs)
    {
        var cts = new CancellationTokenSource();
        _runCts = cts;
        var ct = cts.Token;

        var psi = CreateStartInfo(trackerArgs, out string? error);
        if (psi is null)
        {
            Failed?.Invoke(error!);
            return;
        }

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            Failed?.Invoke($"Failed to start tracker:\n{ex.Message}");
            return;
        }
        if (proc is null)
        {
            Failed?.Invoke("Failed to start tracker.");
            return;
        }
        _proc = proc;

        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            _lastLog = e.Data;
            LogReceived?.Invoke(e.Data);
        };
        proc.BeginErrorReadLine();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? line = await proc.StandardOutput.ReadLineAsync(ct);
                if (line is null)
                {
                    if (!ct.IsCancellationRequested) Failed?.Invoke($"Tracker exited.\n{_lastLog}");
                    break;
                }

                TrackerMessage? msg;
                try
                {
                    msg = JsonSerializer.Deserialize<TrackerMessage>(line, Json);
                }
                catch (JsonException ex)
                {
                    // if a records mismatch ever drops every frame, this is where you'll see why
                    string shown = line.Length > 200 ? line[..200] + "..." : line;
                    LogReceived?.Invoke($"[bad line] {ex.Message} | {shown}");
                    continue;
                }
                if (msg is not null) MessageReceived?.Invoke(msg);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Failed?.Invoke($"Tracker loop failed:\n{ex.Message}");
        }
        finally
        {
            Kill(proc);
            proc.Dispose();
            if (_proc == proc) _proc = null;
            cts.Dispose();
            if (_runCts == cts) _runCts = null;
        }
    }

    public void Stop()
    {
        StopProcess();
    }

    private void StopProcess()
    {
        try { _runCts?.Cancel(); } catch (ObjectDisposedException) { }
        if (_proc is { } proc) Kill(proc);
    }

    private static void Kill(Process proc)
    {
        try
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
        }
        catch { /* already gone */ }
    }

    public void Dispose()
    {
        _disposed = true;
        StopProcess();
    }

    /// <summary>
    /// The cameras face_tracker.py --list-cameras can open. Takes a couple of seconds since it opens each one.
    /// A camera that's already in use (like the one the tracker has) might not be listed.
    /// </summary>
    public static async Task<IReadOnlyList<CameraInfo>> ListCamerasAsync(CancellationToken ct = default)
    {
        var psi = CreateStartInfo(["--list-cameras"], out _);
        if (psi is null) return [];

        using var proc = Process.Start(psi);
        if (proc is null) return [];
        proc.ErrorDataReceived += (_, _) => { }; // OpenCV warns on stderr for every index that doesn't open
        proc.BeginErrorReadLine();

        try
        {
            while (await proc.StandardOutput.ReadLineAsync(ct) is { } line)
            {
                TrackerMessage? msg;
                try { msg = JsonSerializer.Deserialize<TrackerMessage>(line, Json); }
                catch (JsonException) { continue; }
                if (msg is { Type: "cameras" }) return msg.Cameras ?? [];
            }
            return [];
        }
        finally
        {
            Kill(proc);
        }
    }

    private static ProcessStartInfo? CreateStartInfo(IEnumerable<string> trackerArgs, out string? error)
    {
        string? trackerDir = FindTrackerDir();
        if (trackerDir is null)
        {
            error = "Can't find tracker/face_tracker.py";
            return null;
        }

        string scriptPath = Path.Combine(trackerDir, "face_tracker.py");
        string venvPython = Path.Combine(trackerDir, ".venv", "Scripts", "python.exe");
        string pythonExe = File.Exists(venvPython) ? venvPython : "python";

        var psi = new ProcessStartInfo
        {
            FileName = pythonExe,
            ArgumentList = { "-u", scriptPath },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = trackerDir,
        };
        // pass args through to the tracker (--preview, --camera 1, ...)
        foreach (var arg in trackerArgs) psi.ArgumentList.Add(arg);
        error = null;
        return psi;
    }

    // Looks for a "tracker" folder with face_tracker.py, walking up from the exe and the working dir
    private static string? FindTrackerDir()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                string candidate = Path.Combine(dir.FullName, "tracker");
                if (File.Exists(Path.Combine(candidate, "face_tracker.py"))) return candidate;

                // sibling of app/ (the original layout)
                string sibling = Path.Combine(dir.FullName, "..", "tracker");
                if (File.Exists(Path.Combine(sibling, "face_tracker.py"))) return Path.GetFullPath(sibling);

                dir = dir.Parent;
            }
        }
        return null;
    }
}
