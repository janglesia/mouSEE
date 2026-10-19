using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace app.Calibration.GazeGrid;

// The grid screen: the mouSEE logo goes to 20 places (5 columns by 4 rows), the frames while each ring fills are
// recorded (App.StartGazeCalibration does that through the events below), then the head step, then the result.
// The gaze is fitted to it when you accept (GazeFit). One class in three files: this one (setup, the tracker, which
// panel shows), GridWindow.Sequence.cs (the 20 points and the head step) and GridWindow.Results.cs (the result
// screen and the visual test).
public partial class GridWindow : Window
{
    // ---- sequence tuning ----
    private const double EdgeMargin = 0.07;  // outer targets sit this far (fraction of screen) from the edges
    private const int GlideMs = 500;         // target moves to its next position
    private const int SettleMs = 700;        // eyes land on the target; logo shrinks to pull focus. NOT sampled
    // ring fill time (when a backend should collect samples) is the Calibration.TargetDwellMs setting
    private const double StartScale = 1.5;   // logo starts this much larger and shrinks to 1.0
    private const int HeadMoveSettleMs = 800; // after saying how to hold the head, time to get into position
    private const int PoseHoldMs = 800;         // in range this long before the pose's dots start
    private const int PoseHintAfterMs = 3000;   // not in range yet: say what to do
    private const int PoseTimeoutMs = 12000;    // go on anyway after this long

    // ---- results tuning (starting guesses, adjust once there are real numbers) ----
    private const double MinSolidTargetRatio = 0.6;   // a target counts if both eyes were tracked in this share of its capture frames
    private const double StableRatio = 0.90;          // share of capture frames with both eyes tracked: Stable at or above this
    private const double FairRatio = 0.70;            // Some dropouts at or above this, Unstable below
    private const double GoodErrorFraction = 0.05;    // mean validation error as a fraction of screen width: Good at or below this
    private const double FairErrorFraction = 0.09;    // Fair at or below this, Poor above

    // Normalized screen positions (0-1), snake order so the target never jumps back across the screen.
    // 5 columns by 4 rows: with only 3 by 3 the fit couldn't see the curves (the sides of the screen reading higher than
    // the middle, see GazeFit.FitY), more points in between show them. Further into the corners didn't help, the
    // targets were already 8% from the edges and the errors were at the targets themselves
    private const int Columns = 5, Rows = 4;
    private static readonly (double X, double Y)[] Targets = BuildTargets();

    // Accuracy-check targets, inside the calibration grid. The mapping must NOT be fitted on these.
    private static readonly (double X, double Y)[] ValidationTargets = [(0.25, 0.25), (0.75, 0.25), (0.75, 0.75), (0.25, 0.75)];

    private static (double X, double Y)[] BuildTargets()
    {
        double[] Axis(int count) => Enumerable.Range(0, count)
            .Select(i => Math.Round(EdgeMargin + i * (1 - 2 * EdgeMargin) / (count - 1), 4)).ToArray();
        double[] xs = Axis(Columns), ys = Axis(Rows);
        var list = new List<(double X, double Y)>();
        for (int r = 0; r < Rows; r++)
        {
            for (int k = 0; k < Columns; k++)
            {
                int c = r % 2 == 0 ? k : Columns - 1 - k; // left-to-right, then right-to-left, and so on
                list.Add((xs[c], ys[r]));
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
    private readonly Speaker _speaker = new(); // for the lean step's instruction
    private int _dwellMs; // Calibration.TargetDwellMs, read when a run starts

    // the head step is guided: how far to lean and tilt is measured against how the head sat during the grid points
    private readonly List<(double FaceX, double Pitch)> _restHead = [];
    private bool _recordingRest;
    private (double FaceX, double Pitch)? _restReference;
    private string? _guidingPose;
    private bool _poseInRange;
    private static readonly Brush PoseGood = new SolidColorBrush(Color.FromRgb(0x1F, 0x5F, 0x4A));
    private static readonly Brush PoseOff = new SolidColorBrush(Color.FromRgb(0x7A, 0x3B, 0x0E));
    // decides how long a tracking problem has lasted (shared logic in app/Tracker/TrackingMonitor.cs); we only show its verdict.
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

    // what looking right / left answers on the current screen, see AskByLooking
    private Action<bool>? _onChoice;

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

    public GridWindow()
    {
        InitializeComponent();
        Choice.Chosen += yes => _onChoice?.Invoke(yes);

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
            _speaker.Dispose();
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

    private void AcceptButton_Click(object sender, RoutedEventArgs e) => Accept();

    private void Accept()
    {
        _sessionEnded = true; // so closing doesn't count as a cancel
        CalibrationAccepted?.Invoke();
        Close();
    }

    private void TryAgainButton_Click(object sender, RoutedEventArgs e) => TryAgain();

    private void TryAgain()
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

        // where the head is: during the grid points that's the reference, during the head step it's guided against it
        if (msg.Faces is { Count: > 0 } faces && faces[0].Box is { Length: 4 } box)
        {
            var head = (FaceX: box[0] + box[2] / 2, Pitch: faces[0].Pose.Pitch);
            if (_recordingRest && _capturing) _restHead.Add(head);
            if (_guidingPose is { } pose && _restReference is { } rest)
            {
                var (inRange, hint) = HeadPose.Guide(pose, head.FaceX - rest.FaceX, head.Pitch - rest.Pitch);
                _poseInRange = inRange;
                PoseHintText.Text = hint;
                PoseHintText.Foreground = inRange ? PoseGood : PoseOff;
            }
        }

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
        AskByLooking(phase);
    }

    // the intro and the results can be answered by looking, right for yes and left for no (ChoicePrompt)
    private void AskByLooking(Phase phase)
    {
        switch (phase)
        {
            case Phase.Intro:
                _onChoice = yes => { if (yes) StartSequence(); else Close(); };
                Choice.Ask("Begin the calibration", "Cancel");
                break;
            case Phase.Result:
                _onChoice = yes => { if (yes) Accept(); else TryAgain(); };
                Choice.Ask("Accept this calibration", "Try again");
                break;
            default:
                // while the targets move the eyes look everywhere, that mustn't answer anything
                _onChoice = null;
                Choice.Stop();
                break;
        }
    }
}