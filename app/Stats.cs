namespace app;

// Small statistics helpers the whole app uses: percentiles, and Spread, how a number was spread over a recording.

/// <summary>How a number was spread over a recording. Percentiles, so one glitched frame doesn't show up as the extreme.</summary>
public sealed record Spread(double P1, double P5, double Median, double P95, double P99)
{
    /// <summary>null for fewer than 3 values, a spread of 1 or 2 numbers says nothing.</summary>
    public static Spread? Of(IEnumerable<double?> values)
    {
        var list = values.OfType<double>().Where(double.IsFinite).ToList();
        if (list.Count < 3) return null;
        double P(double p) => Math.Round(Stats.Percentile(list, p), 3);
        return new Spread(P(1), P(5), P(50), P(95), P(99));
    }
}

public static class Stats
{
    /// <summary>Percentile with linear interpolation between the closest values (like numpy's default). p is 0-100.</summary>
    public static double Percentile(IReadOnlyCollection<double> values, double p)
    {
        if (values.Count == 0) throw new ArgumentException("no values", nameof(values));
        var sorted = values.OrderBy(v => v).ToArray();
        double rank = Math.Clamp(p, 0, 100) / 100 * (sorted.Length - 1);
        int low = (int)Math.Floor(rank);
        int high = Math.Min(low + 1, sorted.Length - 1);
        return sorted[low] + (sorted[high] - sorted[low]) * (rank - low);
    }
}
