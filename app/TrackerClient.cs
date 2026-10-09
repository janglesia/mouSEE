using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace app;

/// <summary>
/// Owns the Python tracker process. There is one instance for the whole app (App.Tracker).
/// Call RunAsync from the UI thread: MessageReceived and Failed then fire on the UI thread.
/// LogReceived can fire on a background thread, so don't touch UI from it.
/// </summary>
public sealed class TrackerClient : IDisposable
{
    public event Action<TrackerMessage>? MessageReceived;
    public event Action<string>? LogReceived;
    public event Action<string>? Failed;

    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
    private readonly CancellationTokenSource _cts = new();
    private Process? _proc;
    private volatile string _lastLog = "";
    private bool _started;

    /// <summary>Starts the tracker and reads messages until it exits or Stop() is called. Safe to call twice.</summary>
    public async Task RunAsync(string[] trackerArgs)
    {
        if (_started) return;
        _started = true;
        var ct = _cts.Token;

        string? trackerDir = FindTrackerDir();
        if (trackerDir is null)
        {
            Failed?.Invoke("Can't find tracker/face_tracker.py");
            return;
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

        try
        {
            _proc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            Failed?.Invoke($"Failed to start tracker:\n{ex.Message}");
            return;
        }
        if (_proc is null)
        {
            Failed?.Invoke("Failed to start tracker.");
            return;
        }

        _proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            _lastLog = e.Data;
            LogReceived?.Invoke(e.Data);
        };
        _proc.BeginErrorReadLine();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? line = await _proc.StandardOutput.ReadLineAsync(ct);
                if (line is null)
                {
                    if (!ct.IsCancellationRequested) Failed?.Invoke($"Tracker exited.\n{_lastLog}");
                    break;
                }

                TrackerMessage? msg;
                try
                {
                    msg = JsonSerializer.Deserialize<TrackerMessage>(line, _json);
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
    }

    public void Stop()
    {
        _cts.Cancel();
        try
        {
            if (_proc is { HasExited: false }) _proc.Kill(entireProcessTree: true);
        }
        catch { /* already gone */ }
    }

    public void Dispose()
    {
        Stop();
        _cts.Dispose();
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