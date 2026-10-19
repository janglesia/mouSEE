using System.Globalization;

namespace app.Settings;

public static class TrackerArgs
{
    // settings first, then the app's command line. argparse keeps the last value it sees,
    // so make run ARGS="--camera 1" and make run 720 still win over the file.
    // calibrationFile is the calibration in use, null for the tracker's default values
    public static string[] Build(AppSettings settings, string? calibrationFile, IEnumerable<string> commandLine)
    {
        var camera = settings.Camera;
        var (width, height) = camera.Resolution.Size();
        var args = new List<string>
        {
            "--camera", Str(camera.Index),
            "--width", Str(width),
            "--height", Str(height),
            "--fps", Str(camera.Fps),
            "--min-confidence", settings.Advanced.DetectionConfidence.ToString(CultureInfo.InvariantCulture),
        };
        if (camera.ShowPreview) args.Add("--preview");
        if (calibrationFile is not null) args.AddRange(["--calibration", calibrationFile]);
        args.AddRange(commandLine);
        return args.ToArray();
    }

    private static string Str(int value) => value.ToString(CultureInfo.InvariantCulture);
}
