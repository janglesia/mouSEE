namespace app.Calibration.GazeGrid;

// Left/right: k_h, the baselines, center_x, gain_x and the looking down squeeze, from the grid targets. See GazeFit.cs.
public static partial class GazeFit
{
    // the lid openness has to change at least this much over the targets to fit the looking down squeeze from it
    public const double MinOpennessRange = 0.03;

    /// <summary>null if there aren't enough targets to fit (at least 3, in at least 2 columns).</summary>
    public static (GazeModel Model, GazeFitReport Report)? FitX(IReadOnlyList<GazeTargetSummary> targets, double screenWidth)
    {
        var points = targets
            .Where(t => IsGrid(t) && t.UsableFrames >= MinUsableFrames && t.LeftH is not null && t.RightH is not null)
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
}
