namespace app.Calibration;

public enum CheckTrial { CloseLeft, CloseRight, OpenBoth }

/// <summary>
/// Step 7, the last one of the eye check: a random mix of instructions, run with the new values loaded in the tracker,
/// counting how often its closed decision matches what was asked. Under 90% for any of them
/// means the values don't work for this person, see docs/EYE-CALIBRATION.md
/// </summary>
public static class EyeCheckRound
{
    public const int TrialMs = 3000;
    public const double MinMatch = 0.9;
    // every instruction this many times, in a random order
    public const int Repeats = 2;

    public static string Say(CheckTrial trial) => trial switch
    {
        CheckTrial.CloseLeft => "Close your left eye.",
        CheckTrial.CloseRight => "Close your right eye.",
        _ => "Open both eyes.",
    };

    public static string Name(CheckTrial trial) => trial switch
    {
        CheckTrial.CloseLeft => "left eye closed",
        CheckTrial.CloseRight => "right eye closed",
        _ => "both eyes open",
    };

    /// <summary>Each trial Repeats times, shuffled, never the same one twice in a row (that would look like nothing to do).</summary>
    public static List<CheckTrial> Order(Random random)
    {
        var all = Enum.GetValues<CheckTrial>();
        for (int attempt = 0; ; attempt++)
        {
            var order = Enumerable.Repeat(all, Repeats).SelectMany(t => t).OrderBy(_ => random.Next()).ToList();
            bool repeats = order.Zip(order.Skip(1)).Any(p => p.First == p.Second);
            if (!repeats || attempt > 50) return order;
        }
    }

    /// <summary>Does this frame show what was asked? Uses the tracker's own decisions, that's what's being checked.
    /// An eye the tracker thinks is covered is wrong for all three, nothing was in front of it.</summary>
    public static bool Matches(CheckTrial trial, FrameSample f)
    {
        if (!f.FaceFound) return false;
        var l = f.Left;
        var r = f.Right;
        return trial switch
        {
            CheckTrial.CloseLeft => l is { Closed: true, Covered: false } && r is { Closed: false, Covered: false },
            CheckTrial.CloseRight => r is { Closed: true, Covered: false } && l is { Closed: false, Covered: false },
            _ => l is { Closed: false, Covered: false } && r is { Closed: false, Covered: false },
        };
    }

    /// <summary>Share of matching frames per trial, from the usable frames (after the reaction time) of every run of it.</summary>
    public static Dictionary<CheckTrial, double> Score(IEnumerable<(CheckTrial Trial, IReadOnlyList<FrameSample> Frames)> runs)
    {
        var result = new Dictionary<CheckTrial, double>();
        foreach (var group in runs.GroupBy(r => r.Trial))
        {
            var frames = group.SelectMany(r => r.Frames).ToList();
            result[group.Key] = frames.Count == 0 ? 0 : frames.Count(f => Matches(group.Key, f)) / (double)frames.Count;
        }
        return result;
    }

    public static bool Passed(IReadOnlyDictionary<CheckTrial, double> scores) =>
        scores.Count > 0 && scores.Values.All(s => s >= MinMatch);
}
