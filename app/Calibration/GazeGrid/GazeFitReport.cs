namespace app.Calibration.GazeGrid;

// What the gaze fit found, saved with the calibration (CalibrationProfile.GazeFit). The fit itself is GazeFit.

/// <summary>How well the fitted numbers put each grid target back where it was (left/right only for now).</summary>
public sealed record GazeFitTarget(int Index, double X, double PredictedX, double Error);

/// <summary>What the gaze fit found, saved with the calibration (CalibrationProfile.GazeFit).</summary>
public sealed record GazeFitReport
{
    public string Axis { get; init; } = "x";
    public double KH { get; init; }
    public double LeftHBaseline { get; init; }
    public double RightHBaseline { get; init; }
    public double CenterX { get; init; }
    public double GainX { get; init; }
    // errors as a fraction of the screen width: 0.035 = 3.5%
    public double MeanError { get; init; }
    public double MaxError { get; init; }
    // fitted without that target, then measured on it, so closer to what you get on the rest of the screen
    public double LeaveOneOutError { get; init; }
    // the default numbers on the same targets, for comparison
    public double DefaultMeanError { get; init; }
    public double ScreenWidth { get; init; }
    public List<GazeFitTarget> Targets { get; init; } = [];
    public string Explanation { get; init; } = "";

    // ---- leaning left/right, from the lean target (null / false if there wasn't one, or it couldn't be fitted)
    public bool HeadFitted { get; init; }
    public double? HeadXGain { get; init; }
    // where the middle of the face was in the camera image during the grid targets (0-1)
    public double? HeadXRef { get; init; }
    // how far the face moved while leaning, 5th to 95th percentile, as a share of the camera image width
    public double? LeanRange { get; init; }
    // how far off the bubble was while leaning, without and with the correction (share of the screen width)
    public double? LeanErrorBefore { get; init; }
    public double? LeanErrorAfter { get; init; }
    public string? HeadExplanation { get; init; }

    // looking down squeezes left/right, see GazeModel.XSqueeze. null if not fitted
    public double? XSqueeze { get; init; }
    public double? OpennessRef { get; init; }

    // ---- the head step (leaning and tilting with dots on both sides), see FitHead
    public double? HeadXShrink { get; init; }
    public double? HeadYPitch { get; init; }
    public double? HeadYLean { get; init; }
    // the head pitch during the grid targets, tilting is measured from there
    public double? PitchRef { get; init; }
    // on the head step's dots: how far off without and with the corrections (share of the screen height for y)
    public double? HeadYErrorBefore { get; init; }
    public double? HeadYErrorAfter { get; init; }
    public List<GazeFitTarget> HeadTargets { get; init; } = [];

    // ---- up/down, from the grid targets' lid openness and vertical iris offset
    public bool YFitted { get; init; }
    public double? YConst { get; init; }
    public double? YOpenness { get; init; }
    public double? YIris { get; init; }
    public double? YArc { get; init; }
    public double? YOpennessSq { get; init; }
    // which shape was picked (GazeFit.YShapes): flat, arc or arc-and-uneven
    public string? YShape { get; init; }
    // errors as a share of the screen height
    public double? YMeanError { get; init; }
    public double? YMaxError { get; init; }
    public double? YLeaveOneOutError { get; init; }
    public double? YDefaultMeanError { get; init; }
    public List<GazeFitTarget> YTargets { get; init; } = [];
    public string? YExplanation { get; init; }

    // which version of the fit made this. older ones get fitted again from their recording when mouSEE starts
    public int Version { get; init; }
}
