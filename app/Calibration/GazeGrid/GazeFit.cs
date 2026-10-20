namespace app.Calibration.GazeGrid;

/// <summary>
/// Fits the gaze numbers (GazeParams in tracker/gaze_model.py) to the grid targets and the head step. Left/right is the
/// same model as estimate_gaze(): each eye's angle is asin((h - its baseline) / k_h), the two are averaged, and
/// x = center_x + gain_x * tan(angle). So what's measured here is what the bubble does with these numbers.
///
/// Each eye gets its own baseline: at the middle of the screen the eyes turn in a bit, so the left iris sits left of
/// center and the right one right of it (-0.14 and +0.13 in the first recording). k_h is picked from a few values,
/// with center_x and gain_x fitted for each (a straight line through tan(angle) against x), keeping the one that's
/// best on targets it wasn't fitted on. A bigger k_h makes the mapping more linear, which fit real eyes better than
/// the default 0.7. Looking down squeezes left/right, that's fitted too (x_squeeze).
///
/// It's one class in four files: this one (Fit, which runs the others, and the shared math), GazeFit.LeftRight.cs
/// (FitX), GazeFit.UpDown.cs (FitY) and GazeFit.Head.cs (FitHead, leaning and tilting). What it found is GazeFitReport.
/// </summary>
public static partial class GazeFit
{
    // bump when the fit changes, so calibrations fitted before get fitted again from their recording (App.FitOldCalibrations).
    // 1: left/right and leaning. 2: up/down from the lids. 3: the looking down squeeze, and the head step.
    // 4: looking down isn't closed eyes (GazeTargetSummary.LooksDown), the bottom row counts again.
    // 5: up/down can curve (y_arc, y_openness_sq), for the 20-point grid.
    // 6: the vertical iris offset comes from the eye corners. Older recordings measured it from the lids, they're fitted
    // from the lids only (y_iris 0, GazeTargetSummary.WithoutVertical)
    public const int Version = 6;

    // the defaults in gaze_model.py, to show how far off they were
    public const double DefaultK = 0.7;
    public static readonly double DefaultGainX = 0.5 / Math.Tan(15 * Math.PI / 180);
    // same as MAX_SIN in gaze_model.py
    public const double MaxSin = 0.95;

    public static readonly double[] KCandidates = [0.5, 0.6, 0.7, 0.85, 1.0, 1.25, 1.5, 2.0, 3.0];

    // a target needs this many frames with both eyes open to count
    public const int MinUsableFrames = 10;

    /// <summary>
    /// Everything that can be fitted from a grid run: left/right and up/down from the grid targets, then leaning and
    /// tilting from the head step (the targets with a Pose), or for older calibrations leaning from the single lean target
    /// (its frames in lean). null if there aren't enough targets.
    /// </summary>
    public static (GazeModel Model, GazeFitReport Report)? Fit(IReadOnlyList<GazeTargetSummary> targets, double screenWidth,
                                                              IReadOnlyList<FrameSample>? lean = null, double leanX = 0.5)
    {
        if (FitX(targets, screenWidth) is not { } fit) return null;
        fit = FitY(fit.Model, fit.Report, targets);
        fit = fit with { Report = fit.Report with { Version = Version } };

        // where the face was and how the head was tilted during the grid targets, leaning and tilting are measured from there
        var faceX = targets.Where(t => IsGrid(t) && t.FaceX is not null).Select(t => t.FaceX!.Median).ToList();
        var pitch = targets.Where(t => IsGrid(t) && t.Pitch is not null).Select(t => t.Pitch!.Median).ToList();
        if (faceX.Count == 0) return fit;
        double faceXRef = Stats.Percentile(faceX, 50);

        // the head step (calibrations since version 3), or the single lean dot before it
        if (targets.Any(t => t.Pose is not null) && pitch.Count > 0)
            return FitHead(fit.Model, fit.Report, targets, faceXRef, Stats.Percentile(pitch, 50));
        if (lean is null || lean.Count == 0) return fit;
        return FitHeadX(fit.Model, fit.Report, lean, leanX, faceXRef);
    }

    // the targets the left/right and up/down fit use: the grid targets and the fine-tune's dots, sitting normally, not the
    // old accuracy check or the head step
    private static bool IsGrid(GazeTargetSummary t) => !t.IsValidation && !t.MoveHead && t.Pose is null;

    /// <summary>tan of the left/right angle for a target, averaged over both eyes, the same as the tracker's (the t in gaze_model.py).</summary>
    private static double Tan(GazeModel model, GazeTargetSummary t)
    {
        double k = model.KH ?? DefaultK;
        return Math.Tan((Angle(t.LeftH!.Median, model.LeftHBaseline ?? 0, k) + Angle(t.RightH!.Median, model.RightHBaseline ?? 0, k)) / 2);
    }

    /// <summary>Where the fitted up/down puts a target, without the head corrections (BaseY in FitHead).</summary>
    private static double FittedY(GazeModel model, GazeTargetSummary t)
    {
        double open = t.Openness!.Median;
        // no vertical offset: fitted from the lids only (an older recording), y_iris is 0 then
        double iris = t.OffsetV is { } v ? (model.YIris ?? 0) * v.Median : 0;
        double y = model.YConst!.Value + model.YOpenness!.Value * open + iris;
        if (model.YArc is { } arc && t.LeftH is not null && t.RightH is not null) y += arc * Math.Pow(Tan(model, t), 2);
        if (model.YOpennessSq is { } sq) y += sq * open * open;
        return y;
    }

    /// <summary>
    /// Where the tracker puts the bubble (0-1 of the screen) with these gaze numbers, for both eyes open: the same math
    /// as estimate_gaze() / _screen_position() in gaze_model.py. For the fine-tune, which fits the gaze again and needs the
    /// bubble with the new numbers before the tracker has them. leftV / rightV: null for a recording without them (then
    /// y_iris is 0 anyway). faceX / pitch: where the face was and how the head was tilted, null leaves the head
    /// corrections out, like the tracker does in the head modes without them (--no-head-compensation).
    /// </summary>
    public static (double X, double Y) Predict(GazeModel m, double leftH, double rightH, double? leftV, double? rightV,
                                               double openness, double? faceX, double? pitch)
    {
        double k = m.KH ?? DefaultK;
        double t = Math.Tan((Angle(leftH, m.LeftHBaseline ?? 0, k) + Angle(rightH, m.RightHBaseline ?? 0, k)) / 2);
        double x = (m.CenterX ?? 0.5) + (m.GainX ?? DefaultGainX) * t + (m.XSqueeze ?? 0) * t * (openness - (m.OpennessRef ?? 0.3));

        double v = leftV is { } l && rightV is { } r ? (l + r) / 2 : 0;
        bool fitted = (m.YOpenness ?? 0) != 0 || (m.YIris ?? 0) != 0;
        double y;
        if (fitted)
        {
            y = (m.YConst ?? 0.5) + (m.YOpenness ?? 0) * openness + (m.YIris ?? 0) * v
                + (m.YArc ?? 0) * t * t + (m.YOpennessSq ?? 0) * openness * openness;
        }
        else
        {
            // the angle model the tracker falls back to without a fitted up/down
            double kv = m.KV ?? DefaultK;
            double angle = (Angle(leftV ?? 0, m.LeftVBaseline ?? 0, kv) + Angle(rightV ?? 0, m.RightVBaseline ?? 0, kv)) / 2;
            y = (m.CenterY ?? 0.5) + (m.GainY ?? DefaultGainY) * Math.Tan(angle);
        }

        if (faceX is { } fx)
        {
            double moved = fx - (m.HeadXRef ?? 0.5);
            x += (m.HeadXGain ?? 0) * moved + (m.HeadXShrink ?? 0) * t * Math.Abs(moved);
            if (fitted) y += (m.HeadYLean ?? 0) * Math.Abs(moved);
        }
        if (fitted && pitch is { } p) y += (m.HeadYPitch ?? 0) * (p - (m.PitchRef ?? 0));
        return (x, y);
    }

    /// <summary>Predict for a target, from its medians. null without both eyes' numbers or the lid openness.</summary>
    public static (double X, double Y)? Predict(GazeModel m, GazeTargetSummary t, bool headCorrections)
    {
        if (t.LeftH is null || t.RightH is null || t.Openness is null) return null;
        return Predict(m, t.LeftH.Median, t.RightH.Median, t.LeftV?.Median, t.RightV?.Median, t.Openness.Median,
                       headCorrections ? t.FaceX?.Median : null, headCorrections ? t.Pitch?.Median : null);
    }

    /// <summary>Predict for one frame. null without both eyes open.</summary>
    public static (double X, double Y)? Predict(GazeModel m, FrameSample f, bool headCorrections)
    {
        if (!f.FaceFound || f.Left is not { Closed: false, Covered: false } l || f.Right is not { Closed: false, Covered: false } r) return null;
        return Predict(m, l.OffsetH, r.OffsetH, l.OffsetV, r.OffsetV, (l.Openness + r.Openness) / 2,
                       headCorrections ? f.FaceX : null, headCorrections ? f.Pitch : null);
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
