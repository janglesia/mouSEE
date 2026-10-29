namespace app.Calibration.EyeCheck;

// The eye check from docs/calibration/EYE-CALIBRATION.md: runs after the grid, the person closes both eyes (sitting
// normally, then with the head tilted up and down) while the tracker's numbers are recorded, and EyeCheckAnalysis picks
// the value that separates those from the open eyes the grid recorded. This file has the steps and how each one is
// checked. No UI here, EyeCheckWindow runs them.
//
// It used to start with sitting still and following a dot to the corners (for the open eyes and the head angle, the
// grid measures both better now), and had a wink step for each eye. Nothing uses winks, closing both eyes is what
// answers questions (ClosedEyesGate) and what a blink click will use.

public enum EyeCheckStepKind
{
    CloseBoth,          // 1: close both eyes
    CloseBothHeadUp,    // 2: the same with the head tilted up
    CloseBothHeadDown,  // 3: and tilted down
    // no covering an eye with a hand: mouSEE is for people who can't use their hands
}

/// <summary>A step. Say: the instruction. Close: for the tilt steps, said once the head is tilted far enough (closing
/// the eyes before that would hide the live guidance). After: said when it's done.</summary>
public sealed record EyeCheckStep(EyeCheckStepKind Kind, string Say, string? Close, string? After, int DurationMs)
{
    // the tilt steps say to look at the middle of the screen while tilting, they show the middle dot
    public bool LooksAtMiddle => IsTilt;

    public bool IsTilt => Kind is EyeCheckStepKind.CloseBothHeadUp or EyeCheckStepKind.CloseBothHeadDown;

    public bool TiltsUp => Kind == EyeCheckStepKind.CloseBothHeadUp;
}

/// <summary>What a step's recording showed: did the person do what was asked, and if not, what to tell them.</summary>
public sealed record StepCheck(bool Ok, string? Problem)
{
    public static readonly StepCheck Pass = new(true, null);
    public static StepCheck Fail(string problem) => new(false, problem);
}

/// <summary>The resting head angle, from the grid, the tilt checks compare against it.</summary>
public sealed record EyeCheckBaseline(double RestPitch, double RestYaw);

public static class EyeCheckSteps
{
    // people took about 1.1 s to react to an instruction in the recordings, so this much of every step is thrown away
    public const int ReactionMs = 1200;

    // a step needs the face in at least this share of its frames
    public const double MinFaceShare = 0.8;
    // and both eyes measured in at least this share
    public const double MinEyeShare = 0.7;
    // a step with fewer frames than this (after the reaction time) can't be judged, the tracker is too slow
    public const int MinFrames = 20;

    // both eyes closed: MediaPipe's blink score for both (0.67-0.76 in the recordings), or lids nearly shut (0.04). Either
    // is enough, it's whether the person did it that's checked here, not whether the tracker's own decision got it right
    public const double BothClosedBlink = 0.5;
    public const double BothClosedOpenness = 0.12;
    // the head has to tilt at least this far from resting in the tilt steps
    public const double MinTiltDeg = 15;
    // and not much further: past MAX_PITCH (30 by default) from resting the head counts as turned away and tracking stops
    public const double MaxTiltDeg = 28;

    // MediaPipe pitch gets more negative when the head tilts up (-33 tilted up vs -10 resting in the recordings)
    private const double UpSign = -1;

    private const string CloseNow = "Now close both eyes until you hear the sound.";
    private const string HeadBack = "Open your eyes and hold your head normally again.";

    public static IReadOnlyList<EyeCheckStep> All { get; } =
    [
        new(EyeCheckStepKind.CloseBoth, "Close both eyes until you hear the sound.", null, "Open your eyes.", 4000),
        new(EyeCheckStepKind.CloseBothHeadUp, "Tilt your head up and look at the middle of the screen.", CloseNow, HeadBack, 4000),
        new(EyeCheckStepKind.CloseBothHeadDown, "Tilt your head down and look at the middle of the screen.", CloseNow, HeadBack, 4000),
    ];

    /// <summary>Frames that count: after the reaction time.</summary>
    public static List<FrameSample> Usable(IEnumerable<FrameSample> frames) => frames.Where(f => f.Ms >= ReactionMs).ToList();

    /// <summary>The resting head angle: the median over these frames (the grid's, sitting normally), null without enough.</summary>
    public static EyeCheckBaseline? Baseline(IReadOnlyList<FrameSample> frames)
    {
        var face = frames.Where(f => f.FaceFound).ToList();
        if (face.Count < MinFrames) return null;
        return new EyeCheckBaseline(Median(face, f => f.Pitch)!.Value, Median(face, f => f.Yaw)!.Value);
    }

    /// <summary>Did the person do what the step asked? frames: the usable ones (see Usable).</summary>
    public static StepCheck Check(EyeCheckStep step, IReadOnlyList<FrameSample> frames, EyeCheckBaseline? baseline,
                                  int minFrames = MinFrames)
    {
        if (frames.Count < minFrames)
            return StepCheck.Fail("The camera sent too few frames. Check the lighting, then we'll try again.");
        if (Share(frames, f => f.FaceFound) < MinFaceShare)
            return StepCheck.Fail("I couldn't see your face for most of that. Stay in front of the camera.");

        var face = frames.Where(f => f.FaceFound).ToList();
        if (Share(face, f => f.Left is not null && f.Right is not null) < MinEyeShare)
            return StepCheck.Fail("I lost track of your eyes. Keep your face towards the screen.");
        if (!Closed(face, Side.Left) || !Closed(face, Side.Right))
            return StepCheck.Fail("Keep both eyes closed until you hear the sound.");

        if (step.IsTilt)
        {
            double tilt = TiltFromRest(face, baseline) * (step.TiltsUp ? UpSign : -UpSign);
            if (tilt < MinTiltDeg)
                return StepCheck.Fail(step.TiltsUp ? "Tilt your head further up, like looking at the ceiling."
                                                   : "Tilt your head further down, like looking at your keyboard.");
        }
        return StepCheck.Pass;
    }

    // closed by MediaPipe's blink score or by the lids, see BothClosedBlink
    private static bool Closed(IReadOnlyList<FrameSample> face, Side side) =>
        Median(face, f => f.EyeOn(side)?.Blink) > BothClosedBlink || Median(face, f => f.EyeOn(side)?.Openness) < BothClosedOpenness;

    /// <summary>
    /// What to show live under the instruction while a step records, from the last moment's frames: Good is true for
    /// "doing it right" (shown green), false for what's wrong (amber). problemsToo: only say what's wrong once the person
    /// has had time to react, "Good" can show straight away. null = nothing to say. The tilt steps' head angle comes
    /// first, it's live from the start.
    /// </summary>
    public static (bool Good, string Text)? LiveStatus(EyeCheckStep step, IReadOnlyList<FrameSample> recent, EyeCheckBaseline? baseline,
                                                     bool problemsToo)
    {
        if (recent.Count >= 5 && Share(recent, f => f.FaceFound) < 0.5)
            return problemsToo ? (false, "I can't see your face. Move back in front of the camera.") : null;
        var tilt = TiltGuide(step, recent, baseline);
        if (tilt is { InRange: false }) return (false, tilt.Value.Hint);
        (bool, string)? tiltGood = tilt is { } t ? (true, t.Hint) : null;
        if (recent.Count < 5) return tiltGood;

        var check = Check(step, recent, baseline, minFrames: 5);
        if (check.Ok) return tiltGood ?? (true, "Good, keep them closed");
        return problemsToo ? (false, check.Problem!) : tiltGood;
    }

    /// <summary>
    /// Live guidance for the tilt steps, shown while getting into position and while recording: how far the head is
    /// tilted from resting against the 15 it needs, from the last moment's frames. null for the other steps, or without
    /// a resting angle (the grid had no face) or a face.
    /// </summary>
    public static (bool InRange, string Hint)? TiltGuide(EyeCheckStep step, IReadOnlyList<FrameSample> recent, EyeCheckBaseline? baseline)
    {
        if (!step.IsTilt || baseline is null) return null;
        var face = recent.Where(f => f.FaceFound).ToList();
        if (face.Count < 3) return null;
        double tilt = TiltFromRest(face, baseline) * (step.TiltsUp ? UpSign : -UpSign);
        string way = step.TiltsUp ? "up" : "down";
        if (tilt < MinTiltDeg)
            return (false, $"Tilt your head {way} a little more ({Math.Max(0, tilt):0}° of {MinTiltDeg:0}°)");
        if (tilt > MaxTiltDeg)
            return (false, $"A little less, tilt your head back a bit ({tilt:0}°)");
        return (true, $"Good, hold it there ({tilt:0}°)");
    }

    // median head pitch compared with resting
    private static double TiltFromRest(IReadOnlyList<FrameSample> face, EyeCheckBaseline? baseline) =>
        (Median(face, f => f.Pitch) ?? 0) - (baseline?.RestPitch ?? 0);

    internal static double Share(IReadOnlyList<FrameSample> frames, Func<FrameSample, bool> test) =>
        frames.Count == 0 ? 0 : frames.Count(test) / (double)frames.Count;

    internal static double? Median(IEnumerable<FrameSample> frames, Func<FrameSample, double?> value)
    {
        var values = frames.Select(value).Where(v => v is not null).Select(v => v!.Value).ToList();
        return values.Count == 0 ? null : Stats.Percentile(values, 50);
    }
}
