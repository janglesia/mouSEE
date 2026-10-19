using System.Windows;
using app.Calibration;
using app.Settings;

namespace app;

public partial class App : Application
{
    // One tracker for the whole app: the overlay and the calibration wizard both subscribe to it.
    public TrackerClient Tracker { get; } = new();

    // One set of settings for the whole app, saved in app/Settings/settings.json
    public SettingsService Settings { get; } = new(new JsonSettingsStore());

    // Saved calibrations in app/Calibration/Saved/. The last one used is the one in use
    public CalibrationStore Calibrations { get; } = new();

    // the command line minus --calibration-preview, face_tracker.py rejects flags it doesn't know
    private string[] _commandLine = [];
    // what the tracker was started with, to know when it needs a restart. null until it's started
    private string[]? _trackerArgs;

    private UI.CalibrationWindow? _calibrationWindow;
    private UI.CalibrationsWindow? _calibrationsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool calibrationPreview = e.Args.Contains("--calibration-preview", StringComparer.OrdinalIgnoreCase);
        _commandLine = e.Args
            .Where(a => !a.Equals("--calibration-preview", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // the tracker only reads its options when it starts
        Settings.Changed += async (_, _) => await RestartTrackerIfNeededAsync();
        Calibrations.Changed += async () => await RestartTrackerIfNeededAsync();

        // q, p and c pressed in the camera window
        Tracker.MessageReceived += msg =>
        {
            if (msg.Type != "key") return;
            switch (msg.Key)
            {
                case "q": Shutdown(); break;
                case "p": TogglePause(); break;
                case "c": ShowCalibrations(); break;
            }
        };

        if (calibrationPreview)
        {
            MainWindow = StartCalibration();
            return;
        }

        MainWindow = new UI.OverlayWindow();
        MainWindow.Show();

        // first time (or every time until there is one): offer to calibrate, otherwise default values are used
        if (Calibrations.Active is null && Settings.Current.Calibration.AskWhenMissing)
            ShowCalibrations();
    }

    /// <summary>Starts the shared tracker with the current settings. Does nothing if it's already running.</summary>
    public Task StartTrackerAsync()
    {
        _trackerArgs ??= BuildTrackerArgs();
        return Tracker.RunAsync(_trackerArgs);
    }

    public void TogglePause() =>
        Settings.Update(s => s with { Tracking = s.Tracking with { Paused = !s.Tracking.Paused } });

    /// <summary>Opens the calibration wizard, or brings it to the front if it's open. Accepting saves it and switches to it.</summary>
    public UI.CalibrationWindow StartCalibration()
    {
        if (_calibrationWindow is { } open)
        {
            open.Activate();
            return open;
        }

        var window = new UI.CalibrationWindow();
        window.CalibrationAccepted += () => Calibrations.Add(new CalibrationProfile
        {
            ScreenWidth = SystemParameters.PrimaryScreenWidth,
            ScreenHeight = SystemParameters.PrimaryScreenHeight,
            // the gaze numbers go here once calibration works them out. empty means the tracker's defaults
            Gaze = new GazeModel(),
        });
        window.Closed += (_, _) => _calibrationWindow = null;
        _calibrationWindow = window;
        window.Show();
        return window;
    }

    /// <summary>The list of saved calibrations, to switch, delete or make a new one.</summary>
    public void ShowCalibrations()
    {
        if (_calibrationsWindow is { } open)
        {
            open.Activate();
            return;
        }

        var window = new UI.CalibrationsWindow();
        window.Closed += (_, _) => _calibrationsWindow = null;
        _calibrationsWindow = window;
        window.Show();
    }

    private string[] BuildTrackerArgs() => TrackerArgs.Build(Settings.Current, Calibrations.ActiveFile, _commandLine);

    private async Task RestartTrackerIfNeededAsync()
    {
        if (_trackerArgs is null) return; // not started yet, it'll start with the new ones
        var args = BuildTrackerArgs();
        if (args.SequenceEqual(_trackerArgs)) return;
        _trackerArgs = args;
        await Tracker.RestartAsync(args);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Tracker.Dispose(); // kills the Python process
        base.OnExit(e);
    }
}
