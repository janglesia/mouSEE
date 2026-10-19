using app.Calibration;

namespace app.Gaze;

/// <summary>
/// The re-center: the way someone sits changes a bit from one day (or hour) to the next, and the calibration only knows
/// how they sat then. Looking at the middle of the screen for a moment shows how far off the bubble is now, and that's
/// subtracted from every bubble position after. On the first accuracy tests this took left/right from 6.6% to 4.5% of
/// the screen width when the head sat 3° higher than during calibration.
/// </summary>
public static class Recenter
{
    public const int SettleMs = 800;      // the eyes get to the dot, not used
    public const int CaptureMs = 1500;
    // fewer frames with a bubble than this and it's a guess
    public const int MinFrames = 15;
    // off by more than this isn't drift, the calibration doesn't fit any more (another person, the camera moved...)
    public const double MaxOffset = 0.3;

    /// <summary>The shift and where the head was, from the frames while looking at the middle (0.5, 0.5).
    /// Problem says why not when it couldn't work it out.</summary>
    public static (GazeOffset? Offset, HeadPosition? At, string? Problem) From(IReadOnlyList<FrameSample> frames)
    {
        var bubble = frames.Where(f => f.EstimateX is not null && f.EstimateY is not null).ToList();
        if (bubble.Count < MinFrames)
            return (null, null, "Couldn't see your eyes well enough. Look at the dot with both eyes open, then try again (Ctrl+Alt+R).");

        double x = Stats.Percentile(bubble.Select(f => f.EstimateX!.Value).ToList(), 50) - 0.5;
        double y = Stats.Percentile(bubble.Select(f => f.EstimateY!.Value).ToList(), 50) - 0.5;
        if (Math.Abs(x) > MaxOffset || Math.Abs(y) > MaxOffset)
            return (null, null, "The bubble was very far off. A new calibration will work better than re-centering (Ctrl+Alt+C).");

        return (new GazeOffset(Math.Round(x, 4), Math.Round(y, 4)), HeadPosition.Of(frames), null);
    }
}
