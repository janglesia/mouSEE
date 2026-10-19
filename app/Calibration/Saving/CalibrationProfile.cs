using System.Text.Json.Serialization;

namespace app.Calibration.Saving;

// one saved calibration, app/Calibration/Saved/<Id>.json. See docs/calibration/SAVED-CALIBRATIONS.md
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

    // what the eyes did at each grid target, what the gaze numbers get fitted to
    public List<GazeTargetSummary> GazeTargets { get; init; } = [];

    // how Gaze was fitted to GazeTargets and how far off it still is. null if it wasn't fitted
    public GazeFitReport? GazeFit { get; init; }

    // the fine-tune's last correction on the bubble (FineTuneWindow), and what it found. null if there wasn't one.
    // made against this Gaze, so a new fit of Gaze drops it
    public GazeCorrection? FineTune { get; init; }
    public FineTuneReport? FineTuneReport { get; init; }

    // the raw frames are in their own file, CalibrationStore.RecordingFileFor(Id)
}

/// <summary>
/// Per-person eye detection values from the eye check, named like the constants in tracker/eye_extractor.py
/// and tracker/tracking_status.py so the tracker can set them straight from the file. null = keep the default
/// (the eye check couldn't work it out, or it wasn't run). See docs/calibration/EYE-CALIBRATION.md
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

    // leaning left/right: x moves by HeadXGain per 1.0 the face moves across the camera image, from HeadXRef
    [JsonPropertyName("head_x_gain")] public double? HeadXGain { get; init; }
    [JsonPropertyName("head_x_ref")] public double? HeadXRef { get; init; }

    // up/down: y = YConst + YOpenness * lid openness + YIris * vertical iris offset (both eyes averaged)
    [JsonPropertyName("y_const")] public double? YConst { get; init; }
    [JsonPropertyName("y_openness")] public double? YOpenness { get; init; }
    [JsonPropertyName("y_iris")] public double? YIris { get; init; }
    // and the curves on it, when they help: + YArc * tan(left/right angle)², the sides reading higher or lower than
    // the middle, + YOpennessSq * openness², up/down stretched more at one end
    [JsonPropertyName("y_arc")] public double? YArc { get; init; }
    [JsonPropertyName("y_openness_sq")] public double? YOpennessSq { get; init; }

    // looking down squeezes left/right: x gets XSqueeze * tan(angle) * (lid openness - OpennessRef)
    [JsonPropertyName("x_squeeze")] public double? XSqueeze { get; init; }
    [JsonPropertyName("openness_ref")] public double? OpennessRef { get; init; }

    // from the head step: leaning also shrinks left/right, and leaning and tilting move up/down (see gaze_model.py)
    [JsonPropertyName("head_x_shrink")] public double? HeadXShrink { get; init; }
    [JsonPropertyName("head_y_pitch")] public double? HeadYPitch { get; init; }
    [JsonPropertyName("head_y_lean")] public double? HeadYLean { get; init; }
    [JsonPropertyName("pitch_ref")] public double? PitchRef { get; init; }
}
