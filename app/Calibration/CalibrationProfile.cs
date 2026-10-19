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
