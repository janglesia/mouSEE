using System.Windows;

namespace app;

public partial class App : Application
{
    // One tracker for the whole app: the overlay and the calibration wizard both subscribe to it.
    public TrackerClient Tracker { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Window window = e.Args.Contains("--calibration-preview", StringComparer.OrdinalIgnoreCase)
            ? new UI.CalibrationWindow()
            : new UI.OverlayWindow();

        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Tracker.Dispose(); // kills the Python process
        base.OnExit(e);
    }
}