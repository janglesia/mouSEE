namespace app.Calibration;

// Accuracy measured on targets that were NOT used to fit the gaze mapping: for each one, the distance
// between where it was drawn and where the model said the user was looking.
// Distances are in WPF device-independent pixels (the same units as SystemParameters.PrimaryScreenWidth).
public sealed record CalibrationValidation(double MeanErrorPx, double MaxErrorPx, int TargetCount);