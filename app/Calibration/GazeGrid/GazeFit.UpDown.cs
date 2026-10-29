namespace app.Calibration.GazeGrid;

// Up/down: from the lids and the vertical iris offset, with the curves when they help. See GazeFit.cs.
public static partial class GazeFit
{
    public static readonly double DefaultGainY = 0.5 / Math.Tan(10 * Math.PI / 180);

    /// <summary>The shapes up/down can take, simplest first. Each adds to the one before, see FitY.</summary>
    public static readonly (string Name, string Says, int Terms)[] YShapes =
    [
        ("flat", "a straight line", 3),
        ("arc", "with the sides of the screen reading higher or lower than the middle", 4),
        ("arc-and-uneven", "with the sides reading higher or lower, and stretched more at one end than the other", 5),
    ];

    // a shape with more numbers is only picked when it's at least this much better on targets it wasn't fitted on,
    // more numbers can always follow a few targets more closely
    public const double MinImprovementForMore = 0.95;
    // and it needs this many more targets than numbers, so leaving one out still leaves enough to fit
    public const int SpareTargets = 3;

    /// <summary>
    /// Fits up/down (y_const, y_openness, y_iris, y_arc, y_openness_sq in gaze_model.py) to the grid's targets. y is a
    /// line through the lid openness (more open looking up, less looking down) and the vertical iris offset (where the
    /// iris is above or below the line between the eye corners), and when it helps two curves on top: the arc
    /// (+ y_arc * tan(left/right angle)², the sides of the screen reading higher or lower than the middle, which a camera
    /// under the screen looking up showed on every fine-tune) and uneven (+ y_openness_sq * openness², stretched more at
    /// one end). Each shape is fitted without one target and checked on it, for every target, and the best is kept (a
    /// bigger one only when clearly better). Head pitch would fit sitting still a bit better but breaks when leaning, so
    /// it's left out. Targets without the vertical offset (recorded when it was measured from the lids, see
    /// GazeTargetSummary.WithoutVertical) get fitted from the lids only, y_iris 0. Needs at least 4 targets in at least
    /// 2 rows, otherwise up/down stays at the defaults. model: with the left/right numbers already fitted, the arc needs
    /// the left/right angle.
    /// </summary>
    public static (GazeModel Model, GazeFitReport Report) FitY(GazeModel model, GazeFitReport report, IReadOnlyList<GazeTargetSummary> targets)
    {
        var points = targets
            .Where(t => IsGrid(t) && t.UsableFrames >= MinUsableFrames && t.Openness is not null)
            .ToList();
        if (points.Count < 4 || points.Select(p => Math.Round(p.Y, 2)).Distinct().Count() < 2)
            return (model, report with
            {
                YFitted = false,
                YExplanation = "Up/down wasn't fitted: it needs at least 4 targets in at least 2 rows with both eyes open, " +
                               "and lid openness measured (calibrations from before it was recorded don't have it). It uses the default numbers.",
            });

        // the arc needs the left/right angle of both eyes
        bool haveAngle = points.All(t => t.LeftH is not null && t.RightH is not null);
        // the vertical iris offset, unless it's missing (lids only, y_iris 0): then every shape has one number less
        bool useIris = points.All(t => t.OffsetV is not null);
        int baseTerms = useIris ? 3 : 2;
        double[] Row(GazeTargetSummary t, int terms)
        {
            double open = t.Openness!.Median;
            var row = new List<double> { 1, open };
            if (useIris) row.Add(t.OffsetV!.Median);
            if (terms >= baseTerms + 1) row.Add(Math.Pow(Tan(model, t), 2));
            if (terms >= baseTerms + 2) row.Add(open * open);
            return row.ToArray();
        }
        int Terms((string Name, string Says, int Terms) shape) => shape.Terms - (useIris ? 0 : 1);
        double[] Coefficients(IEnumerable<GazeTargetSummary> pts, int terms)
        {
            var list = pts.ToList();
            return Solve(list.Select(t => Row(t, terms)).ToList(), list.Select(t => t.Y).ToList())
                   ?? [list.Average(t => t.Y), .. new double[terms - 1]];
        }
        double Predict(double[] c, GazeTargetSummary t) => Row(t, c.Length).Select((v, i) => v * c[i]).Sum();
        double LeaveOneOut(int terms) => points.Average(t => Math.Abs(Predict(Coefficients(points.Where(q => q.Index != t.Index), terms), t) - t.Y));

        // the shape: flat, then a curve only when there are enough targets for it and it's clearly better
        var shape = YShapes[0];
        double loo = LeaveOneOut(Terms(shape));
        foreach (var candidate in YShapes.Skip(1))
        {
            if (!haveAngle || points.Count < Terms(candidate) + SpareTargets) break;
            double candidateLoo = LeaveOneOut(Terms(candidate));
            if (candidateLoo < loo * MinImprovementForMore) (shape, loo) = (candidate, candidateLoo);
        }

        var coef = Coefficients(points, Terms(shape));
        var fitted = points.Select(t =>
        {
            double predicted = Predict(coef, t);
            return new GazeFitTarget(t.Index, t.Y, Math.Round(predicted, 3), Math.Round(Math.Abs(predicted - t.Y), 3));
        }).ToList();
        // the default angle model on the same targets, for comparison
        var withEyes = points.Where(p => p.LeftV is not null && p.RightV is not null).ToList();
        double? defaultError = withEyes.Count == 0 ? null : withEyes.Average(p =>
            Math.Abs(0.5 + DefaultGainY * Math.Tan((Angle(p.LeftV!.Median, 0, DefaultK) + Angle(p.RightV!.Median, 0, DefaultK)) / 2) - p.Y));

        double mean = fitted.Average(t => t.Error), max = fitted.Max(t => t.Error);
        var updated = model with
        {
            YConst = Math.Round(coef[0], 4),
            YOpenness = Math.Round(coef[1], 4),
            YIris = useIris ? Math.Round(coef[2], 4) : 0,
            // the curves come after the line's numbers
            YArc = coef.Length > baseTerms ? Math.Round(coef[baseTerms], 4) : null,
            YOpennessSq = coef.Length > baseTerms + 1 ? Math.Round(coef[baseTerms + 1], 4) : null,
        };
        string before = defaultError is null ? "" : $" With the default numbers it was {defaultError:P0} off.";
        string from = useIris
            ? "how open your lids are and where the iris is"
            : "how open your lids are only (this calibration was recorded before the iris was measured from the eye corners, " +
              "calibrating again uses it too)";
        return (updated, report with
        {
            YFitted = true,
            YConst = updated.YConst,
            YOpenness = updated.YOpenness,
            YIris = updated.YIris,
            YArc = updated.YArc,
            YOpennessSq = updated.YOpennessSq,
            YShape = shape.Name,
            YMeanError = Math.Round(mean, 3),
            YMaxError = Math.Round(max, 3),
            YLeaveOneOutError = Math.Round(loo, 3),
            YDefaultMeanError = defaultError is null ? null : Math.Round(defaultError.Value, 3),
            YTargets = fitted,
            YExplanation = $"Up/down fitted to {points.Count} targets from {from}, {shape.Says}: on average " +
                           $"{mean:P1} of the screen height off, {max:P0} at worst, about {loo:P1} on places it wasn't fitted on.{before}",
        });
    }
}
