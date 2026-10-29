namespace app.Bubble;

/// <summary>
/// Where the head is: the middle of the face in the camera image (0-1, mirrored so leaning to your right makes FaceX
/// bigger, and sliding down in the chair makes FaceY bigger), how wide the face is (bigger = closer), and the head
/// pitch in degrees. FaceY, FaceW and Pitch are null when not known (older calibrations).
/// </summary>
public sealed record HeadPosition(double FaceX, double? FaceY, double? FaceW, double? Pitch)
{
    public static HeadPosition? Of(IReadOnlyList<FrameSample> frames)
    {
        var face = frames.Where(f => f.FaceFound && f.FaceW > 0).ToList();
        if (face.Count == 0) return null;
        double Median(Func<FrameSample, double> pick) => Stats.Percentile(face.Select(pick).ToList(), 50);
        return new HeadPosition(Median(f => f.FaceX), Median(f => f.FaceY), Median(f => f.FaceW), Median(f => f.Pitch));
    }

    /// <summary>Where the head was during a calibration's grid targets, null if it doesn't have the face position.</summary>
    public static HeadPosition? Of(CalibrationProfile? calibration)
    {
        var grid = calibration?.GazeTargets.Where(t => !t.IsValidation && !t.MoveHead && t.Pose is null && t.FaceX is not null).ToList();
        if (grid is null || grid.Count == 0) return null;
        double? Median(Func<GazeTargetSummary, double?> pick)
        {
            var values = grid.Select(pick).OfType<double>().ToList();
            return values.Count == 0 ? null : Stats.Percentile(values, 50);
        }
        return new HeadPosition(Median(t => t.FaceX!.Median)!.Value, Median(t => t.FaceY?.Median), Median(t => t.FaceW), Median(t => t.Pitch?.Median));
    }
}
