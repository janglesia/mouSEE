using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using app.Input;

namespace app.UI;

public partial class OverlayWindow : Window
{
    // ---- Win32 (click-through + hotkeys) ----
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020; // mouse clicks pass through
    private const int WS_EX_TOOLWINDOW = 0x00000080;  // hidden from Alt+Tab
    private const int WS_EX_NOACTIVATE = 0x08000000;  // never steals focus

    private const int HOTKEY_QUIT = 1, HOTKEY_PAUSE = 2, HOTKEY_CALIBRATIONS = 3,
                      HOTKEY_RECENTER = 4, HOTKEY_FINETUNE = 5, HOTKEY_CURSOR = 6,
                      HOTKEY_CLICK = 7, HOTKEY_WHEEL = 8, HOTKEY_TEST_MODE = 9;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2,
                       VK_C = 0x43, VK_P = 0x50, VK_Q = 0x51, VK_R = 0x52,
                       VK_F = 0x46, VK_M = 0x4D, VK_L = 0x4C, VK_W = 0x57, VK_T = 0x54;
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    // ---- Tracker (shared, owned by App) ----
    private readonly TrackerClient _tracker = ((App)Application.Current).Tracker;
    private readonly SettingsService _settings = ((App)Application.Current).Settings;
    private readonly CursorController _cursorController = new();
    private readonly IClickInjector _clicker = new MouseClicker();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastUpdateMs, _fpsWindowStartMs;
    private int _framesInWindow;
    private double _fps;

    // ---- Action Wheel Dwell Trigger ----
    private Point _lastGazeCanvasPoint;
    private Point _dwellAnchor;
    private long _dwellStartMs;
    private long _wheelClosedMs = long.MinValue / 2;
    private const double DwellRadiusPx = 35.0; // Max eye jitter allowed while dwelling to open
    private const long DwellToOpenMs = 1000;   // Hold gaze for 1s to pop open the wheel
    private const long WheelCooldownMs = 800;  // Brief pause after closing before re-opening

    // ---- Mouse test mode ----
    private bool _isMouseTestMode = false;

    // ---- Tracking status bar ----
    private readonly TrackingMonitor _monitor;
    private readonly PositionGuide _guide = new();
    private readonly HeadNudge _nudge;
    // the shake taken out of the bubble (cursor.smoothing, cursor.sensitivity), see app/Bubble/OneEuroFilter.cs
    private readonly GazeSmoothing _smoothing;
    // no where-to-sit tips while the head nudges the bubble (the tilting is on purpose then), and for a moment after
    // letting go, to get the head back
    public const long QuietAfterNudgeMs = 2000;
    private long _lastNudgedMs = long.MinValue / 2;
    private bool _guideQuiet;
    private static readonly Brush Following = Frozen("#FFFFFF"), Holding = Frozen("#F7D060");
    private readonly App _app = (App)Application.Current;
    private readonly DispatcherTimer _timeoutTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private static readonly Brush Green = Frozen("#2E7D32"), Orange = Frozen("#EF6C00"),
                                  Red = Frozen("#C62828"), Grey = Frozen("#616161");

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;

        _monitor = new TrackingMonitor(_settings.Current.Advanced, _settings.Current.Tracking.Paused);
        _nudge = new HeadNudge(_settings.Current.Head);
        _smoothing = new GazeSmoothing(_settings.Current.Cursor.Sensitivity);
        _monitor.Changed += UpdateStatusBar;
        UpdateStatusBar();
        ApplyOverlaySettings(_settings.Current.Overlay);
        _timeoutTimer.Tick += (_, _) => _monitor.CheckTimeout();
        _settings.Changed += OnSettingsChanged;
        _guide.Changed += UpdateGuide;
        _guide.Calibrated = HeadPosition.Of(_app.Calibrations.Active);
        _guide.LastRecenter = _app.RecenteredAt;
        _app.OffsetChanged += OnRecentered;
        _app.Calibrations.Changed += OnCalibrationsChanged;
        _app.CursorMovementChanged += OnCursorMovementChanged;
        ClickWheel.ActionSelected += OnWheelActionSelected;

        // Mouse testing hooks
        MouseMove += OnOverlayMouseMove;
        MouseDown += OnOverlayMouseDown;

        Loaded += async (_, _) =>
        {
            _timeoutTimer.Start();
            _tracker.MessageReceived += OnMessage;
            _tracker.LogReceived += OnLog;
            _tracker.Failed += OnFailed;
            await ((App)Application.Current).StartTrackerAsync();
        };

        Closed += (_, _) =>
        {
            _timeoutTimer.Stop();
            _settings.Changed -= OnSettingsChanged;
            _app.OffsetChanged -= OnRecentered;
            _app.Calibrations.Changed -= OnCalibrationsChanged;
            _app.CursorMovementChanged -= OnCursorMovementChanged;
            ClickWheel.ActionSelected -= OnWheelActionSelected;
            MouseMove -= OnOverlayMouseMove;
            MouseDown -= OnOverlayMouseDown;
            _tracker.MessageReceived -= OnMessage;
            _tracker.LogReceived -= OnLog;
            _tracker.Failed -= OnFailed;
            var hwnd = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(hwnd, HOTKEY_QUIT);
            UnregisterHotKey(hwnd, HOTKEY_PAUSE);
            UnregisterHotKey(hwnd, HOTKEY_CALIBRATIONS);
            UnregisterHotKey(hwnd, HOTKEY_RECENTER);
            UnregisterHotKey(hwnd, HOTKEY_FINETUNE);
            UnregisterHotKey(hwnd, HOTKEY_CURSOR);
            UnregisterHotKey(hwnd, HOTKEY_CLICK);
            UnregisterHotKey(hwnd, HOTKEY_WHEEL);
            UnregisterHotKey(hwnd, HOTKEY_TEST_MODE);
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        int style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        RegisterHotKey(hwnd, HOTKEY_QUIT, MOD_CONTROL | MOD_ALT, VK_Q);
        RegisterHotKey(hwnd, HOTKEY_PAUSE, MOD_CONTROL | MOD_ALT, VK_P);
        RegisterHotKey(hwnd, HOTKEY_CALIBRATIONS, MOD_CONTROL | MOD_ALT, VK_C);
        RegisterHotKey(hwnd, HOTKEY_RECENTER, MOD_CONTROL | MOD_ALT, VK_R);
        RegisterHotKey(hwnd, HOTKEY_FINETUNE, MOD_CONTROL | MOD_ALT, VK_F);
        RegisterHotKey(hwnd, HOTKEY_CURSOR, MOD_CONTROL | MOD_ALT, VK_M);
        RegisterHotKey(hwnd, HOTKEY_CLICK, MOD_CONTROL | MOD_ALT, VK_L); // Ctrl+Alt+L for left click
        RegisterHotKey(hwnd, HOTKEY_WHEEL, MOD_CONTROL | MOD_ALT, VK_W);
        RegisterHotKey(hwnd, HOTKEY_TEST_MODE, MOD_CONTROL | MOD_ALT, VK_T);
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
                ((App)Application.Current).StartAccuracyTest();
                break;
            case HOTKEY_CURSOR:
                handled = true;
                ((App)Application.Current).ToggleCursorMovement();
                break;
            case HOTKEY_CLICK:
                handled = true;
                _ = ClickAfterDelayAsync();
                break;
            case HOTKEY_WHEEL:
                handled = true;
                ToggleWheelManual();
                break;
            case HOTKEY_TEST_MODE:
                handled = true;
                ToggleMouseTestMode();
                break;
        }

        return IntPtr.Zero;
    }

    /// <summary>The one place a click is allowed: only while the cursor follows the gaze and tracking is good.</summary>
    public bool TryClick() =>
        _app.IsCursorMovementEnabled && _monitor.CanMoveCursor && _clicker.LeftClick();

    // Temporary. The hotkey fires while Ctrl+Alt are still held, so wait for them to be let go
    // or the target app would see Ctrl+Alt+click.
    private async Task ClickAfterDelayAsync()
    {
        await Task.Delay(1000);
        TryClick();
    }

    private void ToggleWheelManual()
    {
        if (ClickWheel.IsOpen)
        {
            ClickWheel.Close();
            _wheelClosedMs = _clock.ElapsedMilliseconds;
        }
        else
        {
            Point target = _lastGazeCanvasPoint.X > 0 && _lastGazeCanvasPoint.Y > 0
                ? _lastGazeCanvasPoint
                : new Point(GazeCanvas.ActualWidth / 2, GazeCanvas.ActualHeight / 2);
            ClickWheel.OpenAt(target);
        }
    }

    private void ToggleMouseTestMode()
    {
        _isMouseTestMode = !_isMouseTestMode;

        var hwnd = new WindowInteropHelper(this).Handle;
        int style = GetWindowLong(hwnd, GWL_EXSTYLE);

        if (_isMouseTestMode)
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, style & ~WS_EX_TRANSPARENT);

            GazeCanvas.IsHitTestVisible = true;
            ClickWheel.IsHitTestVisible = true;

            if (!ClickWheel.IsOpen)
            {
                ClickWheel.OpenAt(new Point(GazeCanvas.ActualWidth / 2, GazeCanvas.ActualHeight / 2));
            }

            Debug.WriteLine("[ActionWheel] Mouse test mode ENABLED (Move/Click to test)");
        }
        else
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT);

            GazeCanvas.IsHitTestVisible = false;
            ClickWheel.IsHitTestVisible = false;

            Debug.WriteLine("[ActionWheel] Mouse test mode DISABLED (Eye tracking mode)");
        }

        UpdateStatusBar();
    }

    private void OnOverlayMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isMouseTestMode || !ClickWheel.IsOpen) return;
        Point mousePoint = e.GetPosition(GazeCanvas);
        ClickWheel.UpdateGaze(mousePoint, _clock.ElapsedMilliseconds);
    }

    private void OnOverlayMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isMouseTestMode || !ClickWheel.IsOpen) return;
        ClickWheel.TriggerHoveredSlice();
    }

    private void LaunchOnScreenKeyboard()
    {
        try
        {
            string oskPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "osk.exe");

            Process.Start(new ProcessStartInfo
            {
                FileName = oskPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ActionWheel] Failed to launch OSK: {ex.Message}");
        }
    }

    private void OnWheelActionSelected(ClickAction action, Point targetCanvasPoint)
    {
        _wheelClosedMs = _clock.ElapsedMilliseconds;

        Point screenPoint = GazeCanvas.PointToScreen(targetCanvasPoint);
        double normalizedX = screenPoint.X / SystemParameters.PrimaryScreenWidth;
        double normalizedY = screenPoint.Y / SystemParameters.PrimaryScreenHeight;
        _cursorController.MoveToNormalized(normalizedX, normalizedY);

        Debug.WriteLine($"[ActionWheel] Triggered {action} at ({screenPoint.X:0}, {screenPoint.Y:0})");
        switch (action)
        {
            case ClickAction.LeftClick:
                // _cursorController.LeftClick();
                break;
            case ClickAction.RightClick:
                // _cursorController.RightClick();
                break;
            case ClickAction.DoubleClick:
                // _cursorController.DoubleClick();
                break;
            case ClickAction.Keyboard:
                LaunchOnScreenKeyboard();
                break;
            case ClickAction.Drag:
                // _cursorController.ToggleLeftDown();
                break;
            case ClickAction.Scroll:
                // Enter scroll mode
                break;
        }
    }

    private void OnRecentered() => _guide.Recentered(_app.RecenteredAt);

    private void OnCalibrationsChanged() => _guide.Calibrated = HeadPosition.Of(_app.Calibrations.Active);

    private void OnCursorMovementChanged()
    {
        if (!_app.IsCursorMovementEnabled && ClickWheel.IsOpen)
            ClickWheel.Close();
        UpdateStatusBar();
    }

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
        if (old.Cursor != now.Cursor)
        {
            _smoothing.Sensitivity = now.Cursor.Sensitivity;
            if (old.Cursor.Smoothing != now.Cursor.Smoothing) _smoothing.Reset();
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
    private void OnLog(string line) => Debug.WriteLine($"[tracker] {line}");

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
            _smoothing.Reset();
            return;
        }

        // the fine-tune and the re-center's shift, then the smoothing, then the head nudge (in the two nudge head modes,
        // smoothed first so it sees the eyes come to rest sooner), then kept within the screen. the bubble turns yellow
        // while it holds on a spot and the head nudges it
        (x, y) = _app.Bubble(x, y);
        if (_settings.Current.Cursor.Smoothing) (x, y) = _smoothing.Filter(x, y, _clock.Elapsed.TotalSeconds);
        bool nudging = false;
        if (!ClickWheel.IsOpen && _settings.Current.Head.Mode.Nudges()
            && msg.Faces?.FirstOrDefault()?.Pose is { } pose)
        {
            (x, y) = _nudge.Update(_clock.ElapsedMilliseconds, x, y, pose.Yaw, pose.Pitch);
            nudging = _nudge.Holding;
        }
        else if (ClickWheel.IsOpen)
        {
            _nudge.Reset();
        }

        long now = _clock.ElapsedMilliseconds;
        GazeBubble.Stroke = nudging ? Holding : Following;
        QuietGuide(nudging || ClickWheel.IsOpen);

        x = Math.Clamp(x, 0.0, 1.0);
        y = Math.Clamp(y, 0.0, 1.0);

        // 3. Update gaze bubble visual position
        Point gazeCanvasPoint = new Point(x * GazeCanvas.ActualWidth, y * GazeCanvas.ActualHeight);
        _lastGazeCanvasPoint = gazeCanvasPoint;
        Canvas.SetLeft(GazeBubble, gazeCanvasPoint.X - GazeBubble.Width / 2);
        Canvas.SetTop(GazeBubble, gazeCanvasPoint.Y - GazeBubble.Height / 2);
        GazeBubble.Visibility = Visibility.Visible;

        // 4. If ActionWheel is open and NOT in mouse test mode, feed gaze directly
        if (ClickWheel.IsOpen)
        {
            if (!_isMouseTestMode)
            {
                ClickWheel.UpdateGaze(gazeCanvasPoint, now);
            }
            if (!ClickWheel.IsOpen)
                _wheelClosedMs = now;
            return;
        }

        // 5. Move real OS cursor only when wheel is closed and not testing
        if (_app.IsCursorMovementEnabled && _monitor.CanMoveCursor && !_isMouseTestMode)
        {
            Point screenPoint = GazeCanvas.PointToScreen(gazeCanvasPoint);
            double normalizedX = screenPoint.X / SystemParameters.PrimaryScreenWidth;
            double normalizedY = screenPoint.Y / SystemParameters.PrimaryScreenHeight;

            _cursorController.MoveToNormalized(normalizedX, normalizedY);

            // 6. Check dwell threshold to open ActionWheel
            if (now - _wheelClosedMs >= WheelCooldownMs)
            {
                double dx = gazeCanvasPoint.X - _dwellAnchor.X;
                double dy = gazeCanvasPoint.Y - _dwellAnchor.Y;
                if (Math.Sqrt(dx * dx + dy * dy) > DwellRadiusPx)
                {
                    _dwellAnchor = gazeCanvasPoint;
                    _dwellStartMs = now;
                }
                else if (now - _dwellStartMs >= DwellToOpenMs)
                {
                    ClickWheel.OpenAt(_dwellAnchor);
                    _dwellStartMs = now;
                }
            }
        }
        else
        {
            _dwellStartMs = now;
        }
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

        if (_isMouseTestMode)
        {
            headline = "MOUSE TEST MODE ON";
            hint = "Move mouse & click wheel slices. Press Ctrl+Alt+T to exit.";
        }
        else if (_app.IsCursorMovementEnabled)
        {
            headline = "Cursor movement ON";
            hint = hint == "" ? "Press Ctrl+Alt+M to stop" : $"{hint}. Press Ctrl+Alt+M to stop cursor movement.";
        }

        StatusHeadline.Text = headline;
        StatusHint.Text = hint;
        StatusHint.Visibility = hint == "" ? Visibility.Collapsed : Visibility.Visible;
        StatusBar.Background = _monitor.State switch
        {
            _ when _isMouseTestMode => Orange,
            TrackingState.Tracking or TrackingState.Uncertain => _app.IsCursorMovementEnabled ? Orange : Green,
            TrackingState.Starting or TrackingState.Paused => Grey,
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

    private static string FormatLatency(long wallMs)
    {
        if (wallMs == 0) return "n/a (no wall field)";
        long latencyMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - wallMs;
        return $"{latencyMs} ms";
    }

    private static string FormatFrame(TrackerMessage msg, double fps, string latency)
    {
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