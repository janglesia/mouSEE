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
}
