using System.Windows;

namespace app;

public partial class App : Application
{
    // One tracker for the whole app: the overlay and the calibration wizard both subscribe to it.
    public TrackerClient Tracker { get; } = new();

    // One set of settings for the whole app, saved in app/Settings/settings.json
    public SettingsService Settings { get; } = new(new JsonSettingsStore());

    // Saved calibrations in app/Calibration/Saved/. The last one used is the one in use
    public CalibrationStore Calibrations { get; } = new();

    // the re-center: how far off the bubble was looking at the middle of the screen the last time (subtracted from
    // every bubble position), and where the head was then. Reset when the calibration in use changes
    public GazeOffset Offset { get; private set; } = GazeOffset.None;
    public HeadPosition? RecenteredAt { get; private set; }
    public event Action? OffsetChanged;

    // cursor movement: follows the gaze when enabled, but only with a calibrated and fine-tuned setup
    public bool IsCursorMovementEnabled { get; private set; }
    public event Action? CursorMovementChanged;

    // options only the app uses, face_tracker.py rejects flags it doesn't know.
    // --calibration-preview opens only the grid screen, --calibrate starts a whole calibration,
    // --accuracy-test and --fine-tune both open the accuracy test, which is the fine-tune too
    private static readonly string[] AppOnlyOptions = ["--calibration-preview", "--calibrate", "--accuracy-test", "--fine-tune"];

    // the command line minus AppOnlyOptions
    private string[] _commandLine = [];
    // what the tracker was started with, to know when it needs a restart. null until it's started
    private string[]? _trackerArgs;
    // and the numbers in the calibration it was started with: the file stays the same when they change
    private (GazeModel? Gaze, EyeThresholds? Eyes) _trackerValues;
    // while the eye check tries out its values, the tracker uses this file instead of the calibration in use
    private string? _trackerCalibrationOverride;

    // the eye check or the grid screen, whichever is open. only one calibration at a time
    private Window? _calibrationWindow;
    private CalibrationListWindow? _calibrationListWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool Has(string option) => e.Args.Contains(option, StringComparer.OrdinalIgnoreCase);
        _commandLine = e.Args.Where(a => !AppOnlyOptions.Contains(a, StringComparer.OrdinalIgnoreCase)).ToArray();

        // the tracker only reads its options when it starts
        Settings.Changed += async (_, _) => await RestartTrackerIfNeededAsync();
        Calibrations.Changed += async () =>
        {
            // a re-center belongs to the calibration it was made with
            if (Calibrations.Active?.Id != _offsetCalibrationId) SetOffset(GazeOffset.None, null);
            await RestartTrackerIfNeededAsync();
        };

        // q, p, c, r, f and m pressed in the camera window
        Tracker.MessageReceived += msg =>
        {
            if (msg.Type != "key") return;
            switch (msg.Key)
            {
                case "q": Shutdown(); break;
                case "p": TogglePause(); break;
                case "c": ShowCalibrations(); break;
                case "r": StartRecenter(); break;
                case "f": StartAccuracyTest(); break;
                case "m": ToggleCursorMovement(); break;
            }
        };

        FitOldCalibrations();

        if (Has("--calibration-preview"))
        {
            MainWindow = StartGazeCalibration(null);
            return;
        }

        MainWindow = new OverlayWindow();
        MainWindow.Show();

        if (Has("--calibrate"))
            StartCalibration();
        else if (Has("--accuracy-test") || Has("--fine-tune"))
            StartAccuracyTest();
        // first time (or every time until there is one): offer to calibrate, otherwise default values are used
        else if (Calibrations.Active is null && Settings.Current.Calibration.AskWhenMissing)
            ShowCalibrations();
        // the way you sit changes a bit from one day to the next, a quick re-center takes care of most of it
        else if (Calibrations.Active is not null && Settings.Current.Calibration.RecenterOnStart)
            StartRecenter();
    }

    /// <summary>Starts the shared tracker with the current settings. Does nothing if it's already running.</summary>
    public Task StartTrackerAsync()
    {
        if (_trackerArgs is null)
        {
            _trackerArgs = BuildTrackerArgs();
            _trackerValues = TrackerValues();
        }
        return Tracker.RunAsync(_trackerArgs);
    }

    public void TogglePause() =>
        Settings.Update(s => s with { Tracking = s.Tracking with { Paused = !s.Tracking.Paused } });

    /// <summary>
    /// A whole new calibration: the grid screen for where you look (with the head step), saved as a new calibration and
    /// used straight away, then the eye check (closed eyes, see docs/calibration/EYE-CALIBRATION.md), which adds to it.
    /// The eye check takes its open eyes and resting head angle from the grid. If one is already open it's brought to the front.
    /// </summary>
    public void StartCalibration()
    {
        if (_calibrationWindow is { } open)
        {
            open.Activate();
            return;
        }
        StartGazeCalibration(null, kept: StartEyeCheck);
    }

    /// <summary>The eye check for the calibration with this id, from its grid's frames sitting normally.</summary>
    private void StartEyeCheck(string id)
    {
        var open = (Calibrations.LoadRecording(id)?.Gaze ?? [])
            .Where(t => !t.IsValidation && !t.MoveHead && t.Pose is null && !t.FineTune)
            .SelectMany(t => t.Frames)
            .ToList();
        var eyeCheck = new EyeCheckWindow(open);
        eyeCheck.Completed += (eyes, report, recording) =>
        {
            Calibrations.Update(id, p => p with { Eyes = eyes, EyeCheck = report });
            var saved = Calibrations.LoadRecording(id) ?? new CalibrationRecording();
            SaveRecording(id, saved with { EyeCheck = recording.EyeCheck, CheckRound = recording.CheckRound });
        };
        ShowCalibrationWindow(eyeCheck);
    }

    /// <summary>The grid screen. Accepting adds to the calibration with this id, or saves a new one if it's null.
    /// What the eyes did at each target is saved with it (GazeTargets, and the raw frames in the recording).
    /// kept: called with the calibration's id once the window closes with it accepted (not done again).</summary>
    public GridWindow StartGazeCalibration(string? id, Action<string>? kept = null)
    {
        var window = new GridWindow();

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

        // accepting saves and fits it straight away, so the preview at the end shows the bubble with it. "Do the grid
        // again" there puts things back as they were (undo), closing or keeping it leaves it saved
        Action? undo = null;
        string? accepted = null;
        window.CalibrationDiscarded += () =>
        {
            undo?.Invoke();
            undo = null;
            accepted = null;
        };
        // after this one has closed, so _calibrationWindow is free for what comes next
        window.Closed += (_, _) =>
        {
            if (accepted is { } done && kept is not null) Dispatcher.BeginInvoke(() => kept(done));
        };
        window.CalibrationAccepted += () =>
        {
            var summaries = targets.Select(t => GazeTargetSummary.Of(t.Target, t.Frames)).ToList();
            var frames = targets.Select(t => new TargetRecording(t.Target.Index, t.Target.NormalizedX, t.Target.NormalizedY,
                                                                 t.Target.IsValidation, t.Frames, t.Target.MoveHead, t.Target.Pose)).ToList();
            // the gaze numbers fitted to the targets (left/right, and leaning left/right from the lean target).
            // none if there weren't enough targets
            var lean = targets.FirstOrDefault(t => t.Target.MoveHead);
            var fit = GazeFit.Fit(summaries, SystemParameters.PrimaryScreenWidth, lean.Frames, lean.Target?.NormalizedX ?? 0.5);
            var gaze = fit?.Model ?? new GazeModel();
            if (id is not null)
            {
                var before = Calibrations.All.FirstOrDefault(p => p.Id == id);
                var recordingBefore = Calibrations.LoadRecording(id);
                Calibrations.Update(id, p => p with { Gaze = gaze, GazeTargets = summaries, GazeFit = fit?.Report, FineTune = null, FineTuneReport = null });
                // the new frames are measured the current way, whatever the recording had before
                SaveRecording(id, (recordingBefore ?? new CalibrationRecording()) with
                {
                    Gaze = frames, Version = CalibrationRecording.CurrentVersion,
                });
                undo = () =>
                {
                    if (before is not null) Calibrations.Update(id, _ => before);
                    SaveRecording(id, recordingBefore ?? new CalibrationRecording());
                };
                accepted = id;
            }
            else
            {
                var saved = Calibrations.Add(new CalibrationProfile
                {
                    ScreenWidth = SystemParameters.PrimaryScreenWidth,
                    ScreenHeight = SystemParameters.PrimaryScreenHeight,
                    Gaze = gaze,
                    GazeTargets = summaries,
                    GazeFit = fit?.Report,
                });
                SaveRecording(saved.Id, new CalibrationRecording { Gaze = frames });
                undo = () => Calibrations.Delete(saved.Id);
                accepted = saved.Id;
            }
            // a re-center belongs to the gaze numbers it was made with
            SetOffset(GazeOffset.None, null);
        };
        ShowCalibrationWindow(window);
        return window;
    }

    // calibrations fitted by an older version of the fit (GazeFit.Version), or not at all, get fitted again from their
    // recording, so improvements to the fit work without calibrating again. without a recording, only the old summaries
    // are there and they get fitted from those. recordings from before the vertical iris offset came from the eye corners
    // (and the old summaries) measured it from the lids, which the tracker doesn't anymore, so up/down is fitted from the
    // lids only for them
    private void FitOldCalibrations()
    {
        foreach (var p in Calibrations.All.ToList())
        {
            if ((p.GazeFit?.Version ?? 0) >= GazeFit.Version) continue;

            var recording = Calibrations.LoadRecording(p.Id);
            var recorded = recording?.Gaze ?? [];
            if (recorded.Count > 0)
            {
                var targets = recorded.Select(r => GazeTargetSummary.Of(r.Target, r.Frames)).ToList();
                if (recording!.Version < CalibrationRecording.CornerOffsetVVersion)
                    targets = targets.Select(t => t.WithoutVertical()).ToList();
                var lean = recorded.FirstOrDefault(r => r.MoveHead);
                if (GazeFit.Fit(targets, p.ScreenWidth, lean?.Frames, lean?.X ?? 0.5) is { } fit)
                    Calibrations.Update(p.Id, q => q with { Gaze = fit.Model, GazeTargets = targets, GazeFit = fit.Report, FineTune = null, FineTuneReport = null });
            }
            else if (p.GazeTargets.Count > 0)
            {
                var targets = p.GazeTargets.Select(t => t.WithoutVertical()).ToList();
                if (GazeFit.Fit(targets, p.ScreenWidth) is { } fit)
                    Calibrations.Update(p.Id, q => q with { Gaze = fit.Model, GazeTargets = targets, GazeFit = fit.Report, FineTune = null, FineTuneReport = null });
            }
        }
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

    /// <summary>Look at a dot in the middle for a moment, see RecenterWindow. Does nothing while another calibration window is open.</summary>
    public void StartRecenter()
    {
        if (_calibrationWindow is { } open)
        {
            open.Activate();
            return;
        }
        ShowCalibrationWindow(new RecenterWindow());
    }

    /// <summary>The re-center's result: the shift for every bubble position and where the head was.</summary>
    public void SetOffset(GazeOffset offset, HeadPosition? at)
    {
        Offset = offset;
        RecenteredAt = at;
        _offsetCalibrationId = Calibrations.Active?.Id;
        OffsetChanged?.Invoke();
    }

    private string? _offsetCalibrationId;

    /// <summary>Toggles cursor movement on/off. Only works if there's an active calibration with a fine-tune.</summary>
    public void ToggleCursorMovement()
    {
        // Can only enable if we have a calibration that's been fine-tuned
        bool canEnable = Calibrations.Active is not null && Calibrations.Active.FineTuneReport is not null;
        if (!canEnable && !IsCursorMovementEnabled) return; // Don't enable without calibration + fine-tune

        IsCursorMovementEnabled = !IsCursorMovementEnabled;
        CursorMovementChanged?.Invoke();
    }

    /// <summary>Disables cursor movement (called when any key is pressed).</summary>
    public void DisableCursorMovement()
    {
        if (IsCursorMovementEnabled)
        {
            IsCursorMovementEnabled = false;
            CursorMovementChanged?.Invoke();
        }
    }

    /// <summary>Where the bubble goes for the tracker's x, y: the fine-tune of the calibration in use, then the re-center's shift.</summary>
    public (double X, double Y) Bubble(double x, double y) => FineTune.Bubble(x, y, Calibrations.Active?.FineTune, Offset);

    /// <summary>The calibration in use fitted again with the fine-tune's dots added to its grid targets (replacing an
    /// earlier fine-tune's): the new gaze numbers, how they fit, and the targets they were fitted to. null without a
    /// calibration with targets, or if it can't be fitted.</summary>
    public (GazeModel Model, GazeFitReport Report, List<GazeTargetSummary> Targets)? FitWithFineTune(IReadOnlyList<TargetRecording> dots)
    {
        if (Calibrations.Active is not { } active || active.GazeTargets.Count == 0) return null;
        var targets = active.GazeTargets.Where(t => !t.FineTune)
            .Concat(dots.Select(d => GazeTargetSummary.Of(d.Target, d.Frames)))
            .ToList();
        // the single lean target of calibrations from before the head step
        var lean = Calibrations.LoadRecording(active.Id)?.Gaze.FirstOrDefault(r => r.MoveHead);
        return GazeFit.Fit(targets, active.ScreenWidth, lean?.Frames, lean?.X ?? 0.5) is { } fit
            ? (fit.Model, fit.Report, targets)
            : null;
    }

    /// <summary>Keeps the fine-tune with the calibration in use: the correction, and when the gaze numbers were fitted
    /// again with its dots (refit), those and the dots' frames. It covers the re-center's shift too, so that starts over,
    /// from where the head was during the fine-tune.</summary>
    public void SaveFineTune(GazeCorrection correction, FineTuneReport report, HeadPosition? at,
                             (GazeModel Model, GazeFitReport Report, List<GazeTargetSummary> Targets)? refit = null,
                             IReadOnlyList<TargetRecording>? dots = null)
    {
        if (Calibrations.Active is not { } active) return;
        Calibrations.Update(active.Id, p => refit is { } r
            ? p with { Gaze = r.Model, GazeFit = r.Report, GazeTargets = r.Targets, FineTune = correction, FineTuneReport = report }
            : p with { FineTune = correction, FineTuneReport = report });
        // with the grid's frames, so fitting it again later (FitOldCalibrations) has them too. a calibration without a
        // recording only has the summaries, which have the dots now
        if (refit is not null && dots is not null && Calibrations.LoadRecording(active.Id) is { } recording)
            SaveRecording(active.Id, recording with { Gaze = [.. recording.Gaze.Where(t => !t.FineTune), .. dots] });
        SetOffset(GazeOffset.None, at);
    }

    /// <summary>The accuracy test and fine-tune in one, see AccuracyTestWindow. Without a calibration in use it only tests.</summary>
    public void StartAccuracyTest()
    {
        if (_calibrationWindow is { } open)
        {
            open.Activate();
            return;
        }
        ShowCalibrationWindow(new AccuracyTestWindow());
    }

    /// <summary>The list of saved calibrations, to switch, delete or make a new one.</summary>
    public void ShowCalibrations()
    {
        if (_calibrationListWindow is { } open)
        {
            open.Activate();
            return;
        }

        var window = new CalibrationListWindow();
        window.Closed += (_, _) => _calibrationListWindow = null;
        _calibrationListWindow = window;
        window.Show();
    }

    private string[] BuildTrackerArgs() =>
        TrackerArgs.Build(Settings.Current, _trackerCalibrationOverride ?? Calibrations.ActiveFile, _commandLine);

    // the tracker reads the calibration file when it starts, so new numbers in it need a restart too
    private (GazeModel?, EyeThresholds?) TrackerValues() =>
        _trackerCalibrationOverride is not null ? (null, null) : (Calibrations.Active?.Gaze, Calibrations.Active?.Eyes);

    private async Task RestartTrackerIfNeededAsync()
    {
        if (_trackerArgs is null) return; // not started yet, it'll start with the new ones
        var args = BuildTrackerArgs();
        var values = TrackerValues();
        if (args.SequenceEqual(_trackerArgs) && values == _trackerValues) return;
        _trackerArgs = args;
        _trackerValues = values;
        _restarting = Tracker.RestartAsync(args);
        await _restarting;
    }

    // the last restart, so WhenTrackerUpToDateAsync can wait for one that's already under way
    private Task _restarting = Task.CompletedTask;

    /// <summary>Returns once the tracker runs with the current settings and calibration: restarts it if they changed,
    /// or waits for a restart that's already going. Frames after this use them (the first take a few seconds).</summary>
    public async Task WhenTrackerUpToDateAsync()
    {
        await RestartTrackerIfNeededAsync();
        await _restarting;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Tracker.Dispose(); // kills the Python process
        base.OnExit(e);
    }
}
