using System.Text.Json.Serialization;

namespace app;

// JSON messages from the tracker
public record TrackerMessage(string Type, long T, List<Face>? Faces, string? Message, int Width, int Height, long Wall = 0);
public record Face(double[] Box, double[][] Landmarks, Irises Irises, Pose Pose,
				   Dictionary<string, double> Blendshapes, Eyes? Eyes = null);
public record Irises(double[] Left, double[] Right);
public record Pose(double Yaw, double Pitch, double Roll);

public record Eyes(EyeData Left, EyeData Right, Gaze Gaze);
public record EyeData(EyeOffset Offset, bool Closed);
public record EyeOffset(double H, double V);
public record GazeRaw(double? H, double? V);
public record GazeError(double? H, double? V);
public record Gaze(GazeRaw Raw, double? H, double? V, string Direction, GazeError BinocularError,
				   [property: JsonPropertyName("eyes_used")] int EyesUsed);