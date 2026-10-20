namespace app.Tests;

// Fake eye check recordings, with numbers like the ones in docs/calibration/EYE-CALIBRATION.md ("Reference numbers").
// Shared by the eye check tests.
internal static class FakeEyeCheck
{
    public static EyeSample Open(int i = 0) => new(0.25 + i % 10 * 0.008, 0.55 + i % 7 * 0.02, 0.05, 0.35, 60, false, false);
    // both eyes shut: lids nearly together, a high blink score, and an iris spot that isn't bright (0.51-0.62 in a recording)
    public static EyeSample Closed(int i = 0, double blink = 0.75) => new(0.55 + i % 5 * 0.01, 0.4 + i % 6 * 0.03, blink, 0.04 + i % 5 * 0.002, 60, true, false);

    public static List<FrameSample> Frames(int count, Func<int, (EyeSample? L, EyeSample? R)> eyes,
                                           double pitch = -10, double yaw = 2, bool face = true, double fps = 30,
                                           Func<int, double>? pitchAt = null, Func<int, double>? yawAt = null) =>
        Enumerable.Range(0, count).Select(i =>
        {
            var (l, r) = eyes(i);
            // starting after the reaction time, 33 ms apart
            return new FrameSample(EyeCheckSteps.ReactionMs + i * 33, face, yawAt?.Invoke(i) ?? yaw,
                                   pitchAt?.Invoke(i) ?? pitch, face ? l : null, face ? r : null, fps);
        }).ToList();

    public static EyeCheckStep Step(EyeCheckStepKind kind) => EyeCheckSteps.All.First(s => s.Kind == kind);

    public static readonly EyeCheckBaseline Baseline = new(-10, 2);

    // the grid's frames sitting normally: open eyes, the lids lower looking at the bottom rows (0.26 there)
    public static List<FrameSample> Grid(int count = 400, Func<int, double>? openness = null) =>
        Frames(count, i =>
        {
            double open = openness?.Invoke(i) ?? 0.26 + i % 4 * 0.03;
            return (Open(i) with { Openness = open }, Open(i) with { Openness = open });
        });

    // every step done right
    public static Dictionary<EyeCheckStepKind, IReadOnlyList<FrameSample>> GoodRun() => new()
    {
        [EyeCheckStepKind.CloseBoth] = Frames(90, i => (Closed(i), Closed(i))),
        [EyeCheckStepKind.CloseBothHeadUp] = Frames(90, i => (Closed(i), Closed(i)), pitch: -33),
        [EyeCheckStepKind.CloseBothHeadDown] = Frames(90, i => (Closed(i), Closed(i)), pitch: 10),
    };
}
