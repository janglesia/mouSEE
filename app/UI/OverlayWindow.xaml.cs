using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Controls;

namespace app.UI;

public partial class OverlayWindow : Window
{
    // ---- Win32 (click-through + hotkeys) ----
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020; // mouse clicks pass through
    private const int WS_EX_TOOLWINDOW = 0x00000080;  // hidden from Alt+Tab
    private const int WS_EX_NOACTIVATE = 0x08000000;  // never steals focus

    private const int HOTKEY_QUIT = 1, HOTKEY_PAUSE = 2, HOTKEY_CALIBRATIONS = 3, HOTKEY_RECENTER = 4, HOTKEY_FINETUNE = 5;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, VK_C = 0x43, VK_P = 0x50, VK_Q = 0x51, VK_R = 0x52, VK_F = 0x46;
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    //private const double OverlayMargin = 10; commented out by eri obsolete for bubble gaze

    // ---- Tracker (shared, owned by App) ----
    private readonly TrackerClient _tracker = ((App)Application.Current).Tracker;
    private readonly SettingsService _settings = ((App)Application.Current).Settings;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastUpdateMs, _fpsWindowStartMs;
    private int _framesInWindow;
    private double _fps;

    // ---- Tracking status bar ----
    private readonly TrackingMonitor _monitor;
    // where to sit: move back toward where calibration was, or re-center
    private readonly PositionGuide _guide = new();
    // the nudge and both head modes: the head nudges the bubble once the eyes settle (app/Bubble/HeadModes/)
    private readonly HeadNudge _nudge;
    // no where-to-sit tips while the head nudges the bubble (the tilting is on purpose then), and for a moment after
    // letting go, to get the head back
    public const long QuietAfterNudgeMs = 2000;
    private long _lastNudgedMs = long.MinValue / 2;
    private bool _guideQuiet;
    private static readonly Brush Following = Frozen("#FFFFFF"), Holding = Frozen("#F7D060");
    private readonly App _app = (App)Application.Current;
    // frames stopping can't be noticed from OnMessage, so check on a timer
    private readonly DispatcherTimer _timeoutTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private static readonly Brush Green = Frozen("#2E7D32"), Orange = Frozen("#EF6C00"),
                                  Red = Frozen("#C62828"), Grey = Frozen("#616161");

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        // text height changes (e.g. "no face" vs. full readout), so keep the bottom edge pinned
        //SizeChanged += (_, _) => PositionBottomLeft();
        //I made the overlay fullscreen for the gaze bubble so the line above is obsolete but i didn't want to delete in case we need to change things

        _monitor = new TrackingMonitor(_settings.Current.Advanced, _settings.Current.Tracking.Paused);
        _nudge = new HeadNudge(_settings.Current.Head);
        _monitor.Changed += UpdateStatusBar;
        UpdateStatusBar(); // could start out paused
        ApplyOverlaySettings(_settings.Current.Overlay);
        _timeoutTimer.Tick += (_, _) => _monitor.CheckTimeout();
        _settings.Changed += OnSettingsChanged;
        _guide.Changed += UpdateGuide;
        _guide.Calibrated = HeadPosition.Of(_app.Calibrations.Active);
        _guide.LastRecenter = _app.RecenteredAt;
        _app.OffsetChanged += OnRecentered;
        _app.Calibrations.Changed += OnCalibrationsChanged;

        Loaded += async (_, _) =>
        {
            //PositionBottomLeft();
            _timeoutTimer.Start();
            _tracker.MessageReceived += OnMessage;
            _tracker.LogReceived += OnLog;
            _tracker.Failed += OnFailed;
            // no-op if something else already started the tracker
            await ((App)Application.Current).StartTrackerAsync();
        };

        Closed += (_, _) =>
        {
            _timeoutTimer.Stop();
            _settings.Changed -= OnSettingsChanged;
            _app.OffsetChanged -= OnRecentered;
            _app.Calibrations.Changed -= OnCalibrationsChanged;
            _tracker.MessageReceived -= OnMessage;
            _tracker.LogReceived -= OnLog;
            _tracker.Failed -= OnFailed;
            var hwnd = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(hwnd, HOTKEY_QUIT);
            UnregisterHotKey(hwnd, HOTKEY_PAUSE);
            UnregisterHotKey(hwnd, HOTKEY_CALIBRATIONS);
            UnregisterHotKey(hwnd, HOTKEY_RECENTER);
            UnregisterHotKey(hwnd, HOTKEY_FINETUNE);
        };
    }

    // I made the overlay fullscreen for the gaze bubble and positioned it bottom left in the xaml so this function is obsolete
    // private void PositionBottomLeft()
    // {
    //     // WorkArea excludes the taskbar, so the overlay sits just above it
    //     var area = SystemParameters.WorkArea;
    //     Left = area.Left + OverlayMargin;
    //     Top = area.Bottom - ActualHeight - OverlayMargin;
    // }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        int style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

        // Ctrl+Alt+Q closes the overlay (needed because the window ignores the mouse and focus),
        // Ctrl+Alt+P pauses and unpauses, Ctrl+Alt+C opens the calibrations, Ctrl+Alt+R re-centers, Ctrl+Alt+F
        // fine-tunes. global, so they work from any app. q, p, c, r and f in the camera window do the same, see App.xaml.cs
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        RegisterHotKey(hwnd, HOTKEY_QUIT, MOD_CONTROL | MOD_ALT, VK_Q);
        RegisterHotKey(hwnd, HOTKEY_PAUSE, MOD_CONTROL | MOD_ALT, VK_P);
        RegisterHotKey(hwnd, HOTKEY_CALIBRATIONS, MOD_CONTROL | MOD_ALT, VK_C);
        RegisterHotKey(hwnd, HOTKEY_RECENTER, MOD_CONTROL | MOD_ALT, VK_R);
        RegisterHotKey(hwnd, HOTKEY_FINETUNE, MOD_CONTROL | MOD_ALT, VK_F);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY) return IntPtr.Zero;
        switch (wParam.ToInt32())
        {
            case HOTKEY_QUIT:
                handled = true;
                Close();
                break;
            case HOTKEY_PAUSE:
                handled = true;
                ((App)Application.Current).TogglePause();
                break;
            case HOTKEY_CALIBRATIONS:
                handled = true;
                ((App)Application.Current).ShowCalibrations();
                break;
            case HOTKEY_RECENTER:
                handled = true;
                ((App)Application.Current).StartRecenter();
                break;
            case HOTKEY_FINETUNE:
                handled = true;
                ((App)Application.Current).StartFineTune();
                break;
        }
        return IntPtr.Zero;
    }

    private void OnRecentered() => _guide.Recentered(_app.RecenteredAt);

    private void OnCalibrationsChanged() => _guide.Calibrated = HeadPosition.Of(_app.Calibrations.Active);

    private void UpdateGuide()
    {
        string? text = _guideQuiet ? null : _guide.MoveHint is { } move
            ? $"{move}. Tracking works best close to where you sat during calibration."
            : _guide.SuggestRecenter
                ? "You moved since the last re-center. Press Ctrl+Alt+R (or r in the camera window) to re-center."
                : null;
        GuideText.Text = text ?? "";
        GuideBar.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // hides the where-to-sit tips while nudging and for QuietAfterNudgeMs after
    private void QuietGuide(bool nudging)
    {
        long now = _clock.ElapsedMilliseconds;
        if (nudging) _lastNudgedMs = now;
        bool quiet = now - _lastNudgedMs < QuietAfterNudgeMs;
        if (quiet == _guideQuiet) return;
        _guideQuiet = quiet;
        UpdateGuide();
    }

    private void OnSettingsChanged(AppSettings old, AppSettings now)
    {
        if (old.Advanced != now.Advanced) _monitor.Configure(now.Advanced);
        if (old.Tracking.Paused != now.Tracking.Paused) _monitor.SetPaused(now.Tracking.Paused);
        if (old.Overlay != now.Overlay) ApplyOverlaySettings(now.Overlay);
        if (old.Head != now.Head)
        {
            _nudge.Settings = now.Head;
            if (old.Head.Mode != now.Head.Mode) _nudge.Reset();
        }
    }

    private void ApplyOverlaySettings(OverlaySettings overlay)
    {
        _guide.MoveAt = overlay.PositionHintPercent / 100.0;
        _guide.TiltAt = overlay.TiltHintDegrees;
        StatusBar.Visibility = overlay.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
        DebugPanel.Visibility = overlay.ShowDebugReadout ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- Tracker events ----
    private void OnLog(string line) => Debug.WriteLine($"[tracker] {line}"); // may be a background thread

    private void OnFailed(string reason)
    {
        DebugText.Text = reason;
        _monitor.OnTrackerStopped(reason);
    }

    private void UpdateGazeBubble(TrackerMessage msg)
    {
        var estimate = msg.Faces?.FirstOrDefault()?.Eyes?.Estimate;

        if (!_settings.Current.Cursor.ShowGazeBubble || _monitor.State == TrackingState.Paused
            || estimate is not { Valid: true, X: double x, Y: double y })
        {
            GazeBubble.Visibility = Visibility.Collapsed;
            _nudge.Reset();
            return;
        }

        // the fine-tune and the re-center's shift, then the head nudge (in the nudge and both head modes), then kept
        // within the screen. the bubble turns yellow while it holds on a spot and the head nudges it
        (x, y) = _app.Bubble(x, y);
        if (_settings.Current.Head.Mode.Nudges() && msg.Faces?.FirstOrDefault()?.Pose is { } pose)
            (x, y) = _nudge.Update(_clock.ElapsedMilliseconds, x, y, pose.Yaw, pose.Pitch);
        bool nudging = _nudge.Holding && _settings.Current.Head.Mode.Nudges();
        GazeBubble.Stroke = nudging ? Holding : Following;
        QuietGuide(nudging);
        x = Math.Clamp(x, 0.0, 1.0);
        y = Math.Clamp(y, 0.0, 1.0);

        // Convert normalized coordinates into canvas pixels.
        double left = x * GazeCanvas.ActualWidth - GazeBubble.Width / 2;
        double top = y * GazeCanvas.ActualHeight - GazeBubble.Height / 2;

        Canvas.SetLeft(GazeBubble, left);
        Canvas.SetTop(GazeBubble, top);

        GazeBubble.Visibility = Visibility.Visible;
    }


    private void OnMessage(TrackerMessage msg)
    {
        switch (msg.Type)
        {
            case "ready":
                DebugText.Text = $"Camera ready ({msg.Width}x{msg.Height})";
                break;

            case "error":
                DebugText.Text = $"Tracker error:\n{msg.Message}";
                // e.g. the camera can't be opened. the tracker exits right after
                _monitor.OnTrackerStopped(msg.Message ?? "error");
                break;

            case "frame":
                _monitor.OnFrame(msg.Tracking);
                UpdateGazeBubble(msg);
                _guide.OnFrame(FrameSample.From(msg, 0));
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

    private void UpdateStatusBar()
    {
        var (headline, hint) = TrackingMonitor.Describe(_monitor.State, _monitor.Reason);
        StatusHeadline.Text = headline;
        StatusHint.Text = hint;
        StatusHint.Visibility = hint == "" ? Visibility.Collapsed : Visibility.Visible;
        StatusBar.Background = _monitor.State switch
        {
            TrackingState.Tracking or TrackingState.Uncertain => Green,
            TrackingState.Starting or TrackingState.Paused => Grey,
            // closed eyes aren't an error, just nothing to track
            TrackingState.Recovering => Orange,
            TrackingState.Lost when _monitor.Reason == "eyes_closed" => Orange,
            _ => Red,
        };
    }

    private static Brush Frozen(string hex)
    {
        var brush = (Brush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
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