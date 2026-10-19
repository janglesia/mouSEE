using System.Windows;
using app.Settings;

namespace app;

public partial class App : Application
{
    // One tracker for the whole app: the overlay and the calibration wizard both subscribe to it.
    public TrackerClient Tracker { get; } = new();

    // One set of settings for the whole app, saved in app/Settings/settings.json
    public SettingsService Settings { get; } = new(new JsonSettingsStore());

    // the command line minus --calibration-preview, face_tracker.py rejects flags it doesn't know
    private string[] _commandLine = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool calibration = e.Args.Contains("--calibration-preview", StringComparer.OrdinalIgnoreCase);
        _commandLine = e.Args
            .Where(a => !a.Equals("--calibration-preview", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // the tracker only reads its options when it starts
        Settings.Changed += async (old, now) =>
        {
            if (!TrackerArgs.Build(old, []).SequenceEqual(TrackerArgs.Build(now, [])))
                await Tracker.RestartAsync(TrackerArgs.Build(now, _commandLine));
        };

        // q and p pressed in the camera window
        Tracker.MessageReceived += msg =>
        {
            if (msg.Type != "key") return;
            if (msg.Key == "q") Shutdown();
            else if (msg.Key == "p") TogglePause();
        };

        Window window = calibration ? new UI.CalibrationWindow() : new UI.OverlayWindow();

        MainWindow = window;
        window.Show();
    }

    /// <summary>Starts the shared tracker with the current settings. Does nothing if it's already running.</summary>
    public Task StartTrackerAsync() => Tracker.RunAsync(TrackerArgs.Build(Settings.Current, _commandLine));

    public void TogglePause() =>
        Settings.Update(s => s with { Tracking = s.Tracking with { Paused = !s.Tracking.Paused } });

    protected override void OnExit(ExitEventArgs e)
    {
        Tracker.Dispose(); // kills the Python process
        base.OnExit(e);
    }
}
