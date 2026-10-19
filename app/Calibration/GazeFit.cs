namespace app.Calibration;

/// <summary>How well the fitted numbers put each nine-point target back where it was (left/right only for now).</summary>
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
    // where the middle of the face was in the camera image during the nine targets (0-1)
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
    // the head pitch during the nine targets, tilting is measured from there
    public double? PitchRef { get; init; }
    // on the head step's dots: how far off without and with the corrections (share of the screen height for y)
    public double? HeadYErrorBefore { get; init; }
    public double? HeadYErrorAfter { get; init; }
    public List<GazeFitTarget> HeadTargets { get; init; } = [];

    // ---- up/down, from the nine targets' lid openness and vertical iris offset
    public bool YFitted { get; init; }
    public double? YConst { get; init; }
    public double? YOpenness { get; init; }
    public double? YIris { get; init; }
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

/// <summary>
/// Fits the left/right gaze numbers (GazeParams in tracker/gaze_model.py) to the nine-point targets. The same model as
/// estimate_gaze(): each eye's angle is asin((h - its baseline) / k_h), the two are averaged, and
/// x = center_x + gain_x * tan(angle). So what's measured here is what the bubble does with these numbers.
///
/// Each eye gets its own baseline: at the middle of the screen the eyes turn in a bit, so the left iris sits left of
/// center and the right one right of it (-0.14 and +0.13 in the first recording). k_h is picked from a few values,
/// with center_x and gain_x fitted for each (a straight line through tan(angle) against x), keeping the one that's
/// best on targets it wasn't fitted on. A bigger k_h makes the mapping more linear, which fit real eyes better than
/// the default 0.7. Looking down squeezes left/right, that's fitted too (x_squeeze). Up/down is FitY, leaning and tilting FitHead.
/// </summary>
public static class GazeFit
{
    // the defaults in gaze_model.py, to show how far off they were
    // bump when the fit changes, so calibrations fitted before get fitted again from their recording (App.FitOldCalibrations).
    // 1: left/right and leaning. 2: up/down from the lids. 3: the looking down squeeze, and the head step
    public const int Version = 3;

    public const double DefaultK = 0.7;
    public static readonly double DefaultGainX = 0.5 / Math.Tan(15 * Math.PI / 180);
    // same as MAX_SIN in gaze_model.py
    public const double MaxSin = 0.95;

    public static readonly double[] KCandidates = [0.5, 0.6, 0.7, 0.85, 1.0, 1.25, 1.5, 2.0, 3.0];

    // a target needs this many frames with both eyes open to count
    public const int MinUsableFrames = 10;

    /// <summary>
    /// Everything that can be fitted from a nine-point run: left/right and up/down from the nine targets, then leaning and
    /// tilting from the head step (the targets with a Pose), or for older calibrations leaning from the single lean target
    /// (its frames in lean). null if there aren't enough targets.
    /// </summary>
    public static (GazeModel Model, GazeFitReport Report)? Fit(IReadOnlyList<GazeTargetSummary> targets, double screenWidth,
                                                              IReadOnlyList<FrameSample>? lean = null, double leanX = 0.5)
    {
        if (FitX(targets, screenWidth) is not { } fit) return null;
        fit = FitY(fit.Model, fit.Report, targets);
        fit = fit with { Report = fit.Report with { Version = Version } };

        // where the face was and how the head was tilted during the nine targets, leaning and tilting are measured from there
        var faceX = targets.Where(t => IsNine(t) && t.FaceX is not null).Select(t => t.FaceX!.Median).ToList();
        var pitch = targets.Where(t => IsNine(t) && t.Pitch is not null).Select(t => t.Pitch!.Median).ToList();
        if (faceX.Count == 0) return fit;
        double faceXRef = Stats.Percentile(faceX, 50);

        // the head step (calibrations since version 3), or the single lean dot before it
        if (targets.Any(t => t.Pose is not null) && pitch.Count > 0)
            return FitHead(fit.Model, fit.Report, targets, faceXRef, Stats.Percentile(pitch, 50));
        if (lean is null || lean.Count == 0) return fit;
        return FitHeadX(fit.Model, fit.Report, lean, leanX, faceXRef);
    }

    // the targets the left/right and up/down fit use: the nine sitting normally, not the accuracy check or the head step
    private static bool IsNine(GazeTargetSummary t) => !t.IsValidation && !t.MoveHead && t.Pose is null;

    // the lid openness has to change at least this much over the targets to fit the looking down squeeze from it
    public const double MinOpennessRange = 0.03;

    /// <summary>null if there aren't enough targets to fit (at least 3, in at least 2 columns).</summary>
    public static (GazeModel Model, GazeFitReport Report)? FitX(IReadOnlyList<GazeTargetSummary> targets, double screenWidth)
    {
        var points = targets
            .Where(t => IsNine(t) && t.UsableFrames >= MinUsableFrames && t.LeftH is not null && t.RightH is not null)
            .Select(t => (t.Index, t.X, L: t.LeftH!.Median, R: t.RightH!.Median, Open: t.Openness?.Median))
            .ToList();
        if (points.Count < 3 || points.Select(p => Math.Round(p.X, 2)).Distinct().Count() < 2) return null;

        // baselines: where each iris sits when looking at the middle column, or the average over everything without one
        var middle = points.Where(p => Math.Abs(p.X - 0.5) < 0.05).ToList();
        var basis = middle.Count > 0 ? middle : points;
        double bL = basis.Average(p => p.L), bR = basis.Average(p => p.R);

        // looking down squeezes left/right (the iris moves less sideways). fitted from the lid openness when every target
        // has one and it changes enough over them (it does between the rows), as a third number next to center and gain
        bool squeeze = points.Count >= 5 && points.All(p => p.Open is not null)
                       && points.Max(p => p.Open!.Value) - points.Min(p => p.Open!.Value) >= MinOpennessRange;
        double openRef = squeeze ? Stats.Percentile(points.Select(p => p.Open!.Value).ToList(), 50) : 0;

        double Tan(double k, double l, double r) => Math.Tan((Angle(l, bL, k) + Angle(r, bR, k)) / 2);

        // center, gain, squeeze
        double[] Coefficients(double k, IEnumerable<(int Index, double X, double L, double R, double? Open)> pts)
        {
            var list = pts.ToList();
            var t = list.Select(p => Tan(k, p.L, p.R)).ToList();
            if (!squeeze)
            {
                var (c, g) = LeastSquares(t, list.Select(p => p.X).ToList());
                return [c, g, 0];
            }
            return LeastSquares3(t, list.Select((p, i) => t[i] * (p.Open!.Value - openRef)).ToList(), list.Select(p => p.X).ToList());
        }
        double Predict(double k, double[] c, (int Index, double X, double L, double R, double? Open) p)
        {
            double t = Tan(k, p.L, p.R);
            return c[0] + c[1] * t + (squeeze ? c[2] * t * (p.Open!.Value - openRef) : 0);
        }

        double LeaveOneOut(double k) => points.Average(p =>
            Math.Abs(Predict(k, Coefficients(k, points.Where(q => q.Index != p.Index)), p) - p.X));

        double best = double.MaxValue, bestK = DefaultK;
        foreach (double k in KCandidates)
        {
            double loo = LeaveOneOut(k);
            if (loo < best - 1e-9)
            {
                best = loo;
                bestK = k;
            }
        }

        var coef = Coefficients(bestK, points);
        var fitted = points.Select(p =>
        {
            double predicted = Predict(bestK, coef, p);
            return new GazeFitTarget(p.Index, p.X, Math.Round(predicted, 3), Math.Round(Math.Abs(predicted - p.X), 3));
        }).ToList();
        double defaultError = points.Average(p =>
            Math.Abs(0.5 + DefaultGainX * Math.Tan((Angle(p.L, 0, DefaultK) + Angle(p.R, 0, DefaultK)) / 2) - p.X));

        double mean = fitted.Average(t => t.Error), max = fitted.Max(t => t.Error);
        var model = new GazeModel
        {
            KH = bestK,
            LeftHBaseline = Math.Round(bL, 4),
            RightHBaseline = Math.Round(bR, 4),
            CenterX = Math.Round(coef[0], 4),
            GainX = Math.Round(coef[1], 4),
            XSqueeze = squeeze ? Math.Round(coef[2], 4) : null,
            OpennessRef = squeeze ? Math.Round(openRef, 4) : null,
        };
        string Px(double fraction) => screenWidth > 0 ? $" (about {fraction * screenWidth:0} px)" : "";
        var report = new GazeFitReport
        {
            KH = bestK,
            LeftHBaseline = model.LeftHBaseline.Value,
            RightHBaseline = model.RightHBaseline.Value,
            CenterX = model.CenterX.Value,
            GainX = model.GainX.Value,
            XSqueeze = model.XSqueeze,
            OpennessRef = model.OpennessRef,
            MeanError = Math.Round(mean, 3),
            MaxError = Math.Round(max, 3),
            LeaveOneOutError = Math.Round(best, 3),
            DefaultMeanError = Math.Round(defaultError, 3),
            ScreenWidth = screenWidth,
            Targets = fitted,
            Explanation = $"Left/right fitted to {points.Count} targets{(squeeze ? ", with how looking down squeezes it" : "")}. " +
                          $"The bubble now lands on average {mean:P1} of the screen width{Px(mean)} " +
                          $"from where you looked, {max:P0} at worst, and about {best:P1}{Px(best)} on places it wasn't fitted on. " +
                          $"With the default numbers it was {defaultError:P0} off.",
        };
        return (model, report);
    }

    public static readonly double DefaultGainY = 0.5 / Math.Tan(10 * Math.PI / 180);

    /// <summary>
    /// Fits up/down (y_const, y_openness, y_iris in gaze_model.py) to the nine targets: a straight line through the lid
    /// openness and the vertical iris offset against where the targets were. The iris itself barely moves up and down in
    /// the tracker's numbers (it's measured from the middle of the lids, which follow the eye), the lids do: more open
    /// looking up, less looking down. Head pitch would fit sitting still a bit better but breaks when leaning, so it's left
    /// out. Needs at least 4 targets in at least 2 rows, otherwise up/down stays at the defaults.
    /// </summary>
    public static (GazeModel Model, GazeFitReport Report) FitY(GazeModel model, GazeFitReport report, IReadOnlyList<GazeTargetSummary> targets)
    {
        var points = targets
            .Where(t => IsNine(t) && t.UsableFrames >= MinUsableFrames && t.Openness is not null && t.OffsetV is not null)
            .Select(t => (t.Index, t.Y, Open: t.Openness!.Median, V: t.OffsetV!.Median, t.LeftV, t.RightV))
            .ToList();
        if (points.Count < 4 || points.Select(p => Math.Round(p.Y, 2)).Distinct().Count() < 2)
            return (model, report with
            {
                YFitted = false,
                YExplanation = "Up/down wasn't fitted: it needs at least 4 targets in at least 2 rows with both eyes open, " +
                               "and lid openness measured (calibrations from before it was recorded don't have it). It uses the default numbers.",
            });

        double[] Coefficients(IEnumerable<(int Index, double Y, double Open, double V, Spread? LeftV, Spread? RightV)> pts)
        {
            var list = pts.ToList();
            return LeastSquares3(list.Select(p => p.Open).ToList(), list.Select(p => p.V).ToList(), list.Select(p => p.Y).ToList());
        }
        double Predict(double[] c, double open, double v) => c[0] + c[1] * open + c[2] * v;

        var coef = Coefficients(points);
        var fitted = points.Select(p =>
        {
            double predicted = Predict(coef, p.Open, p.V);
            return new GazeFitTarget(p.Index, p.Y, Math.Round(predicted, 3), Math.Round(Math.Abs(predicted - p.Y), 3));
        }).ToList();
        double loo = points.Average(p =>
        {
            var c = Coefficients(points.Where(q => q.Index != p.Index));
            return Math.Abs(Predict(c, p.Open, p.V) - p.Y);
        });
        // the default angle model on the same targets, for comparison
        var withEyes = points.Where(p => p.LeftV is not null && p.RightV is not null).ToList();
        double? defaultError = withEyes.Count == 0 ? null : withEyes.Average(p =>
            Math.Abs(0.5 + DefaultGainY * Math.Tan((Angle(p.LeftV!.Median, 0, DefaultK) + Angle(p.RightV!.Median, 0, DefaultK)) / 2) - p.Y));

        double mean = fitted.Average(t => t.Error), max = fitted.Max(t => t.Error);
        var updated = model with
        {
            YConst = Math.Round(coef[0], 4),
            YOpenness = Math.Round(coef[1], 4),
            YIris = Math.Round(coef[2], 4),
        };
        string before = defaultError is null ? "" : $" With the default numbers it was {defaultError:P0} off.";
        return (updated, report with
        {
            YFitted = true,
            YConst = updated.YConst,
            YOpenness = updated.YOpenness,
            YIris = updated.YIris,
            YMeanError = Math.Round(mean, 3),
            YMaxError = Math.Round(max, 3),
            YLeaveOneOutError = Math.Round(loo, 3),
            YDefaultMeanError = defaultError is null ? null : Math.Round(defaultError.Value, 3),
            YTargets = fitted,
            YExplanation = $"Up/down fitted to {points.Count} targets from how open your lids are and where the iris is: on average " +
                           $"{mean:P1} of the screen height off, {max:P0} at worst, about {loo:P1} on places it wasn't fitted on.{before}",
        });
    }

    // y = c0 + c1 * a + c2 * b, least squares. falls back to just the average for degenerate input
    private static double[] LeastSquares3(IReadOnlyList<double> a, IReadOnlyList<double> b, IReadOnlyList<double> y) =>
        Solve(a.Select((v, i) => new[] { 1, v, b[i] }).ToList(), y) ?? [y.Average(), 0, 0];

    // least squares y = rows * c (no extra constant, put a 1 in the rows for one), through the normal equations with
    // Gauss-Jordan. Only for a handful of numbers. null if they can't be told apart (e.g. a column that's all 0)
    private static double[]? Solve(IReadOnlyList<double[]> rows, IReadOnlyList<double> y)
    {
        int n = rows[0].Length;
        double[,] m = new double[n, n + 1];
        for (int i = 0; i < rows.Count; i++)
        {
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++) m[r, c] += rows[i][r] * rows[i][c];
                m[r, n] += rows[i][r] * y[i];
            }
        }
        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            for (int r = col + 1; r < n; r++)
                if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col])) pivot = r;
            if (Math.Abs(m[pivot, col]) < 1e-12) return null;
            for (int c = 0; c <= n; c++) (m[col, c], m[pivot, c]) = (m[pivot, c], m[col, c]);
            for (int r = 0; r < n; r++)
            {
                if (r == col) continue;
                double factor = m[r, col] / m[col, col];
                for (int c = 0; c <= n; c++) m[r, c] -= factor * m[col, c];
            }
        }
        return Enumerable.Range(0, n).Select(i => m[i, n] / m[i, i]).ToArray();
    }

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
    /// tilted during the nine targets.
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
        bool yFitted = model.YOpenness is not null && dots.All(t => t.Openness is not null && t.OffsetV is not null);
        double BaseY(GazeTargetSummary t) => model.YConst!.Value + model.YOpenness!.Value * t.Openness!.Median + model.YIris!.Value * t.OffsetV!.Median;
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
    /// slope of "how far off the bubble was" against "how far the face moved from where it was during the nine targets".
    /// lean: the lean target's frames. faceXRef: where the middle of the face was during the nine targets.
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

    // one eye's angle in radians, like eye_angle() in gaze_model.py
    private static double Angle(double h, double baseline, double k) => Math.Asin(Math.Clamp((h - baseline) / k, -MaxSin, MaxSin));

    // y = c + g * x
    private static (double C, double G) LeastSquares(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        double mx = x.Average(), my = y.Average();
        double sxx = x.Sum(v => (v - mx) * (v - mx));
        if (sxx < 1e-12) return (my, 0);
        double sxy = x.Zip(y).Sum(p => (p.First - mx) * (p.Second - my));
        double g = sxy / sxx;
        return (my - g * mx, g);
    }
}
