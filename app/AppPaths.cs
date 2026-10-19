using System.IO;

namespace app;

// where mouSEE keeps its files. Everything lives in the app/ folder (the one with mouSEE.csproj),
// found by walking up from the exe (app/bin/Debug/...). A published build has no project folder,
// then it's the folder the exe is in
public static class AppPaths
{
    public static string AppFolder { get; } = FindAppFolder();

    public static string SettingsFile => Path.Combine(AppFolder, "Settings", "settings.json");

    // one .json per saved calibration
    public static string CalibrationsFolder => Path.Combine(AppFolder, "Calibration", "Saved");

    private static string FindAppFolder()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "mouSEE.csproj"))) return dir.FullName;
                if (File.Exists(Path.Combine(dir.FullName, "app", "mouSEE.csproj"))) return Path.Combine(dir.FullName, "app");
            }
        }
        return AppContext.BaseDirectory;
    }
}
