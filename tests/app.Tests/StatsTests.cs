namespace app.Tests;

public class StatsTests
{
    [Fact]
    public void Percentile_interpolates_like_numpy()
    {
        double[] values = [1, 2, 3, 4, 5];
        Assert.Equal(3, Stats.Percentile(values, 50));
        Assert.Equal(1, Stats.Percentile(values, 0));
        Assert.Equal(5, Stats.Percentile(values, 100));
        Assert.Equal(4.96, Stats.Percentile(values, 99), 6); // numpy.percentile([1,2,3,4,5], 99)
        Assert.Equal(1.2, Stats.Percentile(values, 5), 6);
        Assert.Equal(7, Stats.Percentile([7], 30));
    }
}
