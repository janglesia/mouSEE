using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using app.Calibration;

namespace app.UI;

// Runs the eye check from docs/EYE-CALIBRATION.md. The steps, their checks and the math are in
// app/Calibration (EyeCheckSteps, EyeCheckAnalysis, EyeCheckRound), this only runs them: says each
// instruction, records the tracker's frames, gives hints, repeats steps that didn't work, and at the
// end checks the new values with the tracker actually using them.
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
    // the nine-point screen's yellow. a bright screen reflects on glasses right over the iris, so the open eyes
    // (step 1) and the check round are measured on it, like the nine-point screen and a white web page
    private static readonly Brush Bright = Frozen(Color.FromRgb(0xF7, 0xD0, 0x60));
    private static readonly Brush Dark = Frozen(Color.FromRgb(0x10, 0x10, 0x10));
    private static readonly Brush DarkText = Frozen(Color.FromRgb(0x1C, 0x1C, 0x1C));
    private static readonly Brush DotOnBright = Frozen(Color.FromRgb(0x29, 0x4A, 0x42));
    private static readonly Brush StepGrey = Frozen(Color.FromRgb(0x8A, 0x8A, 0x8A));

    private readonly App _app = (App)Application.Current;
    private readonly Speaker _speaker = new();
    private readonly Stopwatch _clock = new();
    private CancellationTokenSource? _cts;
    private bool _running;

    private List<FrameSample>? _recording;         // frames go here while a step records
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

    public EyeCheckWindow()
    {
        InitializeComponent();

        // the whole primary screen, so the dot's corners are the screen's corners
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
            _ = _speaker.SayAsync("Eye check. To begin, look to the right and hold it for three seconds. To cancel, look to the left.",
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
        EyeCheckBaseline? baseline = null;

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
            if (check.Ok)
            {
                recordings[step.Kind] = usable;
                baseline = UpdateBaseline(baseline, step, usable);
            }
            else if (step.Kind is EyeCheckStepKind.Rest or EyeCheckStepKind.LookAround)
            {
                // everything after compares against these two, no point going on
                ShowStopped(check.Problem!);
                return;
            }
            else
            {
                _problems.Add($"Left out \"{step.Say}\" after {AttemptsPerStep} tries: {check.Problem}");
            }

            await Task.Delay(PauseBetweenStepsMs, ct);
        }

        _outcome = EyeCheckAnalysis.Analyze(recordings);

        // step 7: only worth it if a closed eye value changed
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
        SetProgress(0);
        bool dot = step.Kind == EyeCheckStepKind.LookAround;
        SetBright(dot);
        if (dot)
        {
            MoveDot(0);
            DotLayer.Visibility = Visibility.Visible;
        }
        await _speaker.SayAsync(step.Say, ct);

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
                if (dot) MoveDot(now / (double)step.DurationMs);

                if (now >= HintAfterMs)
                {
                    var recent = frames.Where(f => f.Ms >= now - HintWindowMs).ToList();
                    string? hint = EyeCheckSteps.LiveHint(step, recent, baseline);
                    HintText.Text = hint ?? "";
                    // say one hint per try, more than that just talks over the person
                    if (hint is not null && !hintSpoken)
                    {
                        _speaker.SayNow(hint);
                        hintSpoken = true;
                    }
                }
                await Task.Delay(50, ct);
            }
        }
        finally
        {
            _recording = null;
            DotLayer.Visibility = Visibility.Collapsed;
            SetBright(false);
        }

        SetProgress(1);
        Speaker.Ding();
        return frames;
    }

    private static EyeCheckBaseline? UpdateBaseline(EyeCheckBaseline? baseline, EyeCheckStep step, IReadOnlyList<FrameSample> usable)
    {
        var face = usable.Where(f => f.FaceFound).ToList();
        return step.Kind switch
        {
            EyeCheckStepKind.Rest => new EyeCheckBaseline(
                EyeCheckSteps.Median(face, f => f.Pitch) ?? 0, EyeCheckSteps.Median(face, f => f.Yaw) ?? 0),
            _ => baseline,
        };
    }

    // the dot visits the corners and waits at each one, so the eyes (and head) get all the way there
    private static readonly (double T, double X, double Y)[] DotPath =
    [
        (0.00, 0.5, 0.5),
        (0.12, 0.06, 0.08), (0.27, 0.06, 0.08),
        (0.37, 0.94, 0.08), (0.52, 0.94, 0.08),
        (0.62, 0.94, 0.92), (0.77, 0.94, 0.92),
        (0.87, 0.06, 0.92), (1.00, 0.06, 0.92),
    ];

    private void MoveDot(double t)
    {
        int i = 1;
        while (i < DotPath.Length - 1 && DotPath[i].T < t) i++;
        var (t0, x0, y0) = DotPath[i - 1];
        var (t1, x1, y1) = DotPath[i];
        double k = t1 > t0 ? Math.Clamp((t - t0) / (t1 - t0), 0, 1) : 1;
        Canvas.SetLeft(Dot, (x0 + (x1 - x0) * k) * Root.ActualWidth - Dot.Width / 2);
        Canvas.SetTop(Dot, (y0 + (y1 - y0) * k) * Root.ActualHeight - Dot.Height / 2);
    }

    // =====================================================================
    // Step 7: check the new values with the tracker using them
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
            HintText.Text = $"Check {i + 1} of {order.Count}";
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
                    await Task.Delay(50, ct);
                }
            }
            finally
            {
                _recording = null;
            }
            Speaker.Ding();
            runs.Add((trial, EyeCheckSteps.Usable(frames)));
            _raw.CheckRound.Add(new TrialRecording(trial.ToString(), frames));
        }

        SetBright(false);
        InstructionText.Text = "Open your eyes.";
        HintText.Text = "";
        await _speaker.SayAsync(InstructionText.Text, ct);
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
            ResultTitle.Text = "The new values didn't work well enough";
            ResultSubtitle.Text = "mouSEE couldn't reliably tell when your eyes are closed with them. " +
                                  "Try again with more light on your face, or keep the default values.";
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
                ? "Your values work. Continue to save them and go on to the gaze calibration."
                : "Continue to save these values and go on to the gaze calibration.";
        }

        NotesList.ItemsSource = lines;
        ContinueButton.Visibility = passed ? Visibility.Visible : Visibility.Collapsed;
        DefaultsButton.Visibility = passed ? Visibility.Collapsed : Visibility.Visible;
        ShowPhase(ResultPanel);
        if (passed)
            Ask("Continue: save these values and go on to the gaze calibration", "Do the eye check again",
                yes => { if (yes) Continue(); else Start(); });
        else
            Ask("Keep the default values for closed eyes and continue", "Do the eye check again",
                yes => { if (yes) KeepDefaults(); else Start(); });
        _ = _speaker.SayAsync($"{ResultTitle.Text}. Look to the right to continue, or to the left to do it again.", CancellationToken.None);
    }

    private void ShowStopped(string problem)
    {
        _outcome = null;
        ResultTitle.Text = "The eye check couldn't go on";
        ResultSubtitle.Text = problem + " The first two steps are needed for everything after them.";
        NotesList.ItemsSource = _problems.Prepend("Sit in front of the camera with your face well lit, then do it again.").ToList();
        ContinueButton.Visibility = Visibility.Collapsed;
        DefaultsButton.Visibility = Visibility.Visible;
        ShowPhase(ResultPanel);
        Ask("Continue with the default values", "Do the eye check again", yes => { if (yes) KeepDefaults(); else Start(); });
        _ = _speaker.SayAsync($"{ResultTitle.Text}. Look to the right to continue with the default values, or to the left to do it again.",
                              CancellationToken.None);
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

    // the check round only showed the closed eye values don't work, the head angle ones are still good
    private static readonly string[] ClosedEyeValues = ["IRIS_HIDDEN_RATIO", "LIDS_OPEN_OPENNESS"];

    private void KeepDefaults()
    {
        var keep = (_outcome?.Thresholds ?? new EyeThresholds()) with { IrisHiddenRatio = null, LidsOpenOpenness = null };
        // say in the report that these ended up at their defaults, and why
        var values = (_outcome?.Values ?? []).Select(v => ClosedEyeValues.Contains(v.Name) && v.Tuned
            ? v with
            {
                Used = v.Default ?? v.Used,
                Tuned = false,
                Explanation = $"{v.Default}, the default: the check round with {v.Used:0.000} didn't pass. What was measured: {v.Explanation}",
            }
            : v).ToList();
        string decision = _outcome is null
            ? "The eye check stopped early, nothing was changed."
            : "Kept the default values for closed eyes, the head angle values are used.";
        Completed?.Invoke(keep, Report(decision, values), _raw);
        Close();
    }

    /// <summary>Answering by looking: right for yes, left for no. Buttons and keys keep working too.</summary>
    private void Ask(string yes, string no, Action<bool> answer)
    {
        _onChoice = answer;
        Choice.Ask(yes, no);
    }

    private void AskIntro() => Ask("Begin the eye check", "Cancel", yes => { if (yes) Start(); else Close(); });

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

    private void SetBright(bool bright)
    {
        Background = bright ? Bright : Dark;
        InstructionText.Foreground = bright ? DarkText : Brushes.White;
        StepText.Foreground = bright ? DarkText : StepGrey;
        HintText.Foreground = bright ? DarkText : Amber;
        Dot.Fill = DotOnBright;
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
