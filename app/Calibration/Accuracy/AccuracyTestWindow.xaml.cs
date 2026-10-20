using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace app.Calibration.Accuracy;

// The accuracy test and the fine-tune in one. Checks how close the bubble lands to where you look, with the calibration
// in use: dots all over the screen (more than the calibration had), sitting normally, leaning both ways and with the head
// tilted. The bubble isn't shown while it runs, so it can't be followed. At the end every dot is drawn with a line to
// where the bubble landed, and everything is saved in app/Calibration/Saved/tests/. The points and the math are in
// app/Calibration/Accuracy/AccuracyTest.cs.
// The same dots are the fine-tune (app/Bubble/FineTune.cs): the gaze numbers are fitted again with them added to the
// calibration's (the sitting normally ones like the grid's, the leaning and tilting ones like the head step's), then a
// correction on top for the sitting normally ones. Both bubbles follow the eyes on the result screen, as it is now and
// with the fine-tune, and it's kept if you want it. Nothing is said out loud once the dots are done.
public partial class AccuracyTestWindow : Window
{
    // the live bubbles move a third of the way to each new frame, so they can be read and not shake
    private const double Smoothing = 0.35;
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

    // the fine-tune after the dots: the correction, what it found, where the head was, the gaze numbers fitted again
    // with the dots (null if that didn't work) and the dots to save with them. null without a calibration to keep it in
    private (GazeCorrection Correction, FineTuneReport Report, HeadPosition? At,
             (GazeModel Model, GazeFitReport Report, List<GazeTargetSummary> Targets)? Refit, List<TargetRecording> Dots)? _fineTune;
    private (double X, double Y)? _before, _after;

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
            // the results: keep the fine-tune if it helps, otherwise run it again. no closes
            if (!yes) Close();
            else if (ResultPanel.Visibility == Visibility.Visible && Helps) Keep();
            else Start();
        };
        Loaded += async (_, _) =>
        {
            Activate();
            Focus();
            Choice.Ask("Begin the test", "Close");
            _ = _speaker.SayAsync("Accuracy test and fine-tune. To begin, look to the right for three seconds. To close, look to the left.",
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
        // the tracker's own position, without the fine-tune or the re-center: the fine-tune is fitted on those
        _recording?.Add(FrameSample.From(msg, _clock.ElapsedMilliseconds));
        if (_fineTune is { } result && ResultPanel.Visibility == Visibility.Visible) ShowBubbles(msg, result.Correction, result.Refit?.Model);
    }

    // with the fine-tune and the re-center's shift as they are now, so the test is about where the bubble actually is
    private FrameSample AsBubble(FrameSample f) =>
        f.EstimateX is { } x && f.EstimateY is { } y && _app.Bubble(x, y) is var (bx, by) ? f with { EstimateX = bx, EstimateY = by } : f;

    // the result screen's two bubbles: grey how it is now, yellow with the fine-tune. with the gaze numbers fitted again,
    // the tracker doesn't have them yet, so where they put the bubble is worked out here
    private void ShowBubbles(TrackerMessage msg, GazeCorrection correction, GazeModel? refit)
    {
        if (msg.Faces?.FirstOrDefault()?.Eyes?.Estimate is not { Valid: true, X: double x, Y: double y })
        {
            BeforeBubble.Visibility = AfterBubble.Visibility = Visibility.Collapsed;
            return;
        }
        _before = Smooth(_before, _app.Bubble(x, y));
        Place(BeforeBubble, _before.Value);
        if (!Helps) return;

        (double X, double Y)? after = refit is not null ? GazeFit.Predict(refit, FrameSample.From(msg, 0), HeadCorrections) : (x, y);
        if (after is not { } a)
        {
            AfterBubble.Visibility = Visibility.Collapsed;
            return;
        }
        _after = Smooth(_after, correction.Apply(a.X, a.Y));
        Place(AfterBubble, _after.Value);
    }

    private static (double X, double Y) Smooth((double X, double Y)? last, (double X, double Y) now) =>
        last is { } l ? (l.X + (now.X - l.X) * Smoothing, l.Y + (now.Y - l.Y) * Smoothing) : now;

    private void Place(Ellipse bubble, (double X, double Y) at)
    {
        Canvas.SetLeft(bubble, Math.Clamp(at.X, 0, 1) * Root.ActualWidth - bubble.Width / 2);
        Canvas.SetTop(bubble, Math.Clamp(at.Y, 0, 1) * Root.ActualHeight - bubble.Height / 2);
        bubble.Visibility = Visibility.Visible;
    }

    // the tracker leaves the head corrections out in the head modes without them, so the bubble worked out here does too
    private bool HeadCorrections => _app.Settings.Current.Head.Mode.Cancels();

    // there's a fine-tune worth trying: the gaze numbers fitted again, or a correction that helps
    private bool Helps => _fineTune is { } f && (f.Refit is not null || f.Report.CorrectsX || f.Report.CorrectsY);

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
        BeforeBubble.Visibility = AfterBubble.Visibility = Visibility.Collapsed;
        foreach (var e in _drawn) Board.Children.Remove(e);
        _drawn.Clear();
        _fineTune = null;
        _before = _after = null;
        Cursor = Cursors.None;

        var points = AccuracyTest.Points;
        var results = new List<TestPointResult>();
        var frames = new List<TargetRecording>();
        // the same dots for the fine-tune, with the tracker's own positions
        var dots = new List<TargetRecording>();
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
            var bubble = recorded.Select(AsBubble).ToList();
            results.Add(AccuracyTest.Result(point, bubble));
            frames.Add(new TargetRecording(i, point.X, point.Y, false, bubble, point.Phase != AccuracyTest.Normal, AccuracyTest.Pose(point.Phase)));
            dots.Add(new TargetRecording(FineTune.FirstIndex + i, point.X, point.Y, false, recorded, Pose: AccuracyTest.Pose(point.Phase), FineTune: true));
        }

        Dot.Visibility = Visibility.Collapsed;
        PoseHintText.Visibility = Visibility.Collapsed;
        _guidingPose = null;
        ProgressText.Text = "";
        Cursor = Cursors.Arrow;
        Speaker.Ding();

        if (_app.Calibrations.Active is not null)
        {
            var (correction, fineTune, refit) = FitFineTune(dots);
            var normal = dots.Where(d => d.Pose is null).SelectMany(d => d.Frames).ToList();
            _fineTune = (correction, fineTune, HeadPosition.Of(normal), refit, dots);
        }

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
                    _speaker.SayNow(HeadPose.Spoken(PoseHintText.Text));
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
        FineTuneText.Text = _fineTune is { } f
            ? (Helps ? "Fine-tune: " : "Fine-tune, nothing to adjust: ") + f.Report.Explanation
            : "No calibration in use, so nothing to fine-tune.";
        KeepButton.Visibility = Helps ? Visibility.Visible : Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
        // the answer only counts after closing the eyes, looking at the dots near the edges isn't one. nothing is said
        if (Helps) Choice.AskAfterLooking("Keep the fine-tune", "Close without it");
        else Choice.AskAfterLooking("Run it again", "Close");
    }

    // the fine-tune from the test's dots: the gaze numbers fitted again with all of them, then the correction on where
    // the bubble lands with those for the sitting normally ones. used as they are (the tracker's positions) if fitting
    // again doesn't work
    private (GazeCorrection, FineTuneReport, (GazeModel Model, GazeFitReport Report, List<GazeTargetSummary> Targets)?) FitFineTune(
        List<TargetRecording> dots)
    {
        var normal = dots.Where(d => d.Pose is null).ToList();
        var points = normal.Select(d => FineTune.Point(d.X, d.Y, d.Frames)).OfType<FineTunePoint>().ToList();
        var refit = _app.FitWithFineTune(dots);
        var fitPoints = points;
        if (refit is { } r)
            fitPoints = r.Targets
                .Where(t => t.FineTune && t.Pose is null && t.UsableFrames >= GazeFit.MinUsableFrames)
                .Select(t => GazeFit.Predict(r.Model, t, HeadCorrections) is { } p ? new FineTunePoint(t.X, t.Y, p.X, p.Y) : null)
                .OfType<FineTunePoint>()
                .ToList();
        // fitted again but too few dots with both eyes open to say anything: the old way
        if (fitPoints.Count < FineTune.MinPoints)
        {
            refit = null;
            fitPoints = points;
        }

        var (correction, fitted) = FineTune.Fit(fitPoints);
        var report = fitted with { HeadMode = _app.Settings.Current.Head.Mode };
        if (refit is null || points.Count == 0) return (correction, report, refit);

        double oldX = points.Average(p => Math.Abs(p.BubbleX - p.TargetX)), oldY = points.Average(p => Math.Abs(p.BubbleY - p.TargetY));
        return (correction, report with
        {
            Refitted = true,
            ErrorXOldFit = Math.Round(oldX, 3),
            ErrorYOldFit = Math.Round(oldY, 3),
            Explanation = $"the calibration was fitted again with these dots added: sitting normally the bubble went from " +
                          $"{oldX:P1} to {report.ErrorXBefore:P1} off left/right and from {oldY:P1} to {report.ErrorYBefore:P1} up/down. " +
                          (report.CorrectsX || report.CorrectsY ? "Then: " + report.Explanation : "A correction on top didn't make it better."),
        }, refit);
    }

    private void Keep()
    {
        if (_fineTune is not { } f || !Helps) return;
        _app.SaveFineTune(f.Correction, f.Report, f.At, f.Refit, f.Dots);
        Speaker.Ding();
        Close();
    }

    private void KeepButton_Click(object sender, RoutedEventArgs e) => Keep();

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
