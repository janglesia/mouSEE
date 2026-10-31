namespace app.Calibration.GazeGrid;

public sealed record MissingCalibrationFeature(
    int TargetIndex,
    double X,
    double Y,
    string? Pose,
    string Feature);

public sealed record CalibrationFeatureValidation(IReadOnlyList<MissingCalibrationFeature> Missing){
    public bool IsValid => Missing.Count == 0;
}

public static class CalibrationFeatureValidator{
    public static CalibrationFeatureValidation Validate(IReadOnlyList<GazeTargetSummary> targets){
        var missing = new List<MissingCalibrationFeature>();

        foreach(var target in targets){
            if(target.UsableFrames < GazeFit.MinUsableFrames){
                missing.Add(new MissingCalibrationFeature(
                    target.Index,
                    target.X,
                    target.Y,
                    target.Pose,
                    $"UsableFrames ({target.UsableFrames}/{GazeFit.MinUsableFrames})"));
            }

            foreach(var feature in RequiredFeatures(target)){
                if(!HasFeature(target, feature)){
                    missing.Add(new MissingCalibrationFeature(
                        target.Index,
                        target.X,
                        target.Y,
                        target.Pose,
                        feature
                    ));
                }
            }
        }

        return new CalibrationFeatureValidation(missing);
    }

    private static IReadOnlyList<string> RequiredFeatures(GazeTargetSummary target){
        var required = new List<string>{
            nameof(GazeTargetSummary.LeftH),
            nameof(GazeTargetSummary.RightH),
            nameof(GazeTargetSummary.Openness),
        };

        if(target.Pose is not null){
            required.Add(nameof(GazeTargetSummary.FaceX));
            required.Add(nameof(GazeTargetSummary.Pitch));
        }

        return required;
    }

    private static bool HasFeature(
        GazeTargetSummary target,
        string feature) => feature switch{
            nameof(GazeTargetSummary.LeftH) => target.LeftH is not null,
            nameof(GazeTargetSummary.RightH) => target.RightH is not null,
            nameof(GazeTargetSummary.Openness) => target.Openness is not null,
            nameof(GazeTargetSummary.FaceX) => target.FaceX is not null,
            nameof(GazeTargetSummary.Pitch) => target.Pitch is not null,
            _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null),
    };
}