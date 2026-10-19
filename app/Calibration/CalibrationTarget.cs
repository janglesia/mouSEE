namespace app.Calibration;

// IsValidation is true for the extra accuracy-check targets shown after the calibration targets.
// Don't fit the gaze mapping on those; measure how far the predicted gaze lands from them instead.
// MoveHead is true for the target looked at while leaning left and right: the eyes stay on it while the head moves,
// which is what shows how much leaning shifts the gaze (GazeFit.FitHeadX). Not used for the rest of the fit.
// (That was the first lean step, calibrations made since have the head step instead, see Pose.)
// Pose is set for the head step's targets: how the person holds their head for it (HeadPose). null = sitting normally.
// Those show the fit what leaning and tilting do (GazeFit.FitHead) and aren't used for the rest of the fit.
public sealed record CalibrationTarget(int Index, double NormalizedX, double NormalizedY, bool IsValidation = false,
                                       bool MoveHead = false, string? Pose = null);

public static class HeadPose
{
    public const string LeanLeft = "lean-left";
    public const string LeanRight = "lean-right";
    public const string HeadUp = "head-up";
    public const string HeadDown = "head-down";

    // the head step: two dots for each, on the left and right for leaning (it changes left/right), at the top and
    // bottom for tilting (it changes up/down)
    public static readonly (string Pose, double X, double Y)[] Step =
    [
        (LeanLeft, 0.12, 0.5), (LeanLeft, 0.88, 0.5),
        (LeanRight, 0.88, 0.5), (LeanRight, 0.12, 0.5),
        (HeadUp, 0.5, 0.12), (HeadUp, 0.5, 0.88),
        (HeadDown, 0.5, 0.88), (HeadDown, 0.5, 0.12),
    ];

    public static string Say(string pose) => pose switch
    {
        LeanLeft => "Lean to the left and stay there. Look at each dot.",
        LeanRight => "Now lean to the right and stay there. Look at each dot.",
        HeadUp => "Sit normally again, and tilt your head up a little. Keep it there and look at each dot.",
        _ => "Now tilt your head down a little. Keep it there and look at each dot.",
    };
}