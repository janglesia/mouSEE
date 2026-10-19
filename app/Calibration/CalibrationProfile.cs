using System.Text.Json.Serialization;

namespace app.Calibration;

// one saved calibration, app/Calibration/Saved/<Id>.json. See docs/SAVED-CALIBRATIONS.md
public sealed record CalibrationProfile
{
    // bump when the file format changes in a way old files can't be read as
    public int Version { get; init; } = 1;

    // file name without .json, set by CalibrationStore. not saved in the file
    [JsonIgnore] public string Id { get; init; } = "";

    public string Name { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    // the one used most recently is the one in use
    public DateTimeOffset LastUsedAt { get; init; }

    // primary screen size when it was made, in WPF units (SystemParameters.PrimaryScreenWidth/Height).
    // a calibration only fits the screen it was made on
    public double ScreenWidth { get; init; }
    public double ScreenHeight { get; init; }

    public GazeModel Gaze { get; init; } = new();

    // from the eye check (EyeCheckWindow). empty = the tracker's defaults
    public EyeThresholds Eyes { get; init; } = new();

    // what the eye check measured and why each value is what it is. for reading, the tracker only uses Eyes
    public EyeCheckReport? EyeCheck { get; init; }

    // what the eyes did at each nine-point target, what the gaze numbers get fitted to
    public List<GazeTargetSummary> GazeTargets { get; init; } = [];

    // the raw frames are in their own file, CalibrationStore.RecordingFileFor(Id)
}

/// <summary>
/// Per-person eye detection values from the eye check, named like the constants in tracker/eye_extractor.py
/// and tracker/tracking_status.py so the tracker can set them straight from the file. null = keep the default
/// (the eye check couldn't work it out, or it wasn't run). See docs/EYE-CALIBRATION.md
/// </summary>
public sealed record EyeThresholds
{
    // eye closed when iris_ratio is above this (eye_extractor.py)
    [JsonPropertyName("IRIS_HIDDEN_RATIO")] public double? IrisHiddenRatio { get; init; }
    // lids at least this far apart mean the eye is open whatever the iris says, glasses reflections (eye_extractor.py)
    [JsonPropertyName("LIDS_OPEN_OPENNESS")] public double? LidsOpenOpenness { get; init; }
    // eye covered when contrast is below this (eye_extractor.py). the eye check doesn't set it (that needs a hand
    // over the eye), it's here for a calibration file that has it
    [JsonPropertyName("COVERED_CONTRAST")] public double? CoveredContrast { get; init; }
    // head angle when sitting normally, head_turned is measured from here (tracking_status.py)
    [JsonPropertyName("REST_PITCH")] public double? RestPitch { get; init; }
    [JsonPropertyName("REST_YAW")] public double? RestYaw { get; init; }
    // how far from resting the head can turn before it counts as head_turned (tracking_status.py)
    [JsonPropertyName("MAX_PITCH")] public double? MaxPitch { get; init; }
    [JsonPropertyName("MAX_YAW")] public double? MaxYaw { get; init; }
}

/// <summary>
/// The numbers for GazeParams in tracker/gaze_model.py, with the same names so the tracker can read them
/// straight from the file. null means calibration didn't work it out, and the tracker uses its default for it.
/// </summary>
public sealed record GazeModel
{
    [JsonPropertyName("k_h")] public double? KH { get; init; }
    [JsonPropertyName("k_v")] public double? KV { get; init; }

    [JsonPropertyName("left_h_baseline")] public double? LeftHBaseline { get; init; }
    [JsonPropertyName("left_v_baseline")] public double? LeftVBaseline { get; init; }
    [JsonPropertyName("right_h_baseline")] public double? RightHBaseline { get; init; }
    [JsonPropertyName("right_v_baseline")] public double? RightVBaseline { get; init; }

    [JsonPropertyName("center_x")] public double? CenterX { get; init; }
    [JsonPropertyName("center_y")] public double? CenterY { get; init; }
    [JsonPropertyName("gain_x")] public double? GainX { get; init; }
    [JsonPropertyName("gain_y")] public double? GainY { get; init; }
}
