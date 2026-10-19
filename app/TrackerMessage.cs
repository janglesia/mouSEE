using System.Text.Json.Serialization;

namespace app;

// JSON messages from the tracker
public record TrackerMessage(string Type, long T, List<Face>? Faces, string? Message, int Width, int Height, long Wall = 0,
	[property: JsonPropertyName("capture_fps")] double CaptureFps = 0,
    [property: JsonPropertyName("landmark_ms")] double LandmarkMs = 0,
    Tracking? Tracking = null,
    List<CameraInfo>? Cameras = null,
    string? Key = null);
// from tracker/tracking_status.py. State: ok / eyes_closed / lost. Reason says why it's lost
public record Tracking(string State, string? Reason);
// from face_tracker.py --list-cameras: a camera that opened and its default resolution
public record CameraInfo(int Index, int Width, int Height);
public record Face(double[] Box, double[][] Landmarks, Irises Irises, Pose Pose,
				   Dictionary<string, double> Blendshapes, Eyes? Eyes = null);
public record Irises(double[] Left, double[] Right);
public record Pose(double Yaw, double Pitch, double Roll);

public record Eyes(EyeData Left, EyeData Right, GazeData Gaze, GazeEstimate? Estimate = null);
// the tracker sends more per eye (iris_ratio, contrast, openness, blink, covered, width_px),
// add them here when needed. the eye check wizard will, see docs/EYE-CALIBRATION.md
// i added this gaze estimate for the bubble
public record GazeEstimate(
    bool Valid,
    string? Reason,
    [property: JsonPropertyName("h_deg")] double? HDeg,
    [property: JsonPropertyName("v_deg")] double? VDeg,
    double? X,
    double? Y,
    [property: JsonPropertyName("eyes_used")] int EyesUsed
);
public record EyeData(EyeOffset Offset, bool Closed, double Openness = 0, bool Covered = false);
public record EyeOffset(double H, double V);
public record GazeRaw(double? H, double? V);
public record GazeError(double? H, double? V);
public record GazeData(GazeRaw Raw, double? H, double? V, string Direction, GazeError BinocularError,
				   [property: JsonPropertyName("eyes_used")] int EyesUsed);