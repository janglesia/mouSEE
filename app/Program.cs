using System.Diagnostics;
using System.Text.Json;

// tracker folder sits next to app/
string trackerDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "tracker"));
string scriptPath = Path.Combine(trackerDir, "face_tracker.py");
string venvPython = Path.Combine(trackerDir, ".venv", "Scripts", "python.exe");
string pythonExe = File.Exists(venvPython) ? venvPython : "python";

if (!File.Exists(scriptPath))
{
    Console.WriteLine($"Can't find tracker at {scriptPath}. Run this from the app folder.");
    return 1;
}

var psi = new ProcessStartInfo
{
    FileName = pythonExe,
    ArgumentList = { "-u", scriptPath },
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false,
    CreateNoWindow = true,
    WorkingDirectory = trackerDir,
};
// pass our args through to the tracker (--preview, --camera 1, ...)
foreach (var arg in args) psi.ArgumentList.Add(arg);

Console.WriteLine($"Starting tracker with {pythonExe} ...");
using var proc = Process.Start(psi)!;

// tracker logs come in on stderr
proc.ErrorDataReceived += (_, e) =>
{
    if (e.Data is not null) Console.Error.WriteLine($"[tracker] {e.Data}");
};
proc.BeginErrorReadLine();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var clock = Stopwatch.StartNew();
long lastPrintMs = 0, fpsWindowStartMs = 0;
int framesInWindow = 0;
double fps = 0;

try
{
    while (!cts.IsCancellationRequested)
    {
        string? line = await proc.StandardOutput.ReadLineAsync(cts.Token);
        if (line is null) break; // tracker exited

        TrackerMessage? msg;
        try { msg = JsonSerializer.Deserialize<TrackerMessage>(line, json); }
        catch (JsonException) { Console.Error.WriteLine($"[bad line] {line}"); continue; }
        if (msg is null) continue;

        switch (msg.Type)
        {
            case "ready":
                Console.WriteLine($"Camera ready ({msg.Width}x{msg.Height}). Press Ctrl+C to stop.");
                break;

            case "error":
                Console.WriteLine($"\nTracker error: {msg.Message}");
                break;

            case "frame":
                framesInWindow++;
                long now = clock.ElapsedMilliseconds;
                if (now - fpsWindowStartMs >= 1000)
                {
                    fps = framesInWindow * 1000.0 / (now - fpsWindowStartMs);
                    framesInWindow = 0;
                    fpsWindowStartMs = now;
                }

                // Only redraw the status line ~10 times a second
                if (now - lastPrintMs >= 100)
                {
                    lastPrintMs = now;
                    string status;
                    if (msg.Faces is { Count: > 0 } faces)
                    {
                        var f = faces[0];
                        var p = f.Pose;
                        double blinkL = f.Blendshapes.GetValueOrDefault("eyeBlinkLeft");
                        double blinkR = f.Blendshapes.GetValueOrDefault("eyeBlinkRight");
                        status = $"yaw {p.Yaw,4:0} pitch {p.Pitch,4:0} roll {p.Roll,4:0} | " +
                                 $"iris L ({f.Irises.Left[0]:0.000}, {f.Irises.Left[1]:0.000}) " +
                                 $"R ({f.Irises.Right[0]:0.000}, {f.Irises.Right[1]:0.000}) | " +
                                 $"blink L {blinkL:0.00} R {blinkR:0.00}";
                    }
                    else status = "no face";
                    Console.Write($"\r{fps,5:0.0} fps | {status}".PadRight(110));
                }
                break;
        }
    }
}
catch (OperationCanceledException) { }
finally
{
    if (!proc.HasExited) proc.Kill(entireProcessTree: true);
    Console.WriteLine("\nStopped.");
}
return 0;

// JSON messages from the tracker
record TrackerMessage(string Type, long T, List<Face>? Faces, string? Message, int Width, int Height);
record Face(double[] Box, double[][] Landmarks, Irises Irises, Pose Pose, Dictionary<string, double> Blendshapes);
record Irises(double[] Left, double[] Right);
record Pose(double Yaw, double Pitch, double Roll);
