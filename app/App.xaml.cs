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

    // options only the app uses, face_tracker.py rejects flags it doesn't know.
    // --calibration-preview opens only the nine-point screen, --calibrate starts a whole calibration
    private static readonly string[] AppOnlyOptions = ["--calibration-preview", "--calibrate"];

    // the command line minus AppOnlyOptions
    private string[] _commandLine = [];
    // what the tracker was started with, to know when it needs a restart. null until it's started
    private string[]? _trackerArgs;
    // while the eye check tries out its values, the tracker uses this file instead of the calibration in use
    private string? _trackerCalibrationOverride;

    // the eye check or the nine-point screen, whichever is open. only one calibration at a time
    private Window? _calibrationWindow;
    private UI.CalibrationsWindow? _calibrationsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool Has(string option) => e.Args.Contains(option, StringComparer.OrdinalIgnoreCase);
        _commandLine = e.Args.Where(a => !AppOnlyOptions.Contains(a, StringComparer.OrdinalIgnoreCase)).ToArray();

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

        if (Has("--calibration-preview"))
        {
            MainWindow = StartGazeCalibration(null);
            return;
        }

        MainWindow = new UI.OverlayWindow();
        MainWindow.Show();

        if (Has("--calibrate"))
            StartCalibration();
        // first time (or every time until there is one): offer to calibrate, otherwise default values are used
        else if (Calibrations.Active is null && Settings.Current.Calibration.AskWhenMissing)
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

    /// <summary>
    /// A whole new calibration: the eye check first (closed eyes and head angle, see docs/EYE-CALIBRATION.md),
    /// then the nine-point screen for where you look. The eye check's values are saved as a new calibration
    /// and used straight away, the nine-point screen adds to it. If one is already open it's brought to the front.
    /// </summary>
    public void StartCalibration()
    {
        if (_calibrationWindow is { } open)
        {
            open.Activate();
            return;
        }

        var eyeCheck = new UI.EyeCheckWindow();
        eyeCheck.Completed += (eyes, report, recording) =>
        {
            var saved = Calibrations.Add(new CalibrationProfile
            {
                ScreenWidth = SystemParameters.PrimaryScreenWidth,
                ScreenHeight = SystemParameters.PrimaryScreenHeight,
                Eyes = eyes,
                EyeCheck = report,
            });
            SaveRecording(saved.Id, recording);
            // after this one has closed, so _calibrationWindow is free
            Dispatcher.BeginInvoke(() => StartGazeCalibration(saved.Id));
        };
        ShowCalibrationWindow(eyeCheck);
    }

    /// <summary>The nine-point screen. Accepting adds to the calibration with this id, or saves a new one if it's null.
    /// What the eyes did at each target is saved with it (GazeTargets, and the raw frames in the recording).</summary>
    public UI.CalibrationWindow StartGazeCalibration(string? id)
    {
        var window = new UI.CalibrationWindow();

        // the frames while each target's ring fills, that's when the eyes are on it
        var targets = new List<(CalibrationTarget Target, List<FrameSample> Frames)>();
        List<FrameSample>? capturing = null;
        var clock = new System.Diagnostics.Stopwatch();
        void OnFrame(TrackerMessage msg)
        {
            if (msg.Type == "frame") capturing?.Add(FrameSample.From(msg, clock.ElapsedMilliseconds));
        }
        Tracker.MessageReceived += OnFrame;
        window.Closed += (_, _) => Tracker.MessageReceived -= OnFrame;
        window.CalibrationStarted += () => targets.Clear(); // Try again starts over
        window.TargetCaptureStarted += target =>
        {
            capturing = [];
            clock.Restart();
            targets.Add((target, capturing));
        };
        window.TargetAccepted += _ => capturing = null;

        window.CalibrationAccepted += () =>
        {
            var summaries = targets.Select(t => GazeTargetSummary.Of(t.Target, t.Frames)).ToList();
            var frames = targets.Select(t => new TargetRecording(t.Target.Index, t.Target.NormalizedX, t.Target.NormalizedY,
                                                                 t.Target.IsValidation, t.Frames)).ToList();
            // the gaze numbers go here once calibration works them out from the targets. empty means the tracker's defaults
            var gaze = new GazeModel();
            if (id is not null)
            {
                Calibrations.Update(id, p => p with { Gaze = gaze, GazeTargets = summaries });
                SaveRecording(id, (Calibrations.LoadRecording(id) ?? new CalibrationRecording()) with { Gaze = frames });
            }
            else
            {
                var saved = Calibrations.Add(new CalibrationProfile
                {
                    ScreenWidth = SystemParameters.PrimaryScreenWidth,
                    ScreenHeight = SystemParameters.PrimaryScreenHeight,
                    Gaze = gaze,
                    GazeTargets = summaries,
                });
                SaveRecording(saved.Id, new CalibrationRecording { Gaze = frames });
            }
        };
        ShowCalibrationWindow(window);
        return window;
    }

    // the raw frames are nice to have, not being able to write them mustn't lose the calibration itself
    private void SaveRecording(string id, CalibrationRecording recording)
    {
        try
        {
            Calibrations.SaveRecording(id, recording);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[calibration] can't save the recording: {ex.Message}");
        }
    }

    /// <summary>For the eye check: run the tracker with this calibration file instead of the one in use, null to go back.
    /// Returns once the old tracker is gone, the new one takes a few seconds to send frames.</summary>
    public async Task UseTrackerCalibrationAsync(string? file)
    {
        _trackerCalibrationOverride = file;
        await RestartTrackerIfNeededAsync();
    }

    private void ShowCalibrationWindow(Window window)
    {
        window.Closed += (_, _) =>
        {
            if (_calibrationWindow == window) _calibrationWindow = null;
        };
        _calibrationWindow = window;
        window.Show();
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

    private string[] BuildTrackerArgs() =>
        TrackerArgs.Build(Settings.Current, _trackerCalibrationOverride ?? Calibrations.ActiveFile, _commandLine);

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
