using System.Text.Json.Serialization;

namespace app;

// JSON messages from the tracker
public record TrackerMessage(string Type, long T, List<Face>? Faces, string? Message, int Width, int Height, long Wall = 0,
	[property: JsonPropertyName("capture_fps")] double CaptureFps = 0,
    [property: JsonPropertyName("landmark_ms")] double LandmarkMs = 0,
    Tracking? Tracking = null);
// from tracker/tracking_status.py. State: ok / eyes_closed / lost. Reason says why it's lost
public record Tracking(string State, string? Reason);
public record Face(double[] Box, double[][] Landmarks, Irises Irises, Pose Pose,
				   Dictionary<string, double> Blendshapes, Eyes? Eyes = null);
public record Irises(double[] Left, double[] Right);
public record Pose(double Yaw, double Pitch, double Roll);

public record Eyes(EyeData Left, EyeData Right, GazeData Gaze);
// the tracker sends more per eye (iris_ratio, contrast, openness, blink, covered, width_px),
// add them here when needed. the eye check wizard will, see docs/eye-calibration.md
public record EyeData(EyeOffset Offset, bool Closed);
public record EyeOffset(double H, double V);
public record GazeRaw(double? H, double? V);
public record GazeError(double? H, double? V);
public record GazeData(GazeRaw Raw, double? H, double? V, string Direction, GazeError BinocularError,
				   [property: JsonPropertyName("eyes_used")] int EyesUsed);