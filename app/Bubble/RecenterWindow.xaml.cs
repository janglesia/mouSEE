using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace app.Bubble;

// Re-center: a dot in the middle, looked at for a moment, see app/Bubble/Recenter.cs. Opens on startup (setting
// calibration.recenterOnStart), with Ctrl+Alt+R or r in the camera window. Closes by itself when it's done.
public partial class RecenterWindow : Window
{
    // the tracker can take a few seconds to start, wait for it this long
    private const int TrackerTimeoutMs = 30000;
    // time to find the dot before it starts looking (there's no voice to wait for)
    private const int ReadMs = 700;

    private readonly App _app = (App)Application.Current;
    private readonly Stopwatch _clock = new();
    private readonly CancellationTokenSource _cts = new();
    private List<FrameSample>? _recording;
    private TaskCompletionSource? _bubbleSeen;

    public RecenterWindow()
    {
        InitializeComponent();
        Middle.Show(this, true);
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        _app.Tracker.MessageReceived += OnTrackerMessage;
        Loaded += async (_, _) =>
        {
            Activate();
            Focus();
            Cursor = Cursors.None;
            var run = RunAsync(_cts.Token);
            await _app.StartTrackerAsync();
            await run;
        };
        Closing += (_, _) => _cts.Cancel();
        Closed += (_, _) =>
        {
            _app.Tracker.MessageReceived -= OnTrackerMessage;
        };
    }

    private void OnTrackerMessage(TrackerMessage msg)
    {
        if (msg.Type != "frame") return;
        var frame = FrameSample.From(msg, _clock.ElapsedMilliseconds);
        if (frame.EstimateX is not null) _bubbleSeen?.TrySetResult();
        // with the fine-tune, the shift is whatever is left after it
        if (frame.EstimateX is { } x && frame.EstimateY is { } y && _app.Calibrations.Active?.FineTune is { } fineTune)
        {
            var (fx, fy) = fineTune.Apply(x, y);
            frame = frame with { EstimateX = fx, EstimateY = fy };
        }
        _recording?.Add(frame);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            // the tracker may still be starting (on startup it is)
            _bubbleSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            MessageText.Text = "Starting the camera...";
            if (await Task.WhenAny(_bubbleSeen.Task, Task.Delay(TrackerTimeoutMs, ct)) != _bubbleSeen.Task)
            {
                await FinishAsync("The camera didn't start, skipping the re-center.", ct);
                return;
            }
            _bubbleSeen = null;

            // nothing said out loud: this runs every time mouSEE starts, and talking then gets old fast. the dot and
            // the text are enough, about as long as saying it took to find the dot
            MessageText.Text = "Look at the dot";
            await Task.Delay(ReadMs + Recenter.SettleMs, ct);

            var frames = new List<FrameSample>();
            _clock.Restart();
            _recording = frames;
            while (_clock.ElapsedMilliseconds < Recenter.CaptureMs)
            {
                SetRing(_clock.ElapsedMilliseconds / (double)Recenter.CaptureMs);
                await Task.Delay(16, ct);
            }
            _recording = null;
            SetRing(1);

            var (offset, at, problem) = Recenter.From(frames);
            if (offset is null)
            {
                await FinishAsync(problem!, ct);
                return;
            }
            _app.SetOffset(offset, at);
            Speaker.Ding();
            await FinishAsync("Done", ct, holdMs: 500);
        }
        catch (OperationCanceledException)
        {
            // Esc, nothing changes
        }
    }

    private async Task FinishAsync(string message, CancellationToken ct, int holdMs = 3500)
    {
        MessageText.Text = message;
        await Task.Delay(holdMs, ct);
        Close();
    }

    // dashes are in multiples of the stroke thickness: circumference / thickness for the whole ring
    private void SetRing(double progress)
    {
        double full = Math.PI * (Ring.ActualWidth - Ring.StrokeThickness) / Ring.StrokeThickness;
        Ring.StrokeDashArray = [Math.Clamp(progress, 0, 1) * full, full + 1];
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }
}
