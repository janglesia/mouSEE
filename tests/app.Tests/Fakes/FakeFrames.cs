namespace app.Tests;

// Fake tracker frames and eyes, shared by the tests that need a few frames.
internal static class FakeFrames
{
    public static EyeSample Open(double h = 0, double v = 0, double openness = 0.35) =>
        new(0.25, 0.55, 0.05, openness, 60, false, false, h, v);

    public static FrameSample Frame(long ms, EyeSample? l, EyeSample? r, double pitch = -10, double yaw = 2, bool face = true) =>
        new(ms, face, yaw, pitch, l, r, 30, 1);

    public static List<FrameSample> Many(int n, Func<int, FrameSample> make) => Enumerable.Range(0, n).Select(make).ToList();
}
