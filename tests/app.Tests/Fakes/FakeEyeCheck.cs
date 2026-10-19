namespace app.Tests;

// Fake eye check recordings, with numbers like the ones in docs/calibration/EYE-CALIBRATION.md ("Reference numbers").
// Shared by the eye check tests.
internal static class FakeEyeCheck
{
    public static EyeSample Open(int i = 0) => new(0.25 + i % 10 * 0.008, 0.55 + i % 7 * 0.02, 0.05, 0.35, 60, false, false);
    public static EyeSample Closed(int i = 0, double blink = 0.3) => new(0.92 + i % 5 * 0.01, 0.4 + i % 6 * 0.03, blink, 0.2, 60, true, false);

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

    // every step done right
    public static Dictionary<EyeCheckStepKind, IReadOnlyList<FrameSample>> GoodRun() => new()
    {
        [EyeCheckStepKind.Rest] = Frames(90, i => (Open(i), Open(i))),
        [EyeCheckStepKind.LookAround] = Frames(200, i => (Open(i), Open(i)), pitchAt: i => -10 + (i % 20 - 10), yawAt: i => 2 + (i % 30 - 15)),
        [EyeCheckStepKind.CloseLeft] = Frames(110, i => (Closed(i), Open(i))),
        [EyeCheckStepKind.CloseRight] = Frames(110, i => (Open(i), Closed(i))),
        [EyeCheckStepKind.CloseLeftHeadUp] = Frames(110, i => (Closed(i, blink: 0.37), Open(i)), pitch: -33),
        [EyeCheckStepKind.CloseRightHeadDown] = Frames(110, i => (Open(i), Closed(i)), pitch: 10),
        [EyeCheckStepKind.CloseBoth] = Frames(80, i => (Closed(i, 0.7), Closed(i, 0.7))),
    };
}
