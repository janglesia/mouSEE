namespace app.Calibration.GazeGrid;

// Leaning and tilting, from the head step (FitHead), and the older single lean target (FitHeadX). See GazeFit.cs.
public static partial class GazeFit
{
    // the head step: a correction is only fitted when the head moved enough for it, and dropped when it's too big to be real
    public const double MinPitchRange = 4;           // degrees between the head step's dots
    public const double MaxHeadShrink = 40;
    public const double MaxHeadYPitch = 0.1;         // per degree
    public const double MaxHeadYLean = 5;

    /// <summary>
    /// Fits what leaning and tilting do, from the head step: dots looked at while leaning left and right (on both sides)
    /// and with the head tilted up and down (at the top and bottom). For each dot, the rest of the fit says where the
    /// bubble would land, and how far that is from the dot is what the head did. Left/right gets the shift
    /// (head_x_gain) and the shrink (head_x_shrink: leaning, the eyes move less for the same distance on the screen),
    /// up/down gets the tilt (head_y_pitch) and leaning (head_y_lean). Each is fitted only if the head moved enough
    /// for it, and left out if the result is too big to be right.
    /// targets: all of them, the ones with a Pose are used. faceXRef / pitchRef: where the face was and how the head was
    /// tilted during the grid targets.
    /// </summary>
    public static (GazeModel Model, GazeFitReport Report) FitHead(GazeModel model, GazeFitReport report,
                                                                  IReadOnlyList<GazeTargetSummary> targets, double faceXRef, double pitchRef)
    {
        var dots = targets
            .Where(t => t.Pose is not null && t.UsableFrames >= MinUsableFrames && t.LeftH is not null && t.RightH is not null
                        && t.FaceX is not null && t.Pitch is not null)
            .ToList();
        (GazeModel, GazeFitReport) NotFitted(string why) =>
            (model, report with { HeadFitted = false, HeadXRef = Math.Round(faceXRef, 4), PitchRef = Math.Round(pitchRef, 1), HeadExplanation = why });
        if (dots.Count < 2)
            return NotFitted("Leaning and tilting weren't fitted: the head step had too few dots with both eyes open.");

        double k = model.KH ?? DefaultK, bL = model.LeftHBaseline ?? 0, bR = model.RightHBaseline ?? 0;
        double Tan(GazeTargetSummary t) => Math.Tan((Angle(t.LeftH!.Median, bL, k) + Angle(t.RightH!.Median, bR, k)) / 2);
        double BaseX(GazeTargetSummary t)
        {
            double tan = Tan(t);
            double squeeze = model.XSqueeze is { } s && t.Openness is not null ? s * tan * (t.Openness.Median - (model.OpennessRef ?? 0)) : 0;
            return (model.CenterX ?? 0.5) + (model.GainX ?? DefaultGainX) * tan + squeeze;
        }
        bool yFitted = model.YOpenness is not null && dots.All(t => t.Openness is not null);
        double BaseY(GazeTargetSummary t) => FittedY(model, t);
        double Moved(GazeTargetSummary t) => t.FaceX!.Median - faceXRef;
        double Tilted(GazeTargetSummary t) => t.Pitch!.Median - pitchRef;

        // ---- left/right: shift and shrink, from the dots where the face moved
        var leaning = dots.Where(t => Math.Abs(Moved(t)) >= MinLeanRange).ToList();
        double gain = 0, shrink = 0;
        var notes = new List<string>();
        if (leaning.Count >= 2 && leaning.Select(t => Math.Round(t.X, 2)).Distinct().Count() >= 2)
        {
            var c = Solve(leaning.Select(t => new[] { Moved(t), Tan(t) * Math.Abs(Moved(t)) }).ToList(),
                          leaning.Select(t => t.X - BaseX(t)).ToList());
            if (c is not null && Math.Abs(c[0]) <= MaxHeadGain && Math.Abs(c[1]) <= MaxHeadShrink) (gain, shrink) = (c[0], c[1]);
            else notes.Add("the leaning correction came out too big to be right, maybe you looked away from a dot");
        }
        else if (leaning.Count >= 1)
        {
            // dots on one side only: just the shift
            var c = Solve(leaning.Select(t => new[] { Moved(t) }).ToList(), leaning.Select(t => t.X - BaseX(t)).ToList());
            if (c is not null && Math.Abs(c[0]) <= MaxHeadGain) gain = c[0];
        }
        else
        {
            notes.Add($"your face moved less than {MinLeanRange:P0} of the camera image while leaning, lean further next time");
        }

        // ---- up/down: tilt and leaning, only with a fitted up/down
        double yPitch = 0, yLean = 0;
        double? yBefore = null, yAfter = null;
        if (yFitted)
        {
            var columns = new List<Func<GazeTargetSummary, double>>();
            var tilts = dots.Select(Tilted).ToList();
            bool tilted = tilts.Max() - tilts.Min() >= MinPitchRange;
            if (tilted) columns.Add(Tilted);
            if (leaning.Count >= 1) columns.Add(t => Math.Abs(Moved(t)));
            if (!tilted) notes.Add($"your head tilted less than {MinPitchRange:0}° between the dots, tilt a bit more next time");
            if (columns.Count > 0 && Solve(dots.Select(t => columns.Select(col => col(t)).ToArray()).ToList(),
                                           dots.Select(t => t.Y - BaseY(t)).ToList()) is { } c)
            {
                int i = 0;
                if (tilted) yPitch = c[i++];
                if (leaning.Count >= 1) yLean = c[i];
                if (Math.Abs(yPitch) > MaxHeadYPitch || Math.Abs(yLean) > MaxHeadYLean)
                {
                    notes.Add("the up/down correction for leaning and tilting came out too big to be right");
                    (yPitch, yLean) = (0, 0);
                }
            }
            yBefore = dots.Average(t => Math.Abs(BaseY(t) - t.Y));
            yAfter = dots.Average(t => Math.Abs(BaseY(t) + yPitch * Tilted(t) + yLean * Math.Abs(Moved(t)) - t.Y));
        }

        double PredictX(GazeTargetSummary t) => BaseX(t) + gain * Moved(t) + shrink * Tan(t) * Math.Abs(Moved(t));
        double xBefore = dots.Average(t => Math.Abs(BaseX(t) - t.X));
        double xAfter = dots.Average(t => Math.Abs(PredictX(t) - t.X));
        bool any = gain != 0 || shrink != 0 || yPitch != 0 || yLean != 0;

        var updated = model with
        {
            HeadXGain = gain != 0 ? Math.Round(gain, 4) : null,
            HeadXRef = Math.Round(faceXRef, 4),
            HeadXShrink = shrink != 0 ? Math.Round(shrink, 4) : null,
            HeadYPitch = yPitch != 0 ? Math.Round(yPitch, 5) : null,
            HeadYLean = yLean != 0 ? Math.Round(yLean, 4) : null,
            PitchRef = Math.Round(pitchRef, 1),
        };
        string Pct(double? v) => v is null ? "-" : $"{v:P1}";
        string explanation = any
            ? $"Leaning and tilting are corrected, from {dots.Count} dots of the head step. On those dots the bubble was {Pct(xBefore)} " +
              $"of the screen width off left/right without the corrections and {Pct(xAfter)} with them" +
              (yFitted ? $", up/down {Pct(yBefore)} of the height without and {Pct(yAfter)} with." : ".")
            : "Leaning and tilting weren't corrected.";
        if (notes.Count > 0) explanation += " Note: " + string.Join("; ", notes) + ".";

        return (updated, report with
        {
            HeadFitted = any,
            HeadXGain = updated.HeadXGain,
            HeadXRef = updated.HeadXRef,
            HeadXShrink = updated.HeadXShrink,
            HeadYPitch = updated.HeadYPitch,
            HeadYLean = updated.HeadYLean,
            PitchRef = updated.PitchRef,
            LeanRange = Math.Round(dots.Max(t => Math.Abs(Moved(t))), 3),
            LeanErrorBefore = Math.Round(xBefore, 3),
            LeanErrorAfter = Math.Round(xAfter, 3),
            HeadYErrorBefore = yBefore is null ? null : Math.Round(yBefore.Value, 3),
            HeadYErrorAfter = yAfter is null ? null : Math.Round(yAfter.Value, 3),
            HeadTargets = dots.Select(t => new GazeFitTarget(t.Index, t.X, Math.Round(PredictX(t), 3), Math.Round(Math.Abs(PredictX(t) - t.X), 3))).ToList(),
            HeadExplanation = explanation,
        });
    }

    // the face has to move at least this much (5th to 95th percentile, share of the camera image width) while leaning,
    // less than that and the slope is mostly noise
    public const double MinLeanRange = 0.03;
    // a correction bigger than this per 1.0 across the image isn't leaning, something went wrong
    public const double MaxHeadGain = 6;

    /// <summary>
    /// Fits the leaning correction (head_x_gain in gaze_model.py) from the lean target: the eyes stayed on a point
    /// that didn't move while the head did, so whatever the bubble did is caused by the head moving. The correction is the
    /// slope of "how far off the bubble was" against "how far the face moved from where it was during the grid targets".
    /// lean: the lean target's frames. faceXRef: where the middle of the face was during the grid targets.
    /// Returns the model and report with the correction added, or unchanged with a note if it couldn't be fitted.
    /// </summary>
    public static (GazeModel Model, GazeFitReport Report) FitHeadX(GazeModel model, GazeFitReport report,
                                                                   IReadOnlyList<FrameSample> lean, double targetX, double faceXRef)
    {
        double k = model.KH ?? DefaultK, bL = model.LeftHBaseline ?? 0, bR = model.RightHBaseline ?? 0;
        double center = model.CenterX ?? 0.5, gain = model.GainX ?? DefaultGainX;
        var frames = lean.Where(f => GazeTargetSummary.Usable(f) && f.FaceW > 0).ToList();
        (GazeModel, GazeFitReport) NotFitted(string why) =>
            (model, report with { HeadFitted = false, HeadXRef = Math.Round(faceXRef, 4), HeadExplanation = why });
        if (frames.Count < MinUsableFrames)
            return NotFitted("Leaning left and right wasn't fitted: there weren't enough frames with both eyes open. Leaning still moves the bubble.");

        var moved = frames.Select(f => f.FaceX - faceXRef).ToList();
        var off = frames.Select(f =>
            targetX - (center + gain * Math.Tan((Angle(f.Left!.OffsetH, bL, k) + Angle(f.Right!.OffsetH, bR, k)) / 2))).ToList();
        double range = Stats.Percentile(moved, 95) - Stats.Percentile(moved, 5);
        if (range < MinLeanRange)
            return NotFitted($"Leaning left and right wasn't fitted: your face only moved {range:P1} of the camera image, " +
                             $"at least {MinLeanRange:P0} is needed. Lean further next time. Leaning still moves the bubble.");

        // slope of off against moved (with its own offset, so a bubble that's a bit off at the middle anyway doesn't change it)
        var (_, slope) = LeastSquares(moved, off);
        if (Math.Abs(slope) > MaxHeadGain)
            return NotFitted($"Leaning left and right wasn't fitted: the result ({slope:0.0}) is too big to be right, " +
                             "maybe you looked away from the logo while leaning.");

        double before = off.Average(Math.Abs);
        double after = off.Zip(moved).Average(p => Math.Abs(p.First - slope * p.Second));
        double gainRounded = Math.Round(slope, 4);
        string Px(double fraction) => report.ScreenWidth > 0 ? $" ({fraction * report.ScreenWidth:0} px)" : "";
        return (model with { HeadXGain = gainRounded, HeadXRef = Math.Round(faceXRef, 4) },
                report with
                {
                    HeadFitted = true,
                    HeadXGain = gainRounded,
                    HeadXRef = Math.Round(faceXRef, 4),
                    LeanRange = Math.Round(range, 3),
                    LeanErrorBefore = Math.Round(before, 3),
                    LeanErrorAfter = Math.Round(after, 3),
                    HeadExplanation = $"Leaning left and right is corrected: your face moved {range:P0} of the camera image while you " +
                                      $"looked at the logo. Without the correction the bubble was on average {before:P1}{Px(before)} off " +
                                      $"while leaning, with it {after:P1}{Px(after)}.",
                });
    }
}
