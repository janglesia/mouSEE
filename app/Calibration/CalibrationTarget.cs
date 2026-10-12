namespace app.Calibration;

// IsValidation is true for the extra accuracy-check targets shown after the calibration targets.
// Don't fit the gaze mapping on those; measure how far the predicted gaze lands from them instead.
public sealed record CalibrationTarget(int Index, double NormalizedX, double NormalizedY, bool IsValidation = false);