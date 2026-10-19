namespace app.Bubble;

/// <summary>
/// A last correction on the bubble's position, from the fine-tune. Per direction a shift, stretch and tilt, plus curves
/// around the middle of the screen (u = x - 0.5, v = y - 0.5):
///   x' = X0 + X1 * x + X2 * y + Xuv * u * v + Xuu * u² + Xvv * v²
///   y' = Y0 + Y1 * x + Y2 * y + Yuv * u * v + Yuu * u² + Yvv * v²
/// Xuv: left/right is wider at the top than the bottom (or the other way). Yuu: the sides land higher or lower than the
/// middle, an arc. Yvv: up/down isn't even, stretched more at one end. Saved with the calibration it was made for. A
/// direction it didn't help stays as it is (X0 0, X1 1, the rest 0). Fine-tunes from before the curves read with them 0.
/// </summary>
public sealed record GazeCorrection(double X0, double X1, double X2, double Y0, double Y1, double Y2,
                                    double Xuv = 0, double Xuu = 0, double Xvv = 0,
                                    double Yuv = 0, double Yuu = 0, double Yvv = 0)
{
    public static readonly GazeCorrection None = new(0, 1, 0, 0, 0, 1);

    public (double X, double Y) Apply(double x, double y)
    {
        double u = x - 0.5, v = y - 0.5;
        return (X0 + X1 * x + X2 * y + Xuv * u * v + Xuu * u * u + Xvv * v * v,
                Y0 + Y1 * x + Y2 * y + Yuv * u * v + Yuu * u * u + Yvv * v * v);
    }
}

/// <summary>What the fine-tune found, saved with the calibration (CalibrationProfile.FineTuneReport).</summary>
public sealed record FineTuneReport
{
    public DateTimeOffset MadeAt { get; init; }
    public int Points { get; init; }
    // how far off the bubble was without the fine-tune, and with it on dots it wasn't fitted on (share of the screen)
    public double ErrorXBefore { get; init; }
    public double ErrorXAfter { get; init; }
    public double ErrorYBefore { get; init; }
    public double ErrorYAfter { get; init; }
    public bool CorrectsX { get; init; }
    public bool CorrectsY { get; init; }
    // which shape of correction was picked per direction (FineTune.XShapes / YShapes), null when not corrected
    public string? ShapeX { get; init; }
    public string? ShapeY { get; init; }
    // the head mode it was made in (docs/bubble/HEAD-MODES.md): cancelling or not changes where the bubble lands a little
    public HeadModes.HeadMode HeadMode { get; init; }
    public string Explanation { get; init; } = "";
    // every dot and where the bubble landed on it, to fit it again later
    public List<FineTunePoint> Dots { get; init; } = [];
}

/// <summary>A fine-tune point: where the dot was and where the bubble landed (median, without any fine-tune).</summary>
public sealed record FineTunePoint(double TargetX, double TargetY, double BubbleX, double BubbleY);

/// <summary>
/// The fine-tune: dots around the screen, and for each where the bubble landed against where it should have been. The
/// calibration learns from the eyes, this learns from where the bubble ends up, so it corrects whatever the calibration
/// still gets wrong (and how you sit right now).
///
/// A flat correction (shift, stretch, tilt) took up/down from 24% to 11% and from 11% to 5.4% of the screen height on the
/// first two accuracy tests, on dots it wasn't fitted on. But with the camera under the screen looking up, the first real
/// fine-tune showed curves a flat one can't fix: the sides of the screen landed higher than the middle (an arc), and
/// left/right was stretched at the top (overshooting into the top corners) and squeezed at the bottom (never reaching the
/// bottom corners). On those dots the curves took left/right from 4.6% to 3.9% and up/down from 5.8% to 3.2%. Which curve
/// helps differs from one session to the next, so each direction tries a few shapes and keeps the one that does best on
/// dots it wasn't fitted on, and is only corrected at all when that helps.
/// </summary>
public static class FineTune
{
    public const int SettleMs = 800;
    public const int CaptureMs = 1500;
    // fewer dots with a bubble than this and there's not enough to fit the curves and check them
    public const int MinPoints = 8;
    // a direction is only corrected when the correction is at least this much better on dots it wasn't fitted on
    public const double MinImprovement = 0.9;
    // a shape with more numbers is only picked when it's at least this much better than the simpler one, more numbers
    // can always follow a few dots more closely
    public const double MinImprovementForMore = 0.95;
    // a correction that stretches by more than this isn't a last adjustment, the calibration doesn't fit
    public const double MaxStretch = 2.5;
    // and curves bigger than this move a corner by more than half the screen
    public const double MaxCurve = 2;

    // the numbers a shape can use: 1, x, y, u*v, u², v²
    public enum Term { One, X, Y, UV, UU, VV }

    public sealed record Shape(string Name, string Says, Term[] Terms);

    /// <summary>The shapes tried for left/right, simplest first.</summary>
    public static IReadOnlyList<Shape> XShapes { get; } =
    [
        new("flat", "shifted and stretched", [Term.One, Term.X, Term.Y]),
        new("wider-at-one-end", "wider at the top or bottom than the other", [Term.One, Term.X, Term.UV]),
        new("flat-and-wider-at-one-end", "shifted, stretched, and wider at the top or bottom", [Term.One, Term.X, Term.Y, Term.UV]),
    ];

    /// <summary>The shapes tried for up/down, simplest first.</summary>
    public static IReadOnlyList<Shape> YShapes { get; } =
    [
        new("flat", "shifted and stretched", [Term.One, Term.X, Term.Y]),
        new("arc", "with the sides landing higher or lower than the middle", [Term.One, Term.X, Term.Y, Term.UU]),
        new("arc-and-uneven", "with the sides higher or lower than the middle, and stretched more at one end", [Term.One, Term.X, Term.Y, Term.UU, Term.VV]),
    ];

    /// <summary>5 columns by 4 rows, close to the corners (the curves show most there), snake order.</summary>
    public static IReadOnlyList<(double X, double Y)> Dots { get; } = BuildDots();

    private static List<(double X, double Y)> BuildDots()
    {
        double[] xs = [0.08, 0.29, 0.5, 0.71, 0.92];
        double[] ys = [0.1, 0.37, 0.63, 0.9];
        var list = new List<(double, double)>();
        for (int r = 0; r < ys.Length; r++)
            for (int c = 0; c < xs.Length; c++)
                list.Add((r % 2 == 0 ? xs[c] : xs[^(c + 1)], ys[r]));
        return list;
    }

    /// <summary>The correction and how it did. Each direction on its own: every shape is fitted without one dot and
    /// measured on that dot, for every dot. The best shape is kept, and only if it's clearly better than no correction.</summary>
    public static (GazeCorrection Correction, FineTuneReport Report) Fit(IReadOnlyList<FineTunePoint> points)
    {
        if (points.Count < MinPoints)
            return (GazeCorrection.None, new FineTuneReport
            {
                MadeAt = DateTimeOffset.Now, Points = points.Count, Dots = points.ToList(),
                Explanation = $"Not enough dots with a bubble ({points.Count}, at least {MinPoints} needed). Keep both eyes open and look at each dot.",
            });

        double beforeX = points.Average(p => Math.Abs(p.BubbleX - p.TargetX));
        double beforeY = points.Average(p => Math.Abs(p.BubbleY - p.TargetY));
        var x = Best(points, XShapes, p => p.TargetX, beforeX);
        var y = Best(points, YShapes, p => p.TargetY, beforeY);

        double C(Fitted? f, Term t, double none) => f is null ? none : Math.Round(f.Value.Coefficient(t), 4);
        var correction = new GazeCorrection(
            C(x, Term.One, 0), C(x, Term.X, 1), C(x, Term.Y, 0),
            C(y, Term.One, 0), C(y, Term.X, 0), C(y, Term.Y, 1),
            C(x, Term.UV, 0), C(x, Term.UU, 0), C(x, Term.VV, 0),
            C(y, Term.UV, 0), C(y, Term.UU, 0), C(y, Term.VV, 0));

        string Part(string what, Fitted? f, double before) => f is { } used
            ? $"{what} {used.Shape.Says}, from {before:P1} to about {used.LeaveOneOut:P1} off"
            : $"{what} stays as it is ({before:P1} off, a correction didn't help)";
        return (correction, new FineTuneReport
        {
            MadeAt = DateTimeOffset.Now,
            Points = points.Count,
            Dots = points.ToList(),
            ErrorXBefore = Math.Round(beforeX, 3),
            ErrorXAfter = Math.Round(x?.LeaveOneOut ?? beforeX, 3),
            ErrorYBefore = Math.Round(beforeY, 3),
            ErrorYAfter = Math.Round(y?.LeaveOneOut ?? beforeY, 3),
            CorrectsX = x is not null,
            CorrectsY = y is not null,
            ShapeX = x?.Shape.Name,
            ShapeY = y?.Shape.Name,
            Explanation = x is null && y is null
                ? $"The bubble was {beforeX:P1} (left/right) and {beforeY:P1} (up/down) off, and a correction didn't make it better, so nothing changes."
                : $"Fine-tuned from {points.Count} dots: {Part("left/right", x, beforeX)}; {Part("up/down", y, beforeY)}.",
        });
    }

    private readonly record struct Fitted(Shape Shape, double[] Coefficients, double LeaveOneOut)
    {
        public double Coefficient(Term t) => Array.IndexOf(Shape.Terms, t) is var i and >= 0 ? Coefficients[i] : 0;
    }

    // the shape that does best on left-out dots (a bigger one only when clearly better), null if none beats no correction
    private static Fitted? Best(IReadOnlyList<FineTunePoint> points, IReadOnlyList<Shape> shapes, Func<FineTunePoint, double> target, double before)
    {
        Fitted? best = null;
        foreach (var shape in shapes)
        {
            if (Fit(points, shape, target) is not { } f || !Sensible(f)) continue;
            if (best is null || f.LeaveOneOut < best.Value.LeaveOneOut * MinImprovementForMore) best = f;
        }
        return best is { } b && b.LeaveOneOut < before * MinImprovement ? b : null;
    }

    private static bool Sensible(Fitted f) => f.Shape.Terms.Select((t, i) => (t, c: Math.Abs(f.Coefficients[i])))
        .All(p => p.t switch { Term.One => true, Term.X or Term.Y => p.c <= MaxStretch, _ => p.c <= MaxCurve });

    private static double Value(Term t, FineTunePoint p)
    {
        double u = p.BubbleX - 0.5, v = p.BubbleY - 0.5;
        return t switch { Term.One => 1, Term.X => p.BubbleX, Term.Y => p.BubbleY, Term.UV => u * v, Term.UU => u * u, _ => v * v };
    }

    // one shape for one direction, and its average error on left-out dots
    private static Fitted? Fit(IReadOnlyList<FineTunePoint> points, Shape shape, Func<FineTunePoint, double> target)
    {
        double[]? Solve(IEnumerable<FineTunePoint> pts)
        {
            var list = pts.ToList();
            return LeastSquares(list.Select(p => shape.Terms.Select(t => Value(t, p)).ToArray()).ToList(), list.Select(target).ToList());
        }
        if (Solve(points) is not { } all) return null;
        double loo = 0;
        for (int i = 0; i < points.Count; i++)
        {
            if (Solve(points.Where((_, j) => j != i)) is not { } c) return null;
            loo += Math.Abs(shape.Terms.Select((t, k) => c[k] * Value(t, points[i])).Sum() - target(points[i]));
        }
        return new Fitted(shape, all, loo / points.Count);
    }

    // least squares through the normal equations, null if the dots can't tell the numbers apart
    private static double[]? LeastSquares(IReadOnlyList<double[]> rows, IReadOnlyList<double> y)
    {
        int n = rows[0].Length;
        if (rows.Count <= n) return null;
        var m = new double[n, n + 1];
        for (int i = 0; i < rows.Count; i++)
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++) m[r, c] += rows[i][r] * rows[i][c];
                m[r, n] += rows[i][r] * y[i];
            }
        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            for (int r = col + 1; r < n; r++) if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col])) pivot = r;
            if (Math.Abs(m[pivot, col]) < 1e-12) return null;
            for (int c = 0; c <= n; c++) (m[col, c], m[pivot, c]) = (m[pivot, c], m[col, c]);
            for (int r = 0; r < n; r++)
            {
                if (r == col) continue;
                double f = m[r, col] / m[col, col];
                for (int c = 0; c <= n; c++) m[r, c] -= f * m[col, c];
            }
        }
        var result = new double[n];
        for (int i = 0; i < n; i++) result[i] = m[i, n] / m[i, i];
        return result;
    }

    /// <summary>Where the bubble goes: the tracker's position, the fine-tune on it, then the re-center's shift.</summary>
    public static (double X, double Y) Bubble(double x, double y, GazeCorrection? correction, GazeOffset offset)
    {
        var (cx, cy) = (correction ?? GazeCorrection.None).Apply(x, y);
        return (cx - offset.X, cy - offset.Y);
    }

    /// <summary>The median bubble position of a dot's frames (without any fine-tune), null without enough of them.</summary>
    public static FineTunePoint? Point(double targetX, double targetY, IReadOnlyList<FrameSample> frames, int minFrames = 15)
    {
        var bubble = frames.Where(f => f.EstimateX is not null && f.EstimateY is not null).ToList();
        if (bubble.Count < minFrames) return null;
        return new FineTunePoint(targetX, targetY,
            Stats.Percentile(bubble.Select(f => f.EstimateX!.Value).ToList(), 50),
            Stats.Percentile(bubble.Select(f => f.EstimateY!.Value).ToList(), 50));
    }
}
