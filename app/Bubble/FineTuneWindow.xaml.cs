using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;

namespace app.Bubble;

// The fine-tune, the last adjustments after a calibration: 20 dots around the screen with the bubble hidden, and for
// each where the bubble landed against where it should have. That gives a correction on the bubble (app/Bubble/FineTune.cs),
// shown live next to the bubble without it, and kept with the calibration if you want it. Opens from the
// calibrations window, with Ctrl+Alt+F or f in the camera window. Not after calibrating any more: the 20-point grid fits
// the curves itself, this is for when the bubble is off on a later day.
public partial class FineTuneWindow : Window
{
    // the live bubbles move a third of the way to each new frame, so they can be read and not shake
    private const double Smoothing = 0.35;

    private readonly App _app = (App)Application.Current;
    private readonly Speaker _speaker = new();
    private readonly Stopwatch _clock = new();
    private CancellationTokenSource? _cts;
    private List<FrameSample>? _recording;
    private bool _running;
    private readonly List<UIElement> _drawn = [];

    // after the dots: what it found, and the bubbles while you try it out
    private (GazeCorrection Correction, FineTuneReport Report, HeadPosition? At)? _result;
    private (double X, double Y)? _before, _after;

    public FineTuneWindow()
    {
        InitializeComponent();
        Middle.Follow(Choice);
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        var active = _app.Calibrations.Active;
        CalibrationText.Text = active?.FineTune is null
            ? $"For \"{active?.Name}\"."
            : $"For \"{active.Name}\". It already has a fine-tune, this one replaces it if you keep it.";

        _app.Tracker.MessageReceived += OnTrackerMessage;
        Choice.Chosen += yes =>
        {
            if (!yes) Close();
            else if (_result is { Report.CorrectsX: false, Report.CorrectsY: false } or null) Start();
            else Keep();
        };
        // the preview: the answer only counts after closing the eyes, looking at the rings near the edges isn't one
        Choice.Ready += () => _ = _speaker.SayAsync(
            "To keep it, look to the right for three seconds. To discard it, look to the left.", CancellationToken.None);
        Loaded += async (_, _) =>
        {
            Activate();
            Focus();
            Choice.Ask("Begin", "Close");
            _ = _speaker.SayAsync("Fine-tune. To begin, look to the right for three seconds. To close, look to the left.",
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
        // the frames without any fine-tune or re-center, the correction is fitted on those
        _recording?.Add(FrameSample.From(msg, _clock.ElapsedMilliseconds));

        if (_result is not { } result || ResultPanel.Visibility != Visibility.Visible) return;
        if (msg.Faces?.FirstOrDefault()?.Eyes?.Estimate is not { Valid: true, X: double x, Y: double y })
        {
            BeforeBubble.Visibility = AfterBubble.Visibility = Visibility.Collapsed;
            return;
        }
        _before = Smooth(_before, _app.Bubble(x, y));
        _after = Smooth(_after, result.Correction.Apply(x, y));
        Place(BeforeBubble, _before.Value);
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
        _result = null;
        _before = _after = null;
        Cursor = Cursors.None;

        await _speaker.SayAsync("Look at each dot until the next one shows up.", ct);

        var dots = FineTune.Dots;
        var points = new List<FineTunePoint>();
        var all = new List<FrameSample>();
        for (int i = 0; i < dots.Count; i++)
        {
            var (dx, dy) = dots[i];
            ProgressText.Text = $"Dot {i + 1} of {dots.Count}";
            Canvas.SetLeft(Dot, dx * Root.ActualWidth - Dot.Width / 2);
            Canvas.SetTop(Dot, dy * Root.ActualHeight - Dot.Height / 2);
            Dot.Visibility = Visibility.Visible;
            await Task.Delay(FineTune.SettleMs, ct);

            var recorded = new List<FrameSample>();
            _clock.Restart();
            _recording = recorded;
            try
            {
                await Task.Delay(FineTune.CaptureMs, ct);
            }
            finally
            {
                _recording = null;
            }
            all.AddRange(recorded);
            if (FineTune.Point(dx, dy, recorded) is { } point) points.Add(point);
        }

        Dot.Visibility = Visibility.Collapsed;
        ProgressText.Text = "";
        Cursor = Cursors.Arrow;
        Speaker.Ding();

        var (correction, fitted) = FineTune.Fit(points);
        var report = fitted with { HeadMode = _app.Settings.Current.Head.Mode };
        _result = (correction, report, HeadPosition.Of(all));
        foreach (var (dx, dy) in dots)
            Add(new Ellipse { Width = 26, Height = 26, Stroke = Dot.Fill, StrokeThickness = 2 }, dx * Root.ActualWidth - 13, dy * Root.ActualHeight - 13);

        ResultText.Text = report.Explanation;
        bool helps = report.CorrectsX || report.CorrectsY;
        ResultTitle.Text = helps ? "Try it out" : "Nothing to adjust";
        LegendText.Visibility = KeepButton.Visibility = helps ? Visibility.Visible : Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
        if (helps)
        {
            Choice.AskAfterLooking("Keep it", "Discard");
            await _speaker.SayAsync("Done. Look at the rings. The yellow bubble is with the fine-tune, the grey one is how it is now. " +
                                    "When you're done looking, close your eyes for two seconds, then choose.", ct);
        }
        else
        {
            Choice.Ask("Try again", "Close");
            await _speaker.SayAsync("Done. A correction didn't make the bubble better, so nothing changes.", ct);
        }
    }

    private void Keep()
    {
        if (_result is not { } result) return;
        _app.SaveFineTune(result.Correction, result.Report, result.At);
        Speaker.Ding();
        Close();
    }

    private void Add(UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        Board.Children.Insert(0, element); // under the bubbles
        _drawn.Add(element);
    }

    private void BeginButton_Click(object sender, RoutedEventArgs e) => Start();
    private void KeepButton_Click(object sender, RoutedEventArgs e) => Keep();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }
}
