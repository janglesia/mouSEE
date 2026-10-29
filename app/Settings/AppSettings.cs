using System.Text.Json.Serialization;

namespace app.Settings;

// All user settings, saved to app/Settings/settings.json. To add one, see docs/SETTINGS.md.
// Every property needs a [Setting], the default is the initializer.
// They're records, so old.Camera != now.Camera tells you if anything in that section changed.
public sealed record AppSettings
{
    [Section("Camera")] public CameraSettings Camera { get; init; } = new();
    [Section("Cursor")] public CursorSettings Cursor { get; init; } = new();
    [Section("Clicking")] public ClickingSettings Clicking { get; init; } = new();
    [Section("Tracking")] public TrackingSettings Tracking { get; init; } = new();
    [Section("Head")] public HeadSettings Head { get; init; } = new();
    [Section("Overlay")] public OverlaySettings Overlay { get; init; } = new();
    [Section("Calibration")] public CalibrationSettings Calibration { get; init; } = new();
    [Section("Advanced")] public AdvancedSettings Advanced { get; init; } = new();

    // every value back in range, missing sections filled in
    public AppSettings Normalized() => SettingsCatalog.Normalize(this);
}

// passed to face_tracker.py when it starts, so changing any of these restarts the tracker
public sealed record CameraSettings
{
    [Setting("Camera", "Which camera to use. 0 is the first one, make cameras lists them.", Min = 0, Max = 9, Step = 1)]
    public int Index { get; init; }

    // the webcam's picture size. not the screen: that's read from Windows (SystemParameters) and is never a setting
    [Setting("Camera resolution", "The size of the webcam's picture, not your screen's (mouSEE always uses your screen's own size). Higher tracks the eyes more precisely but uses more CPU. If the camera can't do it, it picks the closest mode.")]
    public Resolution Resolution { get; init; } = Resolution.P1080;

    [Setting("Frame rate", "Frames per second to ask the camera for. Many webcams only do 30.", Min = 15, Max = 120, Step = 5, Unit = "fps")]
    public int Fps { get; init; } = 60;

    [Setting("Show camera window", "Opens a window with the camera image and the tracked points. Press q in it to quit, p to pause.")]
    public bool ShowPreview { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<Resolution>))]
public enum Resolution
{
    [JsonStringEnumMemberName("360p"), Choice("360p (640 x 360)")] P360,
    [JsonStringEnumMemberName("480p"), Choice("480p (640 x 480)")] P480,
    [JsonStringEnumMemberName("720p"), Choice("720p (1280 x 720)")] P720,
    [JsonStringEnumMemberName("1080p"), Choice("1080p (1920 x 1080)")] P1080,
}

public static class ResolutionExtensions
{
    // same sizes as make run 360 / 480 / 720 / 1080
    public static (int Width, int Height) Size(this Resolution r) => r switch
    {
        Resolution.P360 => (640, 360),
        Resolution.P480 => (640, 480),
        Resolution.P720 => (1280, 720),
        _ => (1920, 1080),
    };
}

public sealed record CursorSettings
{
    [Setting("Sensitivity", "Low keeps the cursor steady but it follows your eyes a bit later. High follows faster but shakes more.", Min = 0, Max = 1, Step = 0.05)]
    public double Sensitivity { get; init; } = 0.5;

    [Setting("Show gaze bubble", "Draws a circle where mouSEE thinks you're looking.")]
    public bool ShowGazeBubble { get; init; } = true;
}

public sealed record ClickingSettings
{
    [Setting("Click method", "Dwell clicks when you keep looking at the same spot, blink clicks when you blink on purpose.")]
    public ClickMethod Method { get; init; } = ClickMethod.Dwell;

    [Setting("Dwell time", "How long you have to look at something before it gets clicked.", Min = 200, Max = 5000, Step = 50, Unit = "ms")]
    public int DwellMs { get; init; } = 1000;

    [Setting("Dwell radius", "How far your gaze can wander while dwelling and still count as the same spot.", Min = 10, Max = 200, Step = 5, Unit = "px")]
    public int DwellRadiusPx { get; init; } = 50;
}

// same values as click_method in the Settings table (persistence/database.py)
[JsonConverter(typeof(JsonStringEnumConverter<ClickMethod>))]
public enum ClickMethod
{
    [JsonStringEnumMemberName("dwell"), Choice("Dwell")] Dwell,
    [JsonStringEnumMemberName("blink"), Choice("Blink")] Blink,
}

public sealed record TrackingSettings
{
    [Setting("Paused", "Stops the cursor from following your eyes. Ctrl+Alt+P, or p in the camera window, switches it.")]
    public bool Paused { get; init; }
}

// what moving your head does to the bubble, see app/Bubble/HeadModes/ and docs/bubble/HEAD-MODES.md. the mode is picked in the
// calibrations window, the nudge numbers only matter in the two nudge modes
public sealed record HeadSettings
{
    [Setting("Head mode", "What moving your head does. Eyes only: nothing, the head correction undoes it. Eyes + head nudge: nothing either, until your eyes rest on a spot and the bubble holds, then turning or tilting your head pushes it. Eyes + head nudge, no head correction: the same nudge, but leaning or tilting also moves the bubble by accident, for comparing. Turning the head correction on or off restarts the tracker.")]
    public HeadMode Mode { get; init; } = HeadMode.EyesOnly;

    [Setting("Nudge strength", "How far the bubble moves for each degree you turn or tilt your head while nudging.", Min = 0.25, Max = 5, Step = 0.25, Unit = "% of the screen per degree")]
    public double NudgeStrength { get; init; } = 1.5;

    [Setting("Nudge dead zone", "Head movement smaller than this doesn't nudge, so the bubble doesn't drift while you hold still.", Min = 0, Max = 5, Step = 0.25, Unit = "degrees")]
    public double NudgeDeadZone { get; init; } = 1;

    [Setting("Settle time", "How long your eyes have to stay on one spot before the bubble holds there and your head can nudge it. Longer gives your eyes time to get there first.", Min = 100, Max = 3000, Step = 50, Unit = "ms")]
    public int NudgeSettleMs { get; init; } = 600;

    [Setting("Let go after looking away", "How far your eyes have to move from the held spot before the bubble follows them again. Raise it if the bubble lets go while you nudge.", Min = 5, Max = 50, Step = 1, Unit = "% of the screen")]
    public int NudgeReleasePercent { get; init; } = 20;
}

public sealed record OverlaySettings
{
    [Setting("Show status bar", "The coloured bar in the bottom left that says whether tracking works.")]
    public bool ShowStatusBar { get; init; } = true;

    [Setting("Show debug readout", "fps, latency, head angle, irises, blinks and gaze under the status bar.")]
    public bool ShowDebugReadout { get; init; } = true;

    [Setting("Move hint after", "How far you can move from where you sat during calibration (to the side, up or down) before the bar says which way to go back. Smaller tells you sooner. Closer or further from the camera scales with it.", Min = 3, Max = 15, Step = 1, Unit = "% of the camera image")]
    public int PositionHintPercent { get; init; } = 6;

    [Setting("Tilt hint after", "How far your head can tilt up or down from how you held it during calibration before the bar says to tilt it back. Reading the bottom of the screen alone tilts it 3-4 degrees.", Min = 4, Max = 20, Step = 1, Unit = "degrees")]
    public double TiltHintDegrees { get; init; } = 8;
}

public sealed record CalibrationSettings
{
    [Setting("Time per target", "How long each calibration target is looked at. Longer gives calibration more samples.", Min = 500, Max = 5000, Step = 100, Unit = "ms")]
    public int TargetDwellMs { get; init; } = 1200;

    [Setting("Ask to calibrate", "When there's no calibration yet, asks on startup if you want to make one. Without one mouSEE uses default values.")]
    public bool AskWhenMissing { get; init; } = true;

    [Setting("Re-center on startup", "When mouSEE starts, look at a dot in the middle for two seconds so the bubble fits how you're sitting today.")]
    public bool RecenterOnStart { get; init; } = true;
}

public sealed record AdvancedSettings
{
    [Setting("Lost after", "How long tracking has to be bad before it counts as lost. Too short and blinks count.", Min = 100, Max = 5000, Step = 50, Unit = "ms")]
    public int LostAfterMs { get; init; } = 500;

    [Setting("Frames to recover", "Good frames in a row needed before tracking counts again after a problem.", Min = 1, Max = 120, Step = 1, Unit = "frames")]
    public int RecoverFrames { get; init; } = 10;

    [Setting("Not responding after", "No frames from the tracker for this long means it's stuck.", Min = 250, Max = 10000, Step = 250, Unit = "ms")]
    public int NotRespondingAfterMs { get; init; } = 1000;

    [Setting("Face detection confidence", "Lower finds a face more easily in bad light, higher gives fewer false detections. Restarts the tracker.", Min = 0.1, Max = 0.9, Step = 0.05)]
    public double DetectionConfidence { get; init; } = 0.5;
}
