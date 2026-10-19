namespace app.Calibration.EyeCheck;

// What the eye check measured, saved with the calibration so you can see why each value is what it is
// (CalibrationProfile.EyeCheck), and its raw frames (in the recording, CalibrationRecording).

/// <summary>One eye over one recording.</summary>
public sealed record EyeStats(Spread? IrisRatio, Spread? Openness, Spread? Blink, Spread? Contrast, double? WidthPx,
                              double ClosedShare, double CoveredShare, Spread? OffsetH, Spread? OffsetV)
{
    // share of the frames with the face in which the tracker found this eye
    public double FoundShare { get; init; }

    public static EyeStats? Of(IReadOnlyList<FrameSample> faceFrames, Func<FrameSample, EyeSample?> eye)
    {
        var eyes = faceFrames.Select(eye).OfType<EyeSample>().ToList();
        if (eyes.Count == 0) return null;
        return new EyeStats(
            Spread.Of(eyes.Select(e => e.IrisRatio)),
            Spread.Of(eyes.Select(e => (double?)e.Openness)),
            Spread.Of(eyes.Select(e => e.Blink)),
            Spread.Of(eyes.Select(e => e.Contrast)),
            Spread.Of(eyes.Select(e => (double?)e.WidthPx))?.Median,
            Math.Round(eyes.Count(e => e.Closed) / (double)eyes.Count, 3),
            Math.Round(eyes.Count(e => e.Covered) / (double)eyes.Count, 3),
            Spread.Of(eyes.Select(e => (double?)e.OffsetH)),
            Spread.Of(eyes.Select(e => (double?)e.OffsetV)))
        {
            FoundShare = Math.Round(eyes.Count / (double)faceFrames.Count, 3),
        };
    }
}

/// <summary>What one eye check step measured, from the usable frames (after the reaction time) of its last try.</summary>
public sealed record StepReport
{
    public string Step { get; init; } = "";
    public string Instruction { get; init; } = "";
    public int Tries { get; init; }
    public bool Passed { get; init; }
    // why the last try failed, null if it passed
    public string? Problem { get; init; }
    public int Frames { get; init; }
    public double FaceShare { get; init; }
    public EyeStats? Left { get; init; }
    public EyeStats? Right { get; init; }
    public Spread? Pitch { get; init; }
    public Spread? Yaw { get; init; }
    public Spread? Roll { get; init; }
    public double? CaptureFps { get; init; }

    public static StepReport Of(EyeCheckStep step, int tries, StepCheck check, IReadOnlyList<FrameSample> usable)
    {
        var face = usable.Where(f => f.FaceFound).ToList();
        return new StepReport
        {
            Step = step.Kind.ToString(),
            Instruction = step.Say,
            Tries = tries,
            Passed = check.Ok,
            Problem = check.Ok ? null : check.Problem,
            Frames = usable.Count,
            FaceShare = usable.Count == 0 ? 0 : Math.Round(face.Count / (double)usable.Count, 3),
            Left = EyeStats.Of(face, f => f.Left),
            Right = EyeStats.Of(face, f => f.Right),
            Pitch = Spread.Of(face.Select(f => (double?)f.Pitch)),
            Yaw = Spread.Of(face.Select(f => (double?)f.Yaw)),
            Roll = Spread.Of(face.Select(f => (double?)f.Roll)),
            CaptureFps = Spread.Of(usable.Select(f => (double?)f.CaptureFps))?.Median,
        };
    }
}

/// <summary>
/// One value the eye check decides: what it's set to, the tracker's default, and the measurements behind it.
/// "Default" is the value built into the tracker (eye_extractor.py / tracking_status.py), picked by hand from
/// short recordings of one person, used when the eye check can't work out a better one.
/// </summary>
public sealed record EyeValueReport
{
    // the constant's name in the tracker, e.g. IRIS_HIDDEN_RATIO
    public string Name { get; init; } = "";
    // what it is, e.g. "Closed eye (iris check)"
    public string Label { get; init; } = "";
    public double Used { get; init; }
    public double? Default { get; init; }
    public bool Tuned { get; init; }
    // the numbers it was worked out from, e.g. {"openHighest": 0.5, "closedLowest": 0.56, "gap": 0.06, "gapNeeded": 0.1}
    public Dictionary<string, double> Measured { get; init; } = [];
    // what it means for this person, with the numbers
    public string Explanation { get; init; } = "";
}

/// <summary>Step 7: how often the tracker got it right with the new values.</summary>
public sealed record CheckRoundReport
{
    public bool Passed { get; init; }
    // share of frames right, per instruction ("left eye closed": 0.96)
    public Dictionary<string, double> Scores { get; init; } = [];
    public double Needed { get; init; } = EyeCheckRound.MinMatch;
}

/// <summary>Everything the eye check found, saved in the calibration (CalibrationProfile.EyeCheck).</summary>
public sealed record EyeCheckReport
{
    public DateTimeOffset MadeAt { get; init; }
    public List<EyeValueReport> Values { get; init; } = [];
    public List<StepReport> Steps { get; init; } = [];
    // null if it wasn't run (nothing changed that it could check, or the tracker didn't come back)
    public CheckRoundReport? Check { get; init; }
    public List<string> Warnings { get; init; } = [];
    // e.g. "Kept the default values for closed eyes, the check round failed."
    public string? Decision { get; init; }

    /// <summary>One line per value and warning, for showing.</summary>
    public IEnumerable<string> Lines() =>
        Values.Select(v => $"{v.Label}: {v.Explanation}").Concat(Warnings).Concat(Decision is null ? [] : [Decision]);
}

public sealed record StepRecording(string Step, int Try, bool Passed, string? Problem, List<FrameSample> Frames);

public sealed record TrialRecording(string Trial, List<FrameSample> Frames);
