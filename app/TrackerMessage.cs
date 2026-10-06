namespace app;

// JSON messages from the tracker
public record TrackerMessage(string Type, long T, List<Face>? Faces, string? Message, int Width, int Height, long Wall = 0);
public record Face(double[] Box, double[][] Landmarks, Irises Irises, Pose Pose, Dictionary<string, double> Blendshapes);
public record Irises(double[] Left, double[] Right);
public record Pose(double Yaw, double Pitch, double Roll);