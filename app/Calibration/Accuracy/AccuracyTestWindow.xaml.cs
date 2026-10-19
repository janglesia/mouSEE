using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace app.Calibration.Accuracy;

// Checks how close the bubble lands to where you look, with the calibration in use: dots all over the screen (more
// than the calibration had), sitting normally and leaning both ways. The bubble isn't shown while it runs, so it
// can't be followed. At the end every dot is drawn with a line to where the bubble landed, and everything is saved
// in app/Calibration/Saved/tests/. The points and the math are in app/Calibration/Accuracy/AccuracyTest.cs
public partial class AccuracyTestWindow : Window
{
    private static readonly Dictionary<string, Brush> PhaseColors = new()
    {
        [AccuracyTest.Normal] = Frozen(Color.FromRgb(0xF7, 0xD0, 0x60)),
        [AccuracyTest.LeanLeft] = Frozen(Color.FromRgb(0x5D, 0xAD, 0xE2)),
        [AccuracyTest.LeanRight] = Frozen(Color.FromRgb(0xE0, 0x6C, 0xA8)),
        [AccuracyTest.HeadUp] = Frozen(Color.FromRgb(0x6F, 0xCF, 0x97)),
        [AccuracyTest.HeadDown] = Frozen(Color.FromRgb(0xE0, 0xA4, 0x58)),
    };
    private static readonly Brush PoseGood = Frozen(Color.FromRgb(0x6F, 0xCF, 0x97));
    private static readonly Brush PoseOff = Frozen(Color.FromRgb(0xE0, 0xA4, 0x58));

    // leaning and tilting are guided like in the calibration, against how the head sat in the "sit normally" part
    private const int PoseHoldMs = 800, PoseHintAfterMs = 3000, PoseTimeoutMs = 12000;
    private (double FaceX, double Pitch)? _rest;
    private string? _guidingPose;
    private bool _poseInRange;

    private readonly App _app = (App)Application.Current;
    private readonly Speaker _speaker = new();
    private readonly Stopwatch _clock = new();
    private CancellationTokenSource? _cts;
    private List<FrameSample>? _recording;
    private bool _running;
    private readonly List<UIElement> _drawn = [];

    public AccuracyTestWindow()
    {
        InitializeComponent();
        Middle.Follow(Choice);
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        var active = _app.Calibrations.Active;
        CalibrationText.Text = (active is null
            ? "No calibration, the default values are tested."
            : $"Testing \"{active.Name}\".") + $" Head mode: {_app.Settings.Current.Head.Mode.Label()}.";

        _app.Tracker.MessageReceived += OnTrackerMessage;
        Choice.Chosen += yes =>
        {
            if (yes) Start();
            else Close();
        };
        // the results: the answer only counts after closing the eyes, looking at the dots near the edges isn't one
        Choice.Ready += () => _ = _speaker.SayAsync(
            "To run it again, look to the right for three seconds. To close, look to the left.", CancellationToken.None);
        Loaded += async (_, _) =>
        {
            Activate();
            Focus();
            Choice.Ask("Begin the test", "Close");
            _ = _speaker.SayAsync("Accuracy test. To begin, look to the right for three seconds. To close, look to the left.",
                                  CancellationToken.None);
            await _app.StartTrackerAsync();
        };
        Closing += (_, _) => _cts?.Cancel();
        Closed += (_, _) =>
        {
            _app.Tracker.MessageReceived -= OnTrackerMessage;
            _speaker.Dispose();
        };
    }

    private void OnTrackerMessage(TrackerMessage msg)
    {
        if (msg.Type != "frame") return;
        if (_guidingPose is { } pose && _rest is { } rest && msg.Faces is { Count: > 0 } faces && faces[0].Box is { Length: 4 } box)
        {
            var (inRange, hint) = HeadPose.Guide(pose, box[0] + box[2] / 2 - rest.FaceX, faces[0].Pose.Pitch - rest.Pitch);
            _poseInRange = inRange;
            PoseHintText.Text = hint;
            PoseHintText.Foreground = inRange ? PoseGood : PoseOff;
        }
        if (_recording is null) return;
        // with the fine-tune and the re-center's shift, so it tests where the bubble actually is
        var f = FrameSample.From(msg, _clock.ElapsedMilliseconds);
        if (f.EstimateX is { } x && f.EstimateY is { } y)
        {
            var (bx, by) = _app.Bubble(x, y);
            f = f with { EstimateX = bx, EstimateY = by };
        }
        _recording.Add(f);
    }

    private async void Start()
    {
        if (_running) return;
        _running = true;
        _cts = new CancellationTokenSource();
        try
        {
            await RunAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            // closing
        }
        finally
        {
            _running = false;
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        Choice.Stop();
        IntroPanel.Visibility = ResultPanel.Visibility = Visibility.Collapsed;
        foreach (var e in _drawn) Board.Children.Remove(e);
        _drawn.Clear();
        Cursor = Cursors.None;

        var points = AccuracyTest.Points;
        var results = new List<TestPointResult>();
        var frames = new List<TargetRecording>();
        string? phase = null;
        for (int i = 0; i < points.Count; i++)
        {
            var point = points[i];
            if (point.Phase != phase)
            {
                // the reference for leaning and tilting: how the head sat in the "sit normally" part
                if (phase == AccuracyTest.Normal)
                {
                    var normal = frames.Where(r => !r.MoveHead).SelectMany(r => r.Frames).Where(f => f.FaceFound && f.FaceW > 0).ToList();
                    if (normal.Count > 0)
                        _rest = (Stats.Percentile(normal.Select(f => f.FaceX).ToList(), 50),
                                 Stats.Percentile(normal.Select(f => f.Pitch).ToList(), 50));
                }
                phase = point.Phase;
                Dot.Visibility = Visibility.Collapsed;
                PhaseText.Text = AccuracyTest.Say(phase);
                PhaseText.Visibility = Visibility.Visible;
                // guide toward the new position right away (or stop guiding), not the last one while the instruction is read out
                StartGuiding(AccuracyTest.Pose(phase));
                await _speaker.SayAsync(PhaseText.Text, ct);
                if (AccuracyTest.Pose(phase) is { } pose) await WaitForPoseAsync(pose, ct);
                else
                {
                    _guidingPose = null;
                    PoseHintText.Visibility = Visibility.Collapsed;
                    await Task.Delay(500, ct);
                }
                PhaseText.Visibility = Visibility.Collapsed;
            }

            ProgressText.Text = $"{point.Phase}, dot {i + 1} of {points.Count}";
            Canvas.SetLeft(Dot, point.X * Root.ActualWidth - Dot.Width / 2);
            Canvas.SetTop(Dot, point.Y * Root.ActualHeight - Dot.Height / 2);
            Dot.Visibility = Visibility.Visible;
            await Task.Delay(AccuracyTest.SettleMs, ct);

            var recorded = new List<FrameSample>();
            _clock.Restart();
            _recording = recorded;
            try
            {
                await Task.Delay(AccuracyTest.CaptureMs, ct);
            }
            finally
            {
                _recording = null;
            }
            results.Add(AccuracyTest.Result(point, recorded));
            frames.Add(new TargetRecording(i, point.X, point.Y, false, recorded, point.Phase != AccuracyTest.Normal, AccuracyTest.Pose(point.Phase)));
        }

        Dot.Visibility = Visibility.Collapsed;
        PoseHintText.Visibility = Visibility.Collapsed;
        _guidingPose = null;
        ProgressText.Text = "";
        Cursor = Cursors.Arrow;
        Speaker.Ding();

        var report = AccuracyTest.Report(results, _app.Calibrations.Active, SystemParameters.PrimaryScreenWidth,
                                         SystemParameters.PrimaryScreenHeight) with { OffsetX = _app.Offset.X, OffsetY = _app.Offset.Y, HeadMode = _app.Settings.Current.Head.Mode };
        try
        {
            SavedText.Text = $"Saved to {_app.Calibrations.SaveTest(new AccuracyTestFile(report, frames))}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SavedText.Text = $"Couldn't save the results: {ex.Message}";
        }
        ShowResults(report);
    }

    // live "a little more / a little less / good" until the head has been in range for a moment, then the dots start
    // (and the hint stays up during them). goes on anyway without a reference or after a while
    // switches the live hint to this pose (null: no guiding, sitting normally)
    private void StartGuiding(string? pose)
    {
        _guidingPose = pose;
        _poseInRange = false;
        PoseHintText.Text = "";
        PoseHintText.Visibility = pose is null || _rest is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task WaitForPoseAsync(string pose, CancellationToken ct)
    {
        if (_guidingPose != pose) StartGuiding(pose);
        if (_rest is null)
        {
            await Task.Delay(1500, ct);
            return;
        }
        var clock = Stopwatch.StartNew();
        long inSince = -1;
        bool hinted = false;
        while (clock.ElapsedMilliseconds < PoseTimeoutMs)
        {
            long now = clock.ElapsedMilliseconds;
            if (_poseInRange)
            {
                if (inSince < 0) inSince = now;
                if (now - inSince >= PoseHoldMs)
                {
                    _speaker.SayNow("Good, hold it there.");
                    return;
                }
            }
            else
            {
                inSince = -1;
                if (!hinted && now >= PoseHintAfterMs)
                {
                    _speaker.SayNow(PoseHintText.Text);
                    hinted = true;
                }
            }
            await Task.Delay(50, ct);
        }
    }

    private void ShowResults(AccuracyTestReport report)
    {
        foreach (var p in report.Points)
        {
            var color = PhaseColors.GetValueOrDefault(p.Phase, Brushes.White);
            double tx = p.X * Root.ActualWidth, ty = p.Y * Root.ActualHeight;
            Add(new Ellipse { Width = 22, Height = 22, Stroke = color, StrokeThickness = 2 }, tx - 11, ty - 11);
            if (p.EstimateX is null || p.EstimateY is null) continue;
            double bx = Math.Clamp(p.EstimateX.Median, 0, 1) * Root.ActualWidth;
            double by = Math.Clamp(p.EstimateY.Median, 0, 1) * Root.ActualHeight;
            Add(new Line { X1 = tx, Y1 = ty, X2 = bx, Y2 = by, Stroke = color, StrokeThickness = 1.5, Opacity = 0.8 }, 0, 0);
            Add(new Ellipse { Width = 10, Height = 10, Fill = color }, bx - 5, by - 5);
        }

        SummaryList.ItemsSource = report.Summary;
        ResultPanel.Visibility = Visibility.Visible;
        Choice.AskAfterLooking("Run it again", "Close");
        _ = _speaker.SayAsync("Done. " + (report.Summary.FirstOrDefault() ?? "") +
                              " When you're done looking, close your eyes for two seconds, then choose.", CancellationToken.None);
    }

    private void Add(UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        Board.Children.Add(element);
        _drawn.Add(element);
    }

    private void BeginButton_Click(object sender, RoutedEventArgs e) => Start();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
