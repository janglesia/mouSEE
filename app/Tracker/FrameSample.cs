namespace app.Tracker;

// One tracker frame as the app records it (for calibrations, tests and the re-center), made from a TrackerMessage.

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
