using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using app.Calibration;
using app.Gaze;
using app.Settings;

namespace app.UI;

public partial class CalibrationWindow : Window
{
    // ---- sequence tuning ----
    private const double EdgeMargin = 0.08;  // outer targets sit this far (fraction of screen) from the edges
    private const int GlideMs = 500;         // target moves to its next position
    private const int SettleMs = 700;        // eyes land on the target; logo shrinks to pull focus. NOT sampled
    // ring fill time (when a backend should collect samples) is the Calibration.TargetDwellMs setting
    private const double StartScale = 1.5;   // logo starts this much larger and shrinks to 1.0

    // ---- results tuning (starting guesses, adjust once there are real numbers) ----
    private const double MinSolidTargetRatio = 0.6;   // a target counts if both eyes were tracked in this share of its capture frames
    private const double StableRatio = 0.90;          // share of capture frames with both eyes tracked: Stable at or above this
    private const double FairRatio = 0.70;            // Some dropouts at or above this, Unstable below
    private const double GoodErrorFraction = 0.05;    // mean validation error as a fraction of screen width: Good at or below this
    private const double FairErrorFraction = 0.09;    // Fair at or below this, Poor above

    // Normalized screen positions (0-1), snake order so the target never jumps back across the screen.
    private static readonly (double X, double Y)[] Targets = BuildTargets();

    // Accuracy-check targets, inside the calibration grid. The mapping must NOT be fitted on these.
    private static readonly (double X, double Y)[] ValidationTargets = [(0.25, 0.25), (0.75, 0.25), (0.75, 0.75), (0.25, 0.75)];

    private static (double X, double Y)[] BuildTargets()
    {
        double[] axis = { EdgeMargin, 0.5, 1 - EdgeMargin };
        var list = new List<(double X, double Y)>();
        for (int r = 0; r < 3; r++)
        {
            for (int k = 0; k < 3; k++)
            {
                int c = r % 2 == 0 ? k : 2 - k; // left-to-right, then right-to-left, then left-to-right
                list.Add((axis[c], axis[r]));
            }
        }
        return list.ToArray();
    }

    private enum Phase { Intro, Running, Result, Test }
    private enum Level { Unknown, Good, Fair, Poor }

    private static readonly Brush Amber = new SolidColorBrush(Color.FromRgb(0xE0, 0xA4, 0x58));
    private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x6F, 0xCF, 0x97));
    private static readonly Brush Grey = new SolidColorBrush(Color.FromRgb(0x6C, 0x81, 0x79));
    private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x6C));
    private static readonly Brush RingStroke = new SolidColorBrush(Color.FromRgb(0x5C, 0x4A, 0x10));

    private readonly TrackerClient _tracker = ((App)Application.Current).Tracker;
    private Phase _phase = Phase.Intro;
    private CancellationTokenSource? _cts;
    private bool _sessionStarted;
    private bool _sessionEnded;
    private readonly SettingsService _settings = ((App)Application.Current).Settings;
    private int _dwellMs; // Calibration.TargetDwellMs, read when a run starts
    // decides how long a tracking problem has lasted (shared logic in app/Gaze); we only show its verdict.
    // never paused, pausing is for the cursor and calibration needs tracking
    private readonly TrackingMonitor _monitor = new(((App)Application.Current).Settings.Current.Advanced);
    private readonly DispatcherTimer _timeoutTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    // measured while the ring is filling
    private bool _capturing;
    private int _dwellTotal, _dwellReady;       // frames seen / frames with both eyes tracked, for the current target
    private int _allTotal, _allReady;           // same, summed over every capture of this run
    private readonly List<double> _calibrationRatios = new(); // per calibration target: share of good frames
    private Stopwatch _sessionClock = new();

    // from the backend
    private CalibrationValidation? _validation;
    private bool _validationPending;
    private int? _backendCompletedTargets;
    private int? _backendTotalTargets;
    private string? _backendStatus;

    private readonly List<Ellipse> _testRings = new();

    // ---- events for whatever does the actual capture / fitting ----
    public event Action? CalibrationStarted;
    /// <summary>Target is on screen at its final position. The eye is still moving; don't sample yet.</summary>
    public event Action<CalibrationTarget>? TargetPresented;
    /// <summary>Settle time is over and the ring starts filling. Sample for Calibration.TargetDwellMs from here.</summary>
    public event Action<CalibrationTarget>? TargetCaptureStarted;
    /// <summary>The ring is full. Stop sampling this target.</summary>
    public event Action<CalibrationTarget>? TargetAccepted;
    /// <summary>All calibration targets are done (validation targets are not included). Fit the mapping now.</summary>
    public event Action? CalibrationCompleted;
    /// <summary>All validation targets are done (only raised when RunValidation is true). Call ShowValidation with the measured error.</summary>
    public event Action? ValidationCompleted;
    /// <summary>The user pressed Accept calibration. Esc or closing the window instead means not accepted.</summary>
    public event Action? CalibrationAccepted;
    public event Action? CalibrationCancelled;

    /// <summary>
    /// Set to true before the user presses Begin to show 4 extra accuracy-check targets after the calibration targets.
    /// Leave it false until a backend exists that can measure the error, otherwise the user waits for nothing.
    /// Starts out as the "Run accuracy check" setting.
    /// </summary>
    public bool RunValidation { get; set; } = ((App)Application.Current).Settings.Current.Calibration.RunAccuracyCheck;

    /// <summary>
    /// Maps a tracker frame to a gaze position on screen (0-1 on both axes), or null if there isn't one.
    /// When set, the results screen offers "Test tracking": a dot follows the predicted gaze so the user can check it.
    /// </summary>
    public Func<TrackerMessage, (double X, double Y)?>? GazePredictor { get; set; }

    public CalibrationWindow()
    {
        InitializeComponent();

        // Cover the whole primary screen, taskbar included, so normalized target positions are screen positions.
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        _tracker.MessageReceived += OnTrackerMessage;
        _tracker.Failed += OnTrackerFailed;
        _monitor.Changed += OnMonitorChanged;
        _settings.Changed += OnSettingsChanged;
        _timeoutTimer.Tick += (_, _) => _monitor.CheckTimeout(); // frames stopping can't be noticed any other way
        _timeoutTimer.Start();

        Loaded += async (_, _) =>
        {
            Activate();
            Focus();
            await ((App)Application.Current).StartTrackerAsync();
        };

        Closing += (_, _) =>
        {
            _cts?.Cancel();
            if (_sessionStarted && !_sessionEnded)
            {
                _sessionEnded = true;
                CalibrationCancelled?.Invoke();
            }
        };

        Closed += (_, _) =>
        {
            _tracker.MessageReceived -= OnTrackerMessage;
            _tracker.Failed -= OnTrackerFailed;
            _monitor.Changed -= OnMonitorChanged;
            _settings.Changed -= OnSettingsChanged;
            _timeoutTimer.Stop();
        };

        ShowPhase(Phase.Intro);
    }

    // =====================================================================
    // Public API for the backend
    // =====================================================================

    /// <summary>Accuracy measured on the validation targets. Can be called any time, including while the results are on screen.</summary>
    public void ShowValidation(CalibrationValidation validation)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => ShowValidation(validation));
            return;
        }

        _validation = validation;
        _validationPending = false;
        if (_phase == Phase.Result) RefreshResult();
    }

    public void ReportBackendProgress(int completedTargets, int totalTargets)
    {
        if (totalTargets <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalTargets));

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => ReportBackendProgress(completedTargets, totalTargets));
            return;
        }

        _backendTotalTargets = totalTargets;
        _backendCompletedTargets = Math.Clamp(completedTargets, 0, totalTargets);
        RefreshBackendText();
    }

    public void ReportBackendStatus(string status)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => ReportBackendStatus(status));
            return;
        }

        _backendStatus = status;
        RefreshBackendText();
    }

    private void RefreshBackendText()
    {
        BackendStatusText.Text = _backendStatus
            ?? (_backendCompletedTargets is int done && _backendTotalTargets is int total ? $"Captured {done} / {total}" : "");
    }

    // =====================================================================
    // Input
    // =====================================================================

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (_phase == Phase.Test) ShowPhase(Phase.Result);
            else Close();
        }
        else if (_phase == Phase.Intro && e.Key is (Key.Space or Key.Enter))
        {
            e.Handled = true;
            StartSequence();
        }
    }

    private void BeginButton_Click(object sender, RoutedEventArgs e) => StartSequence();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void AcceptButton_Click(object sender, RoutedEventArgs e)
    {
        _sessionEnded = true; // so closing doesn't count as a cancel
        CalibrationAccepted?.Invoke();
        Close();
    }

    private void TryAgainButton_Click(object sender, RoutedEventArgs e)
    {
        // CalibrationStarted fires again when the user presses Begin; the backend should drop the old samples then
        _sessionStarted = false;
        _sessionEnded = false;
        ShowPhase(Phase.Intro);
    }

    private void TestButton_Click(object sender, RoutedEventArgs e) => EnterTestMode();
    private void BackToResultButton_Click(object sender, RoutedEventArgs e) => ShowPhase(Phase.Result);

    // =====================================================================
    // Tracker frames
    // =====================================================================

    private void OnTrackerMessage(TrackerMessage msg)
    {
        if (msg.Type != "frame") return;

        _monitor.OnFrame(msg.Tracking);

        switch (_phase)
        {
            case Phase.Intro:
                var (dot, text, _) = DescribeTracking(msg);
                SetFaceStatus(dot, text);
                break;
            case Phase.Running:
                if (_capturing)
                {
                    _dwellTotal++;
                    if (DescribeTracking(msg).Ready) _dwellReady++;
                }
                break;
            case Phase.Test:
                UpdateGazeDot(msg);
                break;
        }
    }

    private void OnSettingsChanged(AppSettings old, AppSettings now)
    {
        if (old.Advanced != now.Advanced) _monitor.Configure(now.Advanced);
    }

    private void OnTrackerFailed(string reason)
    {
        _monitor.OnTrackerStopped(reason);
        if (_phase != Phase.Intro) return;
        FaceStatusDot.Fill = Red;
        FaceStatusText.Text = reason.Replace('\n', ' ');
    }

    // The tracker judges each frame (msg.Tracking). TrackingMonitor decides how long a problem has to last
    // before it counts, so a blink or one bad frame never flashes a warning, and it notices when frames stop.
    private void OnMonitorChanged()
    {
        switch (_phase)
        {
            case Phase.Running:
                UpdateWarning();
                break;
            case Phase.Intro when _monitor.State == TrackingState.NotResponding:
            {
                var (headline, hint) = TrackingMonitor.Describe(_monitor.State, _monitor.Reason);
                SetFaceStatus(Red, $"{headline}. {hint}");
                break;
            }
        }
    }

    private void UpdateWarning()
    {
        if (_monitor.State is TrackingState.Tracking or TrackingState.Uncertain)
        {
            TrackingWarningText.Text = "";
            return;
        }

        var (headline, hint) = TrackingMonitor.Describe(_monitor.State, _monitor.Reason);
        TrackingWarningText.Text = hint.Length > 0 ? $"{headline}. {hint}" : headline;
    }

    private static (Brush Dot, string Text, bool Ready) DescribeTracking(TrackerMessage msg)
    {
        switch (msg.Tracking?.State)
        {
            case "ok":
            {
                // a wink still counts as "ok" for the tracker, but calibration wants both eyes
                var gaze = msg.Faces is { Count: > 0 } faces ? faces[0].Eyes?.Gaze : null;
                return gaze?.EyesUsed == 2
                    ? (Green, "Face and both eyes detected. Ready", true)
                    : (Amber, "Only one eye is usable. Face the screen with both eyes open", false);
            }
            case "eyes_closed":
                return (Amber, "Your eyes are closed. Open your eyes", false);
            case "lost":
                return (Amber, msg.Tracking!.Reason switch
                {
                    "no_face" => "No face detected. Move into the camera's view",
                    "face_partial" => "Part of your face is outside the camera view. Move back to the center",
                    "head_turned" => "Face the screen. Your head is turned or tilted too far",
                    "eyes_not_found" => "Face found, but your eyes can't be read. Check the lighting",
                    "too_far" => "You're too far from the camera. Move closer",
                    "eyes_covered" => "An eye is covered. Uncover your eyes",
                    _ => "Tracking lost. Face the screen",
                }, false);
            default:
                return (Grey, "Waiting for tracker...", false);
        }
    }

    private void SetFaceStatus(Brush dot, string text)
    {
        if (FaceStatusText.Text == text) return; // frames arrive ~60/s, only touch the UI on change
        FaceStatusDot.Fill = dot;
        FaceStatusText.Text = text;
    }

    // =====================================================================
    // The sequence
    // =====================================================================

    private async void StartSequence()
    {
        if (_phase != Phase.Intro) return; // Space on a focused button can fire twice
        await RunSequenceAsync();
    }

    private async Task RunSequenceAsync()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        _sessionStarted = true;
        _sessionEnded = false;
        _dwellMs = _settings.Current.Calibration.TargetDwellMs; // same for every target in a run
        _backendCompletedTargets = null;
        _backendTotalTargets = null;
        _backendStatus = null;
        _validation = null;
        _validationPending = false;
        _calibrationRatios.Clear();
        _allTotal = 0;
        _allReady = 0;
        _sessionClock = Stopwatch.StartNew();
        RefreshBackendText();

        ShowPhase(Phase.Running);
        CalibrationStarted?.Invoke();

        try
        {
            for (int i = 0; i < Targets.Length; i++)
            {
                var (x, y) = Targets[i];
                ProgressText.Text = $"Point {i + 1} of {Targets.Length}";
                _calibrationRatios.Add(await RunTargetAsync(new CalibrationTarget(i, x, y), snap: i == 0, ct));
            }
            CalibrationCompleted?.Invoke(); // the backend can fit the mapping now

            if (RunValidation)
            {
                _validationPending = true;
                for (int j = 0; j < ValidationTargets.Length; j++)
                {
                    var (x, y) = ValidationTargets[j];
                    ProgressText.Text = $"Accuracy check {j + 1} of {ValidationTargets.Length}";
                    await RunTargetAsync(new CalibrationTarget(Targets.Length + j, x, y, IsValidation: true), snap: false, ct);
                }
                ValidationCompleted?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            return; // window is closing; don't touch the UI
        }

        _sessionEnded = true;
        _sessionClock.Stop();
        ShowPhase(Phase.Result);
        RefreshResult();
    }

    // Moves to the target, lets the eye settle, then fills the ring. Returns the share of capture frames with both eyes tracked.
    private async Task<double> RunTargetAsync(CalibrationTarget target, bool snap, CancellationToken ct)
    {
        SetRingProgress(0);

        await MoveTargetAsync(target.NormalizedX, target.NormalizedY, snap, ct);
        TargetPresented?.Invoke(target);

        await SettleAsync(ct);
        TargetCaptureStarted?.Invoke(target);

        double ratio = await DwellAsync(ct);
        TargetAccepted?.Invoke(target);
        return ratio;
    }

    private async Task MoveTargetAsync(double x, double y, bool snap, CancellationToken ct)
    {
        double left = x * Root.ActualWidth - TargetVisual.Width / 2;
        double top = y * Root.ActualHeight - TargetVisual.Height / 2;

        if (snap)
        {
            // a finished glide from a previous run keeps overriding the local value until cleared
            TargetVisual.BeginAnimation(Canvas.LeftProperty, null);
            TargetVisual.BeginAnimation(Canvas.TopProperty, null);
            Canvas.SetLeft(TargetVisual, left);
            Canvas.SetTop(TargetVisual, top);
            return;
        }

        double fromLeft = Canvas.GetLeft(TargetVisual);
        double fromTop = Canvas.GetTop(TargetVisual);

        // set the final value first, then animate toward it
        Canvas.SetLeft(TargetVisual, left);
        Canvas.SetTop(TargetVisual, top);

        var duration = TimeSpan.FromMilliseconds(GlideMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        TargetVisual.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(fromLeft, left, duration) { EasingFunction = ease });
        TargetVisual.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(fromTop, top, duration) { EasingFunction = ease });

        await Task.Delay(GlideMs, ct);
    }

    private async Task SettleAsync(CancellationToken ct)
    {
        // big logo shrinking onto the point draws the eye to the center
        var shrink = new DoubleAnimation(StartScale, 1.0, TimeSpan.FromMilliseconds(SettleMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        TargetScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        TargetScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);

        await Task.Delay(SettleMs, ct);
    }

    private async Task<double> DwellAsync(CancellationToken ct)
    {
        _dwellTotal = 0;
        _dwellReady = 0;
        _capturing = true;
        try
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < _dwellMs)
            {
                SetRingProgress(sw.ElapsedMilliseconds / (double)_dwellMs);
                await Task.Delay(16, ct);
            }
            SetRingProgress(1);
        }
        finally
        {
            _capturing = false;
        }

        _allTotal += _dwellTotal;
        _allReady += _dwellReady;
        return _dwellTotal > 0 ? _dwellReady / (double)_dwellTotal : 0;
    }

    // Ellipse dashes are measured in multiples of the stroke thickness, so the visible arc is
    // progress * (circumference / thickness) long, followed by a gap bigger than the remainder.
    private void SetRingProgress(double progress)
    {
        double t = CaptureRing.StrokeThickness;
        double length = Math.PI * (CaptureRing.Width - t) / t;
        CaptureRing.StrokeDashArray = new DoubleCollection { Math.Clamp(progress, 0, 1) * length, length + 1 };
    }

    // =====================================================================
    // Results
    // =====================================================================

    private static Brush LevelBrush(Level level) => level switch
    {
        Level.Good => Green,
        Level.Fair => Amber,
        Level.Poor => Red,
        _ => Grey,
    };

    private void RefreshResult()
    {
        int total = Targets.Length;

        // ---- targets: how many were captured with both eyes tracked most of the time
        int solid = _calibrationRatios.Count(r => r >= MinSolidTargetRatio);
        TargetsValue.Text = $"{solid} / {total}";
        TargetsValue.Foreground = LevelBrush(solid == total ? Level.Good : solid >= total - 2 ? Level.Fair : Level.Poor);
        TargetsDetail.Text = solid == total
            ? "Every target was captured with both eyes tracked"
            : $"{total - solid} had weak tracking";

        // ---- tracking stability: share of capture frames with both eyes tracked
        double stability = _allTotal > 0 ? _allReady / (double)_allTotal : -1;
        Level stabilityLevel = stability < 0 ? Level.Unknown
                             : stability >= StableRatio ? Level.Good
                             : stability >= FairRatio ? Level.Fair
                             : Level.Poor;
        TrackingValue.Text = stabilityLevel switch
        {
            Level.Good => "Stable",
            Level.Fair => "Some dropouts",
            Level.Poor => "Unstable",
            _ => "No data",
        };
        TrackingValue.Foreground = LevelBrush(stabilityLevel);
        TrackingDetail.Text = stability < 0
            ? "No tracker frames arrived during capture"
            : $"Both eyes detected in {stability:P0} of frames";

        // ---- accuracy: only from a real validation measurement, never guessed
        Level accuracyLevel = Level.Unknown;
        if (_validation is { } v)
        {
            double fraction = v.MeanErrorPx / SystemParameters.PrimaryScreenWidth;
            accuracyLevel = fraction <= GoodErrorFraction ? Level.Good
                          : fraction <= FairErrorFraction ? Level.Fair
                          : Level.Poor;
            AccuracyValue.Text = accuracyLevel switch { Level.Good => "Good", Level.Fair => "Fair", _ => "Poor" };
            AccuracyDetail.Text = $"Average error about {v.MeanErrorPx:0} px ({fraction:P1} of screen width) over {v.TargetCount} test targets";
        }
        else if (_validationPending)
        {
            AccuracyValue.Text = "Measuring...";
            AccuracyDetail.Text = "Checking against the test targets";
        }
        else
        {
            AccuracyValue.Text = "Not measured";
            AccuracyDetail.Text = "No accuracy check has been run yet";
        }
        AccuracyValue.Foreground = LevelBrush(accuracyLevel);

        // ---- overall
        bool captureOk = solid == total && stabilityLevel is Level.Good or Level.Fair;
        bool canTest = GazePredictor is not null;
        TestButton.Visibility = canTest ? Visibility.Visible : Visibility.Collapsed;

        string quality, title, subtitle, next;
        Level qualityLevel;

        if (!captureOk)
        {
            quality = "Retry suggested"; qualityLevel = Level.Poor;
            title = "Calibration needs another try";
            subtitle = "Tracking dropped out during some of the targets.";
            next = "Sit centered, keep your head still, keep both eyes open, and try again.";
        }
        else if (accuracyLevel == Level.Poor)
        {
            quality = "Retry suggested"; qualityLevel = Level.Poor;
            title = "Calibration needs another try";
            subtitle = "The accuracy check shows large errors.";
            next = "Sit centered, keep your head still, and try again.";
        }
        else if (_validationPending)
        {
            quality = "Checking..."; qualityLevel = Level.Unknown;
            title = "Almost done";
            subtitle = "Checking how accurate the calibration is.";
            next = "This only takes a moment.";
        }
        else if (accuracyLevel == Level.Unknown)
        {
            quality = "Unverified"; qualityLevel = Level.Fair;
            title = "Calibration captured";
            subtitle = "Every target was captured, but accuracy hasn't been checked.";
            next = canTest
                ? "Use Test tracking to see whether the dot follows your eyes, then accept or try again."
                : "You can accept it, but there's no measurement yet of how closely it follows your eyes.";
        }
        else
        {
            quality = "Pass"; qualityLevel = accuracyLevel;
            title = "Calibration complete!";
            subtitle = "Your eye tracker is ready to use.";
            next = canTest
                ? "Use Test tracking to check that the dot follows your eyes, then accept."
                : "Accept to start using it, or try again if you moved during calibration.";
        }

        QualityValue.Text = quality;
        QualityValue.Foreground = LevelBrush(qualityLevel);
        QualityDetail.Text = $"Took {_sessionClock.Elapsed.TotalSeconds:0} seconds";
        ResultTitle.Text = title;
        ResultSubtitle.Text = subtitle;
        NextStepText.Text = next;
    }

    // =====================================================================
    // Visual test: a dot follows the predicted gaze so the user can check it before accepting
    // =====================================================================

    private void EnterTestMode()
    {
        foreach (var ring in _testRings) TestLayer.Children.Remove(ring);
        _testRings.Clear();

        (double X, double Y)[] spots = [(0.15, 0.15), (0.85, 0.15), (0.5, 0.5), (0.15, 0.85), (0.85, 0.85)];
        foreach (var (x, y) in spots)
        {
            var ring = new Ellipse
            {
                Width = 64,
                Height = 64,
                Stroke = RingStroke,
                StrokeThickness = 3,
                StrokeDashArray = new DoubleCollection { 2, 2 },
            };
            Canvas.SetLeft(ring, x * Root.ActualWidth - 32);
            Canvas.SetTop(ring, y * Root.ActualHeight - 32);
            TestLayer.Children.Add(ring);
            _testRings.Add(ring);
        }

        ShowPhase(Phase.Test);
    }

    private void UpdateGazeDot(TrackerMessage msg)
    {
        var predicted = GazePredictor?.Invoke(msg);
        if (predicted is not { } p) return;

        Canvas.SetLeft(GazeDot, Math.Clamp(p.X, 0, 1) * Root.ActualWidth - GazeDot.Width / 2);
        Canvas.SetTop(GazeDot, Math.Clamp(p.Y, 0, 1) * Root.ActualHeight - GazeDot.Height / 2);
    }

    // =====================================================================
    // Screen state
    // =====================================================================

    private void ShowPhase(Phase phase)
    {
        _phase = phase;
        IntroPanel.Visibility = phase == Phase.Intro ? Visibility.Visible : Visibility.Collapsed;
        ResultPanel.Visibility = phase == Phase.Result ? Visibility.Visible : Visibility.Collapsed;
        TargetLayer.Visibility = phase == Phase.Running ? Visibility.Visible : Visibility.Collapsed;
        ProgressPanel.Visibility = phase == Phase.Running ? Visibility.Visible : Visibility.Collapsed;
        TestLayer.Visibility = phase == Phase.Test ? Visibility.Visible : Visibility.Collapsed;
        TestBar.Visibility = phase == Phase.Test ? Visibility.Visible : Visibility.Collapsed;
        Cursor = phase == Phase.Running ? Cursors.None : Cursors.Arrow;
        EscHint.Text = phase switch
        {
            Phase.Result => "Esc to close without accepting",
            Phase.Test => "Esc to go back",
            _ => "Esc to cancel",
        };
        if (phase == Phase.Running) UpdateWarning();
        else TrackingWarningText.Text = "";
    }
}