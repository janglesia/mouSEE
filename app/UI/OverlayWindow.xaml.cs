using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;

namespace app.UI;

public partial class OverlayWindow : Window
{
    // ---- Win32 (click-through + quit hotkey) ----
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020; // mouse clicks pass through
    private const int WS_EX_TOOLWINDOW = 0x00000080;  // hidden from Alt+Tab
    private const int WS_EX_NOACTIVATE = 0x08000000;  // never steals focus

    private const int HOTKEY_ID = 1;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, VK_Q = 0x51;
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    // ---- Tracker ----
    private readonly CancellationTokenSource _cts = new();
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
    private Process? _proc;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        // text height changes (e.g. "no face" vs. full readout), so keep the bottom edge pinned
        SizeChanged += (_, _) => PositionBottomLeft();
        Loaded += async (_, _) =>
        {
            PositionBottomLeft();
            await RunTrackerAsync(_cts.Token);
        };
        Closing += (_, _) => StopTracker();
        Closed += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(hwnd, HOTKEY_ID);
        };
    }

    private const double OverlayMargin = 10;

    private void PositionBottomLeft()
    {
        // WorkArea excludes the taskbar, so the overlay sits just above it
        var area = SystemParameters.WorkArea;
        Left = area.Left + OverlayMargin;
        Top = area.Bottom - ActualHeight - OverlayMargin;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        int style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

        // Ctrl+Alt+Q closes the overlay (needed because the window ignores the mouse and focus)
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        RegisterHotKey(hwnd, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_Q);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            handled = true;
            Close();
        }
        return IntPtr.Zero;
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

    private async Task RunTrackerAsync(CancellationToken ct)
    {
        string? trackerDir = FindTrackerDir();
        if (trackerDir is null)
        {
            DebugText.Text = "Can't find tracker/face_tracker.py";
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
        // pass command-line args through to the tracker (--preview, --camera 1, ...)
        foreach (var arg in Environment.GetCommandLineArgs().Skip(1)) psi.ArgumentList.Add(arg);

        try
        {
            _proc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            DebugText.Text = $"Failed to start tracker:\n{ex.Message}";
            return;
        }
        if (_proc is null) { DebugText.Text = "Failed to start tracker."; return; }

        string lastLog = "";
        _proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            Debug.WriteLine($"[tracker] {e.Data}");
            lastLog = e.Data; // read on the UI thread only when we show errors
        };
        _proc.BeginErrorReadLine();

        var clock = Stopwatch.StartNew();
        long lastUpdateMs = 0, fpsWindowStartMs = 0;
        int framesInWindow = 0;
        double fps = 0;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? line = await _proc.StandardOutput.ReadLineAsync(ct);
                if (line is null)
                {
                    DebugText.Text = $"Tracker exited.\n{lastLog}";
                    break;
                }

                TrackerMessage? msg;
                try { msg = JsonSerializer.Deserialize<TrackerMessage>(line, _json); }
                catch (JsonException) { Debug.WriteLine($"[bad line] {line}"); continue; }
                if (msg is null) continue;

                switch (msg.Type)
                {
                    case "ready":
                        DebugText.Text = $"Camera ready ({msg.Width}x{msg.Height})";
                        break;

                    case "error":
                        DebugText.Text = $"Tracker error:\n{msg.Message}";
                        break;

                    case "frame":
                        framesInWindow++;
                        long now = clock.ElapsedMilliseconds;
                        if (now - fpsWindowStartMs >= 1000)
                        {
                            fps = framesInWindow * 1000.0 / (now - fpsWindowStartMs);
                            framesInWindow = 0;
                            fpsWindowStartMs = now;
                        }

                        // update the text ~10 times a second
                        if (now - lastUpdateMs >= 100)
                        {
                            lastUpdateMs = now;
                            DebugText.Text = FormatFrame(msg, fps, FormatLatency(msg.Wall));
                        }
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            DebugText.Text = $"Tracker loop failed:\n{ex.Message}";
        }
    }

    // "wall" is Unix ms stamped by the tracker right after it reads the camera frame.
    // Latency = time from that moment until this app received the frame's message.
    private static string FormatLatency(long wallMs)
    {
        if (wallMs == 0) return "n/a (no wall field)";
        long latencyMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - wallMs;
        return $"{latencyMs} ms";
    }

    private static string FormatFrame(TrackerMessage msg, double fps, string latency)
    {
        string header = $"{fps,5:0.0} fps | latency {latency}";

        if (msg.Faces is not { Count: > 0 } faces)
            return $"{header}\nno face";

        var f = faces[0];
        var p = f.Pose;
        double blinkL = f.Blendshapes.GetValueOrDefault("eyeBlinkLeft");
        double blinkR = f.Blendshapes.GetValueOrDefault("eyeBlinkRight");

        return $"{header}\n" +
               $"yaw {p.Yaw,5:0}  pitch {p.Pitch,5:0}  roll {p.Roll,5:0}\n" +
               $"iris L ({f.Irises.Left[0]:0.000}, {f.Irises.Left[1]:0.000})\n" +
               $"iris R ({f.Irises.Right[0]:0.000}, {f.Irises.Right[1]:0.000})\n" +
               $"blink L {blinkL:0.00}  R {blinkR:0.00}";
    }

    private void StopTracker()
    {
        _cts.Cancel();
        try
        {
            if (_proc is { HasExited: false }) _proc.Kill(entireProcessTree: true);
        }
        catch { /* already gone */ }
    }
}