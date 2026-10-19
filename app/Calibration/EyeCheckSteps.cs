namespace app.Calibration;

// The eye check from docs/EYE-CALIBRATION.md: the person does specific things (close one eye, close both,
// tilt their head...) while the tracker's numbers are recorded, then EyeCheckAnalysis picks values that
// separate the cases. This file has the steps and how each one is checked. No UI here, EyeCheckWindow runs them.

// what one eye measured in one frame. OffsetH / OffsetV: where the iris is in the eye, -1 to 1 (eye_extractor.py),
// what the gaze is worked out from
public sealed record EyeSample(double? IrisRatio, double? Contrast, double? Blink, double Openness, double WidthPx,
                               bool Closed, bool Covered, double OffsetH = 0, double OffsetV = 0);

// one tracker frame, Ms since the step's recording started. FaceX / FaceY: the middle of the face in the camera image,
// FaceW its width, all 0-1 of the image (mirrored like the tracker, so leaning to your right makes FaceX bigger).
// Where the head is changes where the same eye angle lands on the screen. EstimateX / EstimateY: where the tracker put the bubble
public sealed record FrameSample(long Ms, bool FaceFound, double Yaw, double Pitch, EyeSample? Left, EyeSample? Right,
                                 double CaptureFps, double Roll = 0, double FaceX = 0, double FaceY = 0, double FaceW = 0,
                                 double? EstimateX = null, double? EstimateY = null)
{
    public static FrameSample From(TrackerMessage msg, long ms)
    {
        var face = msg.Faces is { Count: > 0 } faces ? faces[0] : null;
        var box = face?.Box is { Length: 4 } b ? b : null;
        return new FrameSample(ms, face is not null, face?.Pose.Yaw ?? 0, face?.Pose.Pitch ?? 0,
            Eye(face?.Eyes?.Left), Eye(face?.Eyes?.Right), msg.CaptureFps, face?.Pose.Roll ?? 0,
            box is null ? 0 : box[0] + box[2] / 2, box is null ? 0 : box[1] + box[3] / 2, box?[2] ?? 0,
            Estimate(face)?.X, Estimate(face)?.Y);
    }

    // where the tracker put the bubble (0-1 of the screen), null when it had no estimate
    private static GazeEstimate? Estimate(Face? face) => face?.Eyes?.Estimate is { Valid: true } e ? e : null;

    private static EyeSample? Eye(EyeData? e) =>
        e is null ? null : new EyeSample(e.IrisRatio, e.Contrast, e.Blink, e.Openness, e.WidthPx, e.Closed, e.Covered,
                                         e.Offset?.H ?? 0, e.Offset?.V ?? 0);

    public EyeSample? EyeOn(Side side) => side == Side.Left ? Left : Right;
}

// the person's own left and right, same as the tracker
public enum Side { Left, Right }

public enum EyeCheckStepKind
{
    Rest,               // 0: sit normally, look at the middle
    LookAround,         // 1: both eyes open, follow a dot to the corners
    CloseLeft,          // 2
    CloseRight,         // 3
    CloseLeftHeadUp,    // 4
    CloseRightHeadDown, // 5
    CloseBoth,          // 6
    // no covering an eye with a hand: mouSEE is for people who can't use their hands
}

public sealed record EyeCheckStep(EyeCheckStepKind Kind, string Say, string? After, int DurationMs)
{
    // which eye(s) are closed in this step, for the analysis
    public Side[] ClosedEyes => Kind switch
    {
        EyeCheckStepKind.CloseLeft or EyeCheckStepKind.CloseLeftHeadUp => [Side.Left],
        EyeCheckStepKind.CloseRight or EyeCheckStepKind.CloseRightHeadDown => [Side.Right],
        EyeCheckStepKind.CloseBoth => [Side.Left, Side.Right],
        _ => [],
    };
}

/// <summary>What a step's recording showed: did the person do what was asked, and if not, what to tell them.</summary>
public sealed record StepCheck(bool Ok, string? Problem)
{
    public static readonly StepCheck Pass = new(true, null);
    public static StepCheck Fail(string problem) => new(false, problem);
}

/// <summary>The resting head angle from the first step, the tilt checks compare against it.</summary>
public sealed record EyeCheckBaseline(double RestPitch, double RestYaw);

public static class EyeCheckSteps
{
    // people took about 1.1 s to react to an instruction in the recordings, so this much of every step is thrown away
    public const int ReactionMs = 1200;

    // a step needs the face in at least this share of its frames
    public const double MinFaceShare = 0.8;
    // and the eye(s) it's about measured in at least this share
    public const double MinEyeShare = 0.7;
    // a step with fewer frames than this (after the reaction time) can't be judged, the tracker is too slow
    public const int MinFrames = 20;

    // closed eye vs open eye. blink scores for a wink are only ~0.2-0.35 against ~0.05 for the open eye,
    // and with the head tilted they're the same, so the iris ratio counts too (closed ~0.9 vs open ~0.3)
    public const double ClosedBlinkDiff = 0.05;
    public const double ClosedIrisDiff = 0.15;
    // both eyes closed: MediaPipe's blink score for both (0.67-0.76 in the recordings)
    public const double BothClosedBlink = 0.5;
    // the head has to tilt at least this far from resting in steps 4 and 5
    public const double MinTiltDeg = 15;

    // MediaPipe pitch gets more negative when the head tilts up (-33 tilted up vs -10 resting in the recordings)
    private const double UpSign = -1;

    public static IReadOnlyList<EyeCheckStep> All { get; } =
    [
        new(EyeCheckStepKind.Rest, "Sit how you normally would and look at the middle of the screen.", null, 4000),
        new(EyeCheckStepKind.LookAround, "Keep both eyes open and follow the dot with your eyes.", null, 8000),
        new(EyeCheckStepKind.CloseLeft, "Close your left eye. Keep the right one open.", "Open your eyes.", 5000),
        new(EyeCheckStepKind.CloseRight, "Close your right eye. Keep the left one open.", "Open your eyes.", 5000),
        new(EyeCheckStepKind.CloseLeftHeadUp, "Close your left eye and tilt your head up. Keep looking at the middle of the screen.", "Open your eyes and hold your head normally again.", 5000),
        new(EyeCheckStepKind.CloseRightHeadDown, "Close your right eye and tilt your head down. Keep looking at the middle of the screen.", "Open your eyes and hold your head normally again.", 5000),
        new(EyeCheckStepKind.CloseBoth, "Close both eyes until you hear the sound.", "Open your eyes.", 4000),
    ];

    /// <summary>Frames that count: after the reaction time.</summary>
    public static List<FrameSample> Usable(IEnumerable<FrameSample> frames) => frames.Where(f => f.Ms >= ReactionMs).ToList();

    /// <summary>Did the person do what the step asked? frames: the usable ones (see Usable).</summary>
    public static StepCheck Check(EyeCheckStep step, IReadOnlyList<FrameSample> frames, EyeCheckBaseline? baseline,
                                  int minFrames = MinFrames)
    {
        if (frames.Count < minFrames)
            return StepCheck.Fail("The camera sent too few frames. Check the lighting, then we'll try again.");
        if (Share(frames, f => f.FaceFound) < MinFaceShare)
            return StepCheck.Fail("I couldn't see your face for most of that. Stay in front of the camera.");

        var face = frames.Where(f => f.FaceFound).ToList();
        switch (step.Kind)
        {
            case EyeCheckStepKind.Rest:
                return Share(face, f => f.Left is not null && f.Right is not null) < MinEyeShare
                    ? StepCheck.Fail("I couldn't find both of your eyes. Check the lighting and look at the screen.")
                    : StepCheck.Pass;

            case EyeCheckStepKind.LookAround:
                if (Share(face, f => f.Left?.IrisRatio is not null && f.Right?.IrisRatio is not null) < MinEyeShare)
                    return StepCheck.Fail("I couldn't see both of your eyes clearly. Keep them open and look at the screen.");
                if (Median(face, f => f.Left?.Blink) > BothClosedBlink || Median(face, f => f.Right?.Blink) > BothClosedBlink)
                    return StepCheck.Fail("Keep both eyes open while you follow the dot.");
                return StepCheck.Pass;

            case EyeCheckStepKind.CloseLeft or EyeCheckStepKind.CloseRight:
                return CheckOneClosed(face, step.ClosedEyes[0]);

            case EyeCheckStepKind.CloseLeftHeadUp or EyeCheckStepKind.CloseRightHeadDown:
            {
                var closed = CheckOneClosed(face, step.ClosedEyes[0]);
                if (!closed.Ok) return closed;
                bool up = step.Kind == EyeCheckStepKind.CloseLeftHeadUp;
                double tilt = TiltFromRest(face, baseline) * (up ? UpSign : -UpSign);
                return tilt < MinTiltDeg
                    ? StepCheck.Fail(up ? "Tilt your head further up, like looking at the ceiling."
                                        : "Tilt your head further down, like looking at your keyboard.")
                    : StepCheck.Pass;
            }

            case EyeCheckStepKind.CloseBoth:
                return Median(face, f => f.Left?.Blink) > BothClosedBlink && Median(face, f => f.Right?.Blink) > BothClosedBlink
                    ? StepCheck.Pass
                    : StepCheck.Fail("Keep both eyes closed until you hear the sound.");
        }
        return StepCheck.Pass;
    }

    /// <summary>
    /// A hint while the step is running, from the last second or so of frames, or null if it looks right.
    /// Only the most important problem, so the person isn't told three things at once.
    /// </summary>
    public static string? LiveHint(EyeCheckStep step, IReadOnlyList<FrameSample> recent, EyeCheckBaseline? baseline)
    {
        if (recent.Count < 5) return null;
        if (Share(recent, f => f.FaceFound) < 0.5) return "I can't see your face. Move back in front of the camera.";

        // same rules as the check at the end, just on the last second
        var result = Check(step, recent, baseline, minFrames: 5);
        return result.Ok ? null : result.Problem;
    }

    // the closed eye has to look more closed than the open one, by blink score or iris ratio
    private static StepCheck CheckOneClosed(IReadOnlyList<FrameSample> face, Side closedSide)
    {
        var openSide = closedSide == Side.Left ? Side.Right : Side.Left;
        string closedName = closedSide == Side.Left ? "left" : "right";
        string openName = closedSide == Side.Left ? "right" : "left";

        if (Share(face, f => f.EyeOn(closedSide) is not null && f.EyeOn(openSide) is not null) < MinEyeShare)
            return StepCheck.Fail($"I lost track of your eyes. Keep your face towards the screen and only close your {closedName} eye.");

        double? blinkDiff = Median(face, f => f.EyeOn(closedSide)?.Blink) - Median(face, f => f.EyeOn(openSide)?.Blink);
        double? irisDiff = Median(face, f => f.EyeOn(closedSide)?.IrisRatio) - Median(face, f => f.EyeOn(openSide)?.IrisRatio);
        bool closedEnough = blinkDiff >= ClosedBlinkDiff || irisDiff >= ClosedIrisDiff;

        // both eyes closed looks like a big blink score on both
        bool bothClosed = Median(face, f => f.EyeOn(openSide)?.Blink) > BothClosedBlink;
        if (bothClosed) return StepCheck.Fail($"Keep your {openName} eye open, only close the {closedName} one.");
        return closedEnough ? StepCheck.Pass : StepCheck.Fail($"Close your {closedName} eye all the way.");
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

public static class Stats
{
    /// <summary>Percentile with linear interpolation between the closest values (like numpy's default). p is 0-100.</summary>
    public static double Percentile(IReadOnlyCollection<double> values, double p)
    {
        if (values.Count == 0) throw new ArgumentException("no values", nameof(values));
        var sorted = values.OrderBy(v => v).ToArray();
        double rank = Math.Clamp(p, 0, 100) / 100 * (sorted.Length - 1);
        int low = (int)Math.Floor(rank);
        int high = Math.Min(low + 1, sorted.Length - 1);
        return sorted[low] + (sorted[high] - sorted[low]) * (rank - low);
    }
}
