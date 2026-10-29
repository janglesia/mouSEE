namespace app.Calibration.EyeCheck;

public enum CheckTrial { CloseBoth, OpenBoth }

/// <summary>
/// The last step of the eye check: closing both eyes and opening them, run with the new value loaded in the tracker,
/// counting how often its closed decision matches what was asked. Under 90% for either means the value doesn't work for
/// this person, see docs/calibration/EYE-CALIBRATION.md
/// </summary>
public static class EyeCheckRound
{
    public const int TrialMs = 3000;
    public const double MinMatch = 0.9;
    // every instruction this many times, taking turns. once each is enough: 3 s each is ~100 frames after the reaction time
    public const int Repeats = 1;

    public static string Say(CheckTrial trial) =>
        trial == CheckTrial.CloseBoth ? "Close both eyes until you hear the sound." : "Open your eyes.";

    /// <summary>How the eyes should be for a trial, shown under the instruction.</summary>
    public static string Eyes(CheckTrial trial) => trial == CheckTrial.CloseBoth ? "Both eyes: closed" : "Both eyes: open";

    /// <summary>"Good" while the tracker sees what was asked in most of the last moment's frames, shown green under the
    /// instruction. null otherwise: the check round doesn't nag, getting it wrong is what it measures.</summary>
    public static string? LiveGood(CheckTrial trial, IReadOnlyList<FrameSample> recent)
    {
        if (recent.Count < 5 || recent.Count(f => Matches(trial, f)) < recent.Count * 0.8) return null;
        return trial == CheckTrial.CloseBoth ? "Good, both eyes closed" : "Good, both eyes open";
    }

    public static string Name(CheckTrial trial) => trial == CheckTrial.CloseBoth ? "both eyes closed" : "both eyes open";

    /// <summary>Closed and open taking turns, Repeats times each, starting with either.</summary>
    public static List<CheckTrial> Order(Random random)
    {
        var first = random.Next(2) == 0 ? CheckTrial.CloseBoth : CheckTrial.OpenBoth;
        var second = first == CheckTrial.CloseBoth ? CheckTrial.OpenBoth : CheckTrial.CloseBoth;
        return Enumerable.Range(0, Repeats).SelectMany(_ => new[] { first, second }).ToList();
    }

    /// <summary>Does this frame show what was asked? Uses the tracker's own decisions, that's what's being checked.
    /// An eye the tracker thinks is covered is wrong for both, nothing was in front of it.</summary>
    public static bool Matches(CheckTrial trial, FrameSample f)
    {
        if (!f.FaceFound) return false;
        bool closed = trial == CheckTrial.CloseBoth;
        return f.Left is { Covered: false } l && l.Closed == closed && f.Right is { Covered: false } r && r.Closed == closed;
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
