using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace app.Calibration.EyeCheck;

// Runs the eye check from docs/calibration/EYE-CALIBRATION.md, after the grid. The steps, their checks and the math are
// next to it in this folder (EyeCheckSteps, EyeCheckAnalysis, EyeCheckRound), this only runs them: says each
// instruction, records the tracker's frames, gives hints, repeats steps that didn't work, and at the
// end checks the new value with the tracker actually using it. The open eyes and the resting head angle come from the
// grid that ran just before.
public partial class EyeCheckWindow : Window
{
    private const int AttemptsPerStep = 3;
    private const int PauseBetweenStepsMs = 700;
    // hints only look at the last bit of a step, and only once people had time to react
    private const int HintWindowMs = 800;
    private const int HintAfterMs = EyeCheckSteps.ReactionMs + 300;
    // the tracker takes a few seconds to load MediaPipe after a restart
    private const int TrackerRestartTimeoutMs = 30000;

    private static readonly Brush Green = Frozen(Color.FromRgb(0x6F, 0xCF, 0x97));
    private static readonly Brush Amber = Frozen(Color.FromRgb(0xE0, 0xA4, 0x58));
    private static readonly Brush Grey = Frozen(Color.FromRgb(0x6C, 0x81, 0x79));
    // the grid screen's yellow. a bright screen reflects on glasses right over the iris, so the check round's open eyes
    // are measured on it, like the grid screen and a white web page
    private static readonly Brush Bright = Frozen(Color.FromRgb(0xF7, 0xD0, 0x60));
    private static readonly Brush Dark = Frozen(Color.FromRgb(0x10, 0x10, 0x10));
    private static readonly Brush DarkText = Frozen(Color.FromRgb(0x1C, 0x1C, 0x1C));
    private static readonly Brush StepGrey = Frozen(Color.FromRgb(0x8A, 0x8A, 0x8A));
    // the live line's colours on the yellow background, the green and amber don't read on it
    private static readonly Brush GoodOnBright = Frozen(Color.FromRgb(0x1F, 0x5F, 0x4A));
    private static readonly Brush OffOnBright = Frozen(Color.FromRgb(0x7A, 0x3B, 0x0E));
    private bool _bright;

    private readonly App _app = (App)Application.Current;
    // the grid's frames sitting normally: the open eyes, and the resting head angle the tilt steps compare against
    private readonly IReadOnlyList<FrameSample> _open;
    private readonly EyeCheckBaseline? _baseline;
    private readonly Speaker _speaker = new();
    private readonly Stopwatch _clock = new();
    private CancellationTokenSource? _cts;
    private bool _running;

    private List<FrameSample>? _recording;         // frames go here while a step records
    // the last moment's frames, always, for the live tilt guidance (EyeCheckSteps.TiltGuide)
    private readonly Queue<FrameSample> _recent = new();
    private readonly Stopwatch _recentClock = Stopwatch.StartNew();
    // the tilt steps: in range this long before recording starts, the hint said out loud after this, go on anyway after this
    private const int TiltHoldMs = 800, TiltHintAfterMs = 3000, TiltTimeoutMs = 12000;
    private TaskCompletionSource? _trackerBack;    // waiting for the tracker after it restarted
    private bool _trackerHasCheckValues;           // the tracker is running with the values being checked

    private EyeCheckOutcome? _outcome;
    private readonly List<StepReport> _stepReports = [];   // what each step measured
    private readonly List<string> _problems = [];          // steps left out, a check round that couldn't run
    private CheckRoundReport? _checkReport;
    private CalibrationRecording _raw = new();              // every frame, saved next to the calibration

    // what looking left / right answers, set with Ask
    private Action<bool>? _onChoice;

    /// <summary>Continue or Keep default values: the values for the tracker (empty for the defaults),
    /// the report of what was measured, and the raw frames.</summary>
    public event Action<EyeThresholds, EyeCheckReport, CalibrationRecording>? Completed;

    /// <summary>open: the frames of the grid that ran just before, sitting normally (App.StartEyeCheck).</summary>
    public EyeCheckWindow(IReadOnlyList<FrameSample> open)
    {
        InitializeComponent();
        _open = open;
        _baseline = EyeCheckSteps.Baseline(open);
        Middle.Follow(Choice);

        // the whole primary screen, so the middle dot is the screen's middle
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        _app.Tracker.MessageReceived += OnTrackerMessage;
        Choice.Chosen += yes => _onChoice?.Invoke(yes);
        Loaded += async (_, _) =>
        {
            Activate();
            Focus();
            AskIntro();
            _ = _speaker.SayAsync("Eye check, the last part. To begin, look to the right and hold it for three seconds. To skip it, look to the left.",
                                  CancellationToken.None);
            await _app.StartTrackerAsync(); // no-op if the overlay already started it
        };
        Closing += (_, _) => _cts?.Cancel();
        Closed += async (_, _) =>
        {
            _app.Tracker.MessageReceived -= OnTrackerMessage;
            _speaker.Dispose();
            if (_trackerHasCheckValues) await _app.UseTrackerCalibrationAsync(null);
        };

        ShowPhase(IntroPanel);
    }

    // =====================================================================
    // Tracker frames
    // =====================================================================

    private void OnTrackerMessage(TrackerMessage msg)
    {
        if (msg.Type != "frame") return;

        _recording?.Add(FrameSample.From(msg, _clock.ElapsedMilliseconds));
        long now = _recentClock.ElapsedMilliseconds;
        _recent.Enqueue(FrameSample.From(msg, now));
        while (now - _recent.Peek().Ms > HintWindowMs) _recent.Dequeue();
        if (msg.Faces is { Count: > 0 }) _trackerBack?.TrySetResult();

        if (IntroPanel.Visibility == Visibility.Visible)
        {
            var face = msg.Faces is { Count: > 0 } faces ? faces[0] : null;
            var (dot, text) = face switch
            {
                null => (Amber, "Can't see your face. Sit in front of the camera."),
                { Eyes: null } => (Amber, "Face found, but not your eyes. Check the lighting."),
                { Eyes.Left: null } or { Eyes.Right: null } => (Amber, "Only one eye found. Face the screen."),
                _ => (Green, "Face and both eyes found. Ready."),
            };
            FaceDot.Fill = dot;
            FaceText.Text = text;
        }
    }

    // =====================================================================
    // The steps
    // =====================================================================

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
            // closing the window
        }
        finally
        {
            _running = false;
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        _outcome = null;
        _stepReports.Clear();
        _problems.Clear();
        _checkReport = null;
        _raw = new CalibrationRecording();
        ShowPhase(RunPanel);

        // a redo after a check round: back to the values in use first, so the steps measure the same way
        if (_trackerHasCheckValues)
        {
            await RestartTrackerAsync(null, ct);
        }

        var steps = EyeCheckSteps.All;
        int total = steps.Count + 1; // + the check round
        var recordings = new Dictionary<EyeCheckStepKind, IReadOnlyList<FrameSample>>();
        var baseline = _baseline;

        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            StepText.Text = $"Step {i + 1} of {total}";
            StepCheck check = StepCheck.Fail("");
            List<FrameSample> usable = [];
            int tries = 0;

            for (int attempt = 1; attempt <= AttemptsPerStep; attempt++)
            {
                tries = attempt;
                var frames = await RecordStepAsync(step, baseline, ct);
                usable = EyeCheckSteps.Usable(frames);
                check = EyeCheckSteps.Check(step, usable, baseline);
                _raw.EyeCheck.Add(new StepRecording(step.Kind.ToString(), attempt, check.Ok, check.Ok ? null : check.Problem, frames));

                if (step.After is not null)
                {
                    InstructionText.Text = step.After;
                    HintText.Text = "";
                    await _speaker.SayAsync(step.After, ct);
                }
                if (check.Ok) break;

                HintText.Text = check.Problem;
                await _speaker.SayAsync(attempt < AttemptsPerStep ? $"{check.Problem} Let's try that again." : check.Problem!, ct);
            }

            _stepReports.Add(StepReport.Of(step, tries, check, usable));
            if (check.Ok) recordings[step.Kind] = usable;
            else _problems.Add($"Left out \"{step.Say}\" after {AttemptsPerStep} tries: {check.Problem}");

            await Task.Delay(PauseBetweenStepsMs, ct);
        }

        _outcome = EyeCheckAnalysis.Analyze(_open, recordings);

        // the check round: only worth it if the closed eye value changed
        if (_outcome.TunedDetection)
        {
            StepText.Text = $"Step {total} of {total}";
            var scores = await RunCheckRoundAsync(_outcome.Thresholds, ct);
            if (scores is not null)
                _checkReport = new CheckRoundReport
                {
                    Passed = EyeCheckRound.Passed(scores),
                    Scores = scores.OrderBy(s => s.Key).ToDictionary(s => EyeCheckRound.Name(s.Key), s => Math.Round(s.Value, 3)),
                };
        }

        ShowResult(_outcome);
    }

    // says the instruction, then records the step's frames while giving hints
    private async Task<List<FrameSample>> RecordStepAsync(EyeCheckStep step, EyeCheckBaseline? baseline, CancellationToken ct)
    {
        InstructionText.Text = step.Say;
        HintText.Text = "";
        SetLive(null);
        SetProgress(0);
        // the instruction moves up out of the middle dot's way
        Middle.Show(step, step.LooksAtMiddle);
        RunPanel.VerticalAlignment = step.LooksAtMiddle ? VerticalAlignment.Top : VerticalAlignment.Center;
        RunPanel.Margin = step.LooksAtMiddle ? new Thickness(0, 90, 0, 0) : new Thickness(0);
        // the tilt steps show the head angle from the moment the instruction starts being read, then wait for the head,
        // and only then say to close the eyes (the guidance can't be read with them closed)
        var saying = _speaker.SayAsync(step.Say, ct);
        while (step.IsTilt && !saying.IsCompleted)
        {
            SetLive(Tilt(step, baseline));
            await Task.Delay(50, ct);
        }
        await saying;
        if (step.IsTilt) await WaitForTiltAsync(step, baseline, ct);
        if (step.Close is not null)
        {
            InstructionText.Text = step.Close;
            await _speaker.SayAsync(step.Close, ct);
        }

        var frames = new List<FrameSample>();
        _clock.Restart();
        _recording = frames;
        bool hintSpoken = false;
        try
        {
            while (_clock.ElapsedMilliseconds < step.DurationMs)
            {
                long now = _clock.ElapsedMilliseconds;
                SetProgress(now / (double)step.DurationMs);

                // green "Good" when it's right, what's wrong once there's been time to react
                var status = EyeCheckSteps.LiveStatus(step, _recent.ToList(), baseline, problemsToo: now >= HintAfterMs);
                SetLive(status);
                // say one problem per try, more than that just talks over the person ("Good" isn't said)
                if (status is { Good: false } problem && !hintSpoken)
                {
                    _speaker.SayNow(HeadPose.Spoken(problem.Text));
                    hintSpoken = true;
                }
                await Task.Delay(50, ct);
            }
        }
        finally
        {
            _recording = null;
            SetLive(null);
            Middle.Show(step, false);
        }

        SetProgress(1);
        Speaker.Ding();
        return frames;
    }

    // the tilt guidance as a live line, or "can't see your face"
    private (bool, string)? Tilt(EyeCheckStep step, EyeCheckBaseline? baseline) =>
        baseline is null ? null
            : EyeCheckSteps.TiltGuide(step, _recent.ToList(), baseline) is { } g ? (g.InRange, g.Hint)
            : (false, "I can't see your face. Move back in front of the camera.");

    // live "a little more / a little less / good" until the head has been tilted far enough for a moment, then the step
    // records (and the guidance stays up during it). goes on anyway without a resting angle, or after a while
    private async Task WaitForTiltAsync(EyeCheckStep step, EyeCheckBaseline? baseline, CancellationToken ct)
    {
        if (baseline is null) return;
        var clock = Stopwatch.StartNew();
        long inSince = -1;
        bool hinted = false;
        while (clock.ElapsedMilliseconds < TiltTimeoutMs)
        {
            long now = clock.ElapsedMilliseconds;
            var guide = EyeCheckSteps.TiltGuide(step, _recent.ToList(), baseline);
            SetLive(Tilt(step, baseline));
            if (guide is { InRange: true })
            {
                if (inSince < 0) inSince = now;
                if (now - inSince >= TiltHoldMs) return;
            }
            else
            {
                inSince = -1;
                if (!hinted && now >= TiltHintAfterMs && guide is { } g)
                {
                    _speaker.SayNow(HeadPose.Spoken(g.Hint));
                    hinted = true;
                }
            }
            await Task.Delay(50, ct);
        }
    }

    // =====================================================================
    // The check round: the new value, with the tracker using it
    // =====================================================================

    private async Task<Dictionary<CheckTrial, double>?> RunCheckRoundAsync(EyeThresholds thresholds, CancellationToken ct)
    {
        SetBright(true);
        InstructionText.Text = "Loading your values...";
        HintText.Text = "";
        SetProgress(0);
        var say = _speaker.SayAsync("Now let's check that it works. One moment.", ct);

        string file = Path.Combine(Path.GetTempPath(), "mouSEE-eye-check.json");
        CalibrationStore.WriteFile(new CalibrationProfile { Name = "eye check", Eyes = thresholds }, file);
        bool back = await RestartTrackerAsync(file, ct);
        await say;
        if (!back)
        {
            _problems.Add("Couldn't check the new values: the tracker didn't come back after loading them.");
            return null;
        }

        var order = EyeCheckRound.Order(Random.Shared);
        var runs = new List<(CheckTrial, IReadOnlyList<FrameSample>)>();
        for (int i = 0; i < order.Count; i++)
        {
            var trial = order[i];
            string text = EyeCheckRound.Say(trial);
            InstructionText.Text = text;
            HintText.Text = $"Check {i + 1} of {order.Count}.  {EyeCheckRound.Eyes(trial)}";
            SetLive(null);
            SetProgress(0);
            await _speaker.SayAsync(text, ct);

            var frames = new List<FrameSample>();
            _clock.Restart();
            _recording = frames;
            try
            {
                while (_clock.ElapsedMilliseconds < EyeCheckRound.TrialMs)
                {
                    SetProgress(_clock.ElapsedMilliseconds / (double)EyeCheckRound.TrialMs);
                    SetLive(EyeCheckRound.LiveGood(trial, _recent.ToList()) is { } good ? (true, good) : null);
                    await Task.Delay(50, ct);
                }
            }
            finally
            {
                _recording = null;
                SetLive(null);
            }
            Speaker.Ding();
            runs.Add((trial, EyeCheckSteps.Usable(frames)));
            _raw.CheckRound.Add(new TrialRecording(trial.ToString(), frames));
        }

        SetBright(false);
        HintText.Text = "";
        // the last one may have been "Open both eyes" already, then saying it again sounds like a mistake
        if (order[^1] != CheckTrial.OpenBoth)
        {
            InstructionText.Text = "Open your eyes.";
            await _speaker.SayAsync(InstructionText.Text, ct);
        }
        return EyeCheckRound.Score(runs);
    }

    // restarts the tracker with these values (null = the calibration in use) and waits for it to see a face again
    private async Task<bool> RestartTrackerAsync(string? file, CancellationToken ct)
    {
        // the old tracker is gone once this returns, so any frame after it is from the new one
        await _app.UseTrackerCalibrationAsync(file);
        _trackerHasCheckValues = file is not null;
        var back = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _trackerBack = back;
        var waited = await Task.WhenAny(back.Task, Task.Delay(TrackerRestartTimeoutMs, ct));
        _trackerBack = null;
        ct.ThrowIfCancellationRequested();
        return waited == back.Task;
    }

    // =====================================================================
    // Results
    // =====================================================================

    private void ShowResult(EyeCheckOutcome outcome)
    {
        var lines = Report(null).Lines().ToList();
        if (_checkReport is not null)
        {
            lines.AddRange(_checkReport.Scores.Select(s => $"Check, {s.Key}: right {s.Value:P0} of the time" +
                                                           (s.Value < EyeCheckRound.MinMatch ? $" (needs {EyeCheckRound.MinMatch:P0})" : "")));
        }

        bool checkRan = _checkReport is not null;
        bool passed = _checkReport?.Passed ?? true;
        if (!passed)
        {
            ResultTitle.Text = "The new value didn't work well enough";
            ResultSubtitle.Text = "mouSEE couldn't reliably tell when your eyes are closed with it. " +
                                  "Try again with more light on your face, or keep the default value.";
        }
        else if (!outcome.TunedDetection)
        {
            ResultTitle.Text = "Eye check done, using the default value for closed eyes";
            ResultSubtitle.Text = "Your head angle was saved. The notes below say why the closed eye value couldn't be tuned.";
        }
        else
        {
            ResultTitle.Text = "Eye check done";
            ResultSubtitle.Text = checkRan
                ? "Your value works. Continue to save it, that finishes the calibration."
                : "Continue to save it, that finishes the calibration.";
        }

        NotesList.ItemsSource = lines;
        ContinueButton.Visibility = passed ? Visibility.Visible : Visibility.Collapsed;
        DefaultsButton.Visibility = passed ? Visibility.Collapsed : Visibility.Visible;
        ShowPhase(ResultPanel);
        if (passed)
            Ask("Continue: save it and finish", "Do the eye check again",
                yes => { if (yes) Continue(); else Start(); });
        else
            Ask("Keep the default value for closed eyes and finish", "Do the eye check again",
                yes => { if (yes) KeepDefaults(); else Start(); });
        _ = _speaker.SayAsync($"{ResultTitle.Text}. Look to the right to continue, or to the left to do it again.", CancellationToken.None);
    }

    // everything measured so far. decision says what was done with it, e.g. that the defaults were kept
    private EyeCheckReport Report(string? decision, List<EyeValueReport>? values = null) => new()
    {
        MadeAt = DateTimeOffset.Now,
        Values = values ?? _outcome?.Values ?? [],
        Steps = [.. _stepReports],
        Check = _checkReport,
        Warnings = [.. (_outcome?.Warnings ?? []), .. _problems],
        Decision = decision,
    };

    // =====================================================================
    // Buttons
    // =====================================================================

    private void BeginButton_Click(object sender, RoutedEventArgs e) => Start();

    private void RedoButton_Click(object sender, RoutedEventArgs e) => Start();

    private void ContinueButton_Click(object sender, RoutedEventArgs e) => Continue();

    private void DefaultsButton_Click(object sender, RoutedEventArgs e) => KeepDefaults();

    private void Continue()
    {
        if (_outcome is null) return;
        Completed?.Invoke(_outcome.Thresholds, Report("Used the values above."), _raw);
        Close();
    }

    // the check round only showed the closed eye value doesn't work, the head angle ones are still good
    private const string ClosedEyeValue = "LIDS_SHUT_OPENNESS";

    private void KeepDefaults()
    {
        var keep = (_outcome?.Thresholds ?? new EyeThresholds()) with { LidsShutOpenness = null };
        // say in the report that it ended up at its default, and why
        var values = (_outcome?.Values ?? []).Select(v => v.Name == ClosedEyeValue && v.Tuned
            ? v with
            {
                Used = v.Default ?? v.Used,
                Tuned = false,
                Explanation = $"{v.Default}, the default: the check round with {v.Used:0.000} didn't pass. What was measured: {v.Explanation}",
            }
            : v).ToList();
        string decision = _outcome is null
            ? "The eye check stopped early, nothing was changed."
            : "Kept the default value for closed eyes, the head angle values are used.";
        Completed?.Invoke(keep, Report(decision, values), _raw);
        Close();
    }

    /// <summary>Answering by looking: right for yes, left for no. Buttons and keys keep working too.</summary>
    private void Ask(string yes, string no, Action<bool> answer)
    {
        _onChoice = answer;
        Choice.Ask(yes, no);
    }

    private void AskIntro() => Ask("Begin the eye check", "Skip it", yes => { if (yes) Start(); else Close(); });

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if (IntroPanel.Visibility == Visibility.Visible && e.Key is Key.Enter or Key.Space)
        {
            e.Handled = true;
            Start();
        }
    }

    // =====================================================================

    private void ShowPhase(FrameworkElement panel)
    {
        foreach (var p in new FrameworkElement[] { IntroPanel, RunPanel, ResultPanel })
            p.Visibility = p == panel ? Visibility.Visible : Visibility.Collapsed;
        Cursor = panel == RunPanel ? Cursors.None : Cursors.Arrow;
        // looking left and right is part of the steps, it mustn't answer anything then
        if (panel == RunPanel) Choice.Stop();
    }

    // the live line: green when it's going right, amber for what's wrong, nothing for null
    private void SetLive((bool Good, string Text)? status)
    {
        LiveText.Text = status?.Text ?? "";
        LiveText.Foreground = status is { Good: true } ? (_bright ? GoodOnBright : Green) : (_bright ? OffOnBright : Amber);
    }

    private void SetBright(bool bright)
    {
        _bright = bright;
        Background = bright ? Bright : Dark;
        InstructionText.Foreground = bright ? DarkText : Brushes.White;
        StepText.Foreground = bright ? DarkText : StepGrey;
        HintText.Foreground = bright ? DarkText : Amber;
    }

    private void SetProgress(double fraction) =>
        ProgressFill.Width = Math.Clamp(fraction, 0, 1) * ((FrameworkElement)ProgressFill.Parent).Width;

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
