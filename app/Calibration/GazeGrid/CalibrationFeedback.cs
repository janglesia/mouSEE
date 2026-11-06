namespace app.Calibration.GazeGrid;

// What to show the person when a calibration falls short, in plain words: what went wrong at which dots and what to do
// about it, worked out from the frames those dots got. Shown on screen by the grid window, nothing here is spoken.
public static class CalibrationFeedback
{
    private enum Cause { Camera, Face, EyesNotFound, Covered, Closed }

    private const int MaxPlaces = 4;

    /// <summary>One line per cause, the most common first. frames: what each target captured, by target index.</summary>
    public static string Explain(CalibrationFeatureValidation validation, IReadOnlyDictionary<int, IReadOnlyList<FrameSample>> frames)
    {
        var lines = validation.Missing
            .GroupBy(m => m.TargetIndex)
            .Select(g => g.First())
            .Select(m => (Cause: CauseOf(frames.GetValueOrDefault(m.TargetIndex) ?? []), Place: Place(m)))
            .GroupBy(t => t.Cause)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{Advice(g.Key)} ({Dots(g.Count())}: {Places(g.Select(t => t.Place))})");
        return string.Join("\n", lines);
    }

    /// <summary>Which head positions weren't reached, or null if they all were. This doesn't fail the calibration: someone
    /// who can't move their head that far still has to be able to finish, the fit leaves out what didn't move enough.</summary>
    public static string? HeadNote(IReadOnlyList<GazeTargetSummary> targets)
    {
        var rest = targets.Where(t => t.Pose is null && !t.MoveHead && t.FaceX is not null && t.Pitch is not null).ToList();
        if (rest.Count == 0) return null;
        double restX = Stats.Percentile(rest.Select(t => t.FaceX!.Median).ToList(), 50);
        double restPitch = Stats.Percentile(rest.Select(t => t.Pitch!.Median).ToList(), 50);

        var short_ = new List<string>();
        foreach (string pose in HeadPose.Step.Select(s => s.Pose).Distinct())
        {
            var dots = targets.Where(t => t.Pose == pose && t.FaceX is not null && t.Pitch is not null).ToList();
            if (dots.Count == 0) continue;
            double moved = Stats.Percentile(dots.Select(t => t.FaceX!.Median).ToList(), 50) - restX;
            double tilted = Stats.Percentile(dots.Select(t => t.Pitch!.Median).ToList(), 50) - restPitch;
            if (!HeadPose.Reached(pose, moved, tilted)) short_.Add(PoseName(pose));
        }
        return short_.Count == 0 ? null
            : $"Your head didn't get as far as asked when {string.Join(", ", short_)}. You can still use this calibration, but the bubble may be less accurate when you move that way.";
    }

    private static Cause CauseOf(IReadOnlyList<FrameSample> frames)
    {
        if (frames.Count < GazeFit.MinUsableFrames) return Cause.Camera;
        var counts = new Dictionary<Cause, int>
        {
            [Cause.Face] = frames.Count(f => !f.FaceFound),
            [Cause.EyesNotFound] = frames.Count(f => f.FaceFound && (f.Left is null || f.Right is null)),
            [Cause.Covered] = frames.Count(f => f.FaceFound && f.Left is not null && f.Right is not null && !GazeTargetSummary.Usable(f) && (f.Left.Covered || f.Right.Covered)),
            [Cause.Closed] = frames.Count(f => f.FaceFound && f.Left is { Covered: false } && f.Right is { Covered: false } && !GazeTargetSummary.Usable(f)),
        };
        return counts.MaxBy(c => c.Value).Key;
    }

    private static string Advice(Cause cause) => cause switch
    {
        Cause.Camera => "The camera sent too few frames. Check that it's working and that nothing else is using it.",
        Cause.Face => "Your face went out of view. Sit in the middle of the camera's picture, in good light.",
        Cause.EyesNotFound => "Your eyes couldn't be found. Add some light and face the camera.",
        Cause.Covered => "Something seemed to cover your eyes. Move hair or hands away, and avoid glare on glasses.",
        _ => "Your eyes looked closed. Keep both open while each ring fills, and blink between dots.",
    };

    private static string Dots(int count) => count == 1 ? "1 dot" : $"{count} dots";

    private static string Places(IEnumerable<string> places)
    {
        var distinct = places.Distinct().ToList();
        return distinct.Count <= MaxPlaces ? string.Join(", ", distinct) : string.Join(", ", distinct.Take(MaxPlaces)) + " and others";
    }

    private static string Place(MissingCalibrationFeature m)
    {
        if (m.Feature == CalibrationFeatureValidator.NotCaptured) return "not shown";
        string row = m.Y < 1 / 3.0 ? "top" : m.Y > 2 / 3.0 ? "bottom" : "middle";
        string column = m.X < 1 / 3.0 ? "left" : m.X > 2 / 3.0 ? "right" : "middle";
        string place = row == column ? "middle" : $"{row} {column}";
        return m.Pose is null ? place : $"{place} {PoseName(m.Pose)}";
    }

    private static string PoseName(string pose) => pose switch
    {
        HeadPose.LeanLeft => "leaning left",
        HeadPose.LeanRight => "leaning right",
        HeadPose.HeadUp => "tilting up",
        _ => "tilting down",
    };
}
