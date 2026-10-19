namespace app.Calibration.GazeGrid;

// What the eyes did at each grid target, what the gaze numbers get fitted to (CalibrationProfile.GazeTargets),
// and the raw frames of each target (in the recording, CalibrationRecording.Gaze).

/// <summary>One target of the grid screen: what the eyes did while it was looked at. The input for fitting the gaze.</summary>
public sealed record GazeTargetSummary
{
    public int Index { get; init; }
    // where it was, 0-1 across the screen
    public double X { get; init; }
    public double Y { get; init; }
    public bool IsValidation { get; init; }
    // looked at while leaning left and right, see CalibrationTarget.MoveHead
    public bool MoveHead { get; init; }
    // the head step: how the head was held (HeadPose), null sitting normally
    public string? Pose { get; init; }
    public int Frames { get; init; }
    // frames with the face found and both eyes open and not covered, the ones a gaze fit should use
    public int UsableFrames { get; init; }
    public Spread? LeftH { get; init; }
    public Spread? LeftV { get; init; }
    public Spread? RightH { get; init; }
    public Spread? RightV { get; init; }
    public Spread? Pitch { get; init; }
    public Spread? Yaw { get; init; }
    // where the face was in the camera image (middle, 0-1) and how wide (0-1), see FrameSample
    public Spread? FaceX { get; init; }
    public Spread? FaceY { get; init; }
    public double? FaceW { get; init; }
    // lid openness, both eyes averaged per frame: the lids follow the eyes up and down, so it's what up/down is fitted from
    public Spread? Openness { get; init; }
    // vertical iris offset, both eyes averaged per frame
    public Spread? OffsetV { get; init; }

    // both eyes open and not covered. looking down counts as open, also in recordings made before the tracker knew that
    public static bool Usable(FrameSample f) =>
        f.FaceFound && f.Left is { Covered: false } l && f.Right is { Covered: false } r
        && ((!l.Closed && !r.Closed) || LooksDown(l, r));

    // the tracker's rule for "both lids down by the same amount, not shut, low blink score = looking down, not closed"
    // (SAME_LIDS_RATIO in eye_extractor.py, EyeRulesTests checks the numbers match)
    public const double SameLidsRatio = 0.82, LookingDownMinOpenness = 0.15, LookingDownMaxBlink = 0.45;

    public static bool LooksDown(EyeSample l, EyeSample r)
    {
        double low = Math.Min(l.Openness, r.Openness), high = Math.Max(l.Openness, r.Openness);
        double blink = Math.Max(l.Blink ?? 0, r.Blink ?? 0);
        return high > 0 && low / high >= SameLidsRatio && low >= LookingDownMinOpenness && blink < LookingDownMaxBlink;
    }

    public static GazeTargetSummary Of(CalibrationTarget target, IReadOnlyList<FrameSample> frames)
    {
        var usable = frames.Where(Usable).ToList();
        return new GazeTargetSummary
        {
            Index = target.Index,
            X = target.NormalizedX,
            Y = target.NormalizedY,
            IsValidation = target.IsValidation,
            MoveHead = target.MoveHead,
            Pose = target.Pose,
            Frames = frames.Count,
            UsableFrames = usable.Count,
            LeftH = Spread.Of(usable.Select(f => (double?)f.Left!.OffsetH)),
            LeftV = Spread.Of(usable.Select(f => (double?)f.Left!.OffsetV)),
            RightH = Spread.Of(usable.Select(f => (double?)f.Right!.OffsetH)),
            RightV = Spread.Of(usable.Select(f => (double?)f.Right!.OffsetV)),
            Pitch = Spread.Of(usable.Select(f => (double?)f.Pitch)),
            Yaw = Spread.Of(usable.Select(f => (double?)f.Yaw)),
            FaceX = Spread.Of(usable.Select(f => (double?)f.FaceX)),
            FaceY = Spread.Of(usable.Select(f => (double?)f.FaceY)),
            FaceW = Spread.Of(usable.Select(f => (double?)f.FaceW))?.Median,
            Openness = Spread.Of(usable.Select(f => (double?)(f.Left!.Openness + f.Right!.Openness) / 2)),
            OffsetV = Spread.Of(usable.Select(f => (double?)(f.Left!.OffsetV + f.Right!.OffsetV) / 2)),
        };
    }
}

// ---- the raw frames, in their own file (app/Calibration/Saved/recordings/<id>.json) so the calibration file stays readable.
// Every frame of every try, reaction time included, so the analysis can be run again on them later

public sealed record TargetRecording(int Index, double X, double Y, bool IsValidation, List<FrameSample> Frames, bool MoveHead = false,
                                     string? Pose = null);
