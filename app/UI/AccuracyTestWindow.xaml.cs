using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using app.Calibration;

namespace app.UI;

// Checks how close the bubble lands to where you look, with the calibration in use: dots all over the screen (more
// than the calibration had), sitting normally and leaning both ways. The bubble isn't shown while it runs, so it
// can't be followed. At the end every dot is drawn with a line to where the bubble landed, and everything is saved
// in app/Calibration/Saved/tests/. The points and the math are in app/Calibration/AccuracyTest.cs
public partial class AccuracyTestWindow : Window
{
    private static readonly Dictionary<string, Brush> PhaseColors = new()
    {
        [AccuracyTest.Normal] = Frozen(Color.FromRgb(0xF7, 0xD0, 0x60)),
        [AccuracyTest.LeanLeft] = Frozen(Color.FromRgb(0x5D, 0xAD, 0xE2)),
        [AccuracyTest.LeanRight] = Frozen(Color.FromRgb(0xE0, 0x6C, 0xA8)),
    };

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
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        var active = _app.Calibrations.Active;
        CalibrationText.Text = active is null
            ? "No calibration, the default values are tested."
            : $"Testing \"{active.Name}\".";

        _app.Tracker.MessageReceived += OnTrackerMessage;
        Choice.Chosen += yes =>
        {
            if (yes) Start();
            else Close();
        };
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
        if (msg.Type != "frame" || _recording is null) return;
        // with the re-center's shift, so it tests where the bubble actually is
        var f = FrameSample.From(msg, _clock.ElapsedMilliseconds);
        var offset = _app.Offset;
        _recording.Add(f with { EstimateX = f.EstimateX - offset.X, EstimateY = f.EstimateY - offset.Y });
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
                phase = point.Phase;
                Dot.Visibility = Visibility.Collapsed;
                PhaseText.Text = AccuracyTest.Say(phase);
                PhaseText.Visibility = Visibility.Visible;
                await _speaker.SayAsync(PhaseText.Text, ct);
                // time to lean over before the first dot
                await Task.Delay(phase == AccuracyTest.Normal ? 500 : 1500, ct);
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
            frames.Add(new TargetRecording(i, point.X, point.Y, false, recorded, point.Phase != AccuracyTest.Normal));
        }

        Dot.Visibility = Visibility.Collapsed;
        ProgressText.Text = "";
        Cursor = Cursors.Arrow;
        Speaker.Ding();

        var report = AccuracyTest.Report(results, _app.Calibrations.Active, SystemParameters.PrimaryScreenWidth,
                                         SystemParameters.PrimaryScreenHeight) with { OffsetX = _app.Offset.X, OffsetY = _app.Offset.Y };
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
        Choice.Ask("Run it again", "Close");
        _ = _speaker.SayAsync("Done. " + (report.Summary.FirstOrDefault() ?? ""), CancellationToken.None);
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
