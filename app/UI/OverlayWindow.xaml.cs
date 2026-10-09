using System.Diagnostics;
using System.Runtime.InteropServices;
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

    private const double OverlayMargin = 10;

    // ---- Tracker (shared, owned by App) ----
    private readonly TrackerClient _tracker = ((App)Application.Current).Tracker;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastUpdateMs, _fpsWindowStartMs;
    private int _framesInWindow;
    private double _fps;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        // text height changes (e.g. "no face" vs. full readout), so keep the bottom edge pinned
        SizeChanged += (_, _) => PositionBottomLeft();

        Loaded += async (_, _) =>
        {
            PositionBottomLeft();
            _tracker.MessageReceived += OnMessage;
            _tracker.LogReceived += OnLog;
            _tracker.Failed += OnFailed;
            // no-op if something else already started the tracker
            await _tracker.RunAsync(Environment.GetCommandLineArgs().Skip(1).ToArray());
        };

        Closed += (_, _) =>
        {
            _tracker.MessageReceived -= OnMessage;
            _tracker.LogReceived -= OnLog;
            _tracker.Failed -= OnFailed;
            var hwnd = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(hwnd, HOTKEY_ID);
        };
    }

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

    // ---- Tracker events ----
    private void OnLog(string line) => Debug.WriteLine($"[tracker] {line}"); // may be a background thread

    private void OnFailed(string reason) => DebugText.Text = reason;

    private void OnMessage(TrackerMessage msg)
    {
        switch (msg.Type)
        {
            case "ready":
                DebugText.Text = $"Camera ready ({msg.Width}x{msg.Height})";
                break;

            case "error":
                DebugText.Text = $"Tracker error:\n{msg.Message}";
                break;

            case "frame":
                _framesInWindow++;
                long now = _clock.ElapsedMilliseconds;
                if (now - _fpsWindowStartMs >= 1000)
                {
                    _fps = _framesInWindow * 1000.0 / (now - _fpsWindowStartMs);
                    _framesInWindow = 0;
                    _fpsWindowStartMs = now;
                }

                // update the text ~10 times a second
                if (now - _lastUpdateMs >= 100)
                {
                    _lastUpdateMs = now;
                    DebugText.Text = FormatFrame(msg, _fps, FormatLatency(msg.Wall));
                }
                break;
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
        //messages FPS: how frequently the C# app receives frame messages.
        //capture FPS: how frequently the Python tracker successfully reads frames from the camera, measured over approximately one-second windows.
        //landmark ms: how long MediaPipe's inference call takes for a frame.
        //latency: existing elapsed time from the Python wall timestamp to the C# app's current time.
        string header = $"messages {fps,5:0.0} fps | " +
                        $"capture {msg.CaptureFps,5:0.0} fps | " +
                        $"landmark {msg.LandmarkMs,6:0.0} ms | " +
                        $"latency {latency}";

        if (msg.Faces is not { Count: > 0 } faces)
            return $"{header}\nno face";

        var f = faces[0];
        var p = f.Pose;
        double blinkL = f.Blendshapes.GetValueOrDefault("eyeBlinkLeft");
        double blinkR = f.Blendshapes.GetValueOrDefault("eyeBlinkRight");

        // from eye_extractor.py; this line also confirms the Eyes records are parsing
        var gz = f.Eyes?.Gaze;
        string gazeLine = gz is { H: double gh, V: double gv }
            ? $"gaze h {gh:+0.000;-0.000;+0.000}  v {gv:+0.000;-0.000;+0.000}  {gz.Direction}"
            : "gaze n/a (eyes closed or Eyes not parsed)";

        return $"{header}\n" +
               $"yaw {p.Yaw,5:0}  pitch {p.Pitch,5:0}  roll {p.Roll,5:0}\n" +
               $"iris L ({f.Irises.Left[0]:0.000}, {f.Irises.Left[1]:0.000})\n" +
               $"iris R ({f.Irises.Right[0]:0.000}, {f.Irises.Right[1]:0.000})\n" +
               $"blink L {blinkL:0.00}  R {blinkR:0.00}\n" +
               gazeLine;
    }
}