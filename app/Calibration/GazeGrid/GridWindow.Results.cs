using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace app.Calibration.GazeGrid;

// The grid screen's result: how the run went, and the visual test (a dot follows the gaze). See GridWindow.xaml.cs.
public partial class GridWindow
{
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

        // ---- overall. how accurate it is shows in the preview after "Try it out", the accuracy test measures it
        bool captureOk = solid == total && stabilityLevel is Level.Good or Level.Fair;

        string quality, title, subtitle, next;
        Level qualityLevel;

        if (!captureOk)
        {
            quality = "Retry suggested"; qualityLevel = Level.Poor;
            title = "Calibration needs another try";
            subtitle = "Tracking dropped out during some of the targets.";
            next = "Sit centered, keep your head still, keep both eyes open, and try again.";
        }
        else
        {
            quality = "Captured"; qualityLevel = Level.Good;
            title = "Calibration captured";
            subtitle = "Every target was captured with both eyes tracked.";
            next = "Try it out: it's saved and you see the bubble with it, then keep it or do the grid again if you moved during it.";
        }

        QualityValue.Text = quality;
        QualityValue.Foreground = LevelBrush(qualityLevel);
        QualityDetail.Text = $"Took {_sessionClock.Elapsed.TotalSeconds:0} seconds";
        ResultTitle.Text = title;
        ResultSubtitle.Text = subtitle;
        NextStepText.Text = next;
    }

    public void ShowCalibrationFailure(string message)
    {
        if(!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => ShowCalibrationFailure(message));
            return;
        }

        ResultTitle.Text = "Calibration failed";
        ResultSubtitle.Text = message;
        QualityValue.Text = "Failed";
        QualityDetail.Text = "No calibration was saved.";
        NextStepText.Text = "Start calibration again manually.";

        ShowPhase(Phase.Failure);
    }

    // =====================================================================
    // The preview: the calibration is saved, the 20 points stay up as rings and the bubble follows the eyes with it,
    // like the fine-tune's preview. Keep it, or do the grid again (which puts back what was saved)
    // =====================================================================

    // the bubble moves a third of the way to each new frame, so it can be read and doesn't shake
    private const double PreviewSmoothing = 0.35;
    private const int PreviewTrackerTimeoutMs = 30000;
    private (double X, double Y)? _previewBubble;
    private TaskCompletionSource? _previewFirstFrame;

    private async Task ShowPreviewAsync()
    {
        foreach (var ring in _previewRings) PreviewLayer.Children.Remove(ring);
        _previewRings.Clear();
        foreach (var (x, y) in Targets)
        {
            var ring = new Ellipse { Width = 40, Height = 40, Stroke = RingStroke, StrokeThickness = 3 };
            Canvas.SetLeft(ring, x * Root.ActualWidth - 20);
            Canvas.SetTop(ring, y * Root.ActualHeight - 20);
            PreviewLayer.Children.Add(ring);
            _previewRings.Add(ring);
        }
        GazeDot.Visibility = Visibility.Collapsed;
        _previewBubble = null;
        PreviewText.Text = "Starting the tracker with your new calibration...";
        PreviewButtons.Visibility = Visibility.Collapsed;
        ShowPhase(Phase.Preview);

        // the tracker restarts with the new numbers, wait for it and its first bubble
        var app = (App)Application.Current;
        await app.WhenTrackerUpToDateAsync();
        _previewFirstFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Task.WhenAny(_previewFirstFrame.Task, Task.Delay(PreviewTrackerTimeoutMs));
        if (_phase != Phase.Preview) return;

        PreviewText.Text = "Look at the rings. The red dot is where the bubble lands with your new calibration. " +
                           "When you're done looking, close your eyes for 2 seconds, then choose.";
        PreviewButtons.Visibility = Visibility.Visible;
        _onChoice = yes => { if (yes) Close(); else Redo(); };
        Choice.AskAfterLooking("Keep it", "Do the grid again");
        _ = _speaker.SayAsync("Done. Look at the rings, the red dot is where the bubble lands now. " +
                              "When you're done looking, close your eyes for two seconds, then choose.", CancellationToken.None);
    }

    // the bubble with the new calibration: the tracker's point, with the re-center's shift (none yet) and no fine-tune
    private void UpdateGazeDot(TrackerMessage msg)
    {
        if (msg.Faces?.FirstOrDefault()?.Eyes?.Estimate is not { Valid: true, X: double x, Y: double y })
        {
            GazeDot.Visibility = Visibility.Collapsed;
            return;
        }
        _previewFirstFrame?.TrySetResult();
        var now = ((App)Application.Current).Bubble(x, y);
        _previewBubble = _previewBubble is { } l
            ? (l.X + (now.X - l.X) * PreviewSmoothing, l.Y + (now.Y - l.Y) * PreviewSmoothing)
            : now;
        Canvas.SetLeft(GazeDot, Math.Clamp(_previewBubble.Value.X, 0, 1) * Root.ActualWidth - GazeDot.Width / 2);
        Canvas.SetTop(GazeDot, Math.Clamp(_previewBubble.Value.Y, 0, 1) * Root.ActualHeight - GazeDot.Height / 2);
        GazeDot.Visibility = Visibility.Visible;
    }
}
