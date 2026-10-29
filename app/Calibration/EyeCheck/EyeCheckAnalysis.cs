namespace app.Calibration.EyeCheck;

/// <summary>What the eye check worked out: the values for the tracker (null = keep the default) and a report for each.</summary>
public sealed record EyeCheckOutcome(EyeThresholds Thresholds, List<EyeValueReport> Values, List<string> Warnings)
{
    /// <summary>True if the closed eye value changed, then it's worth checking it (the check round).</summary>
    public bool TunedDetection => Thresholds.LidsShutOpenness is not null;
}

/// <summary>
/// Turns the grid's open eyes and the eye check's closed ones into values, the formulas from
/// docs/calibration/EYE-CALIBRATION.md. Percentiles, not min/max, so one glitched frame doesn't move a limit.
/// </summary>
public static class EyeCheckAnalysis
{
    // the tracker's defaults, picked by hand from short recordings of one person. used when the eye check can't
    // work out a better value. EyeCheckAnalysisTests checks they still match the Python files
    public const double DefaultLidsShutOpenness = 0.1;   // eye_extractor.py
    public const double DefaultIrisHiddenRatio = 0.65;   // eye_extractor.py
    public const double DefaultLidsOpenOpenness = 0.26;  // eye_extractor.py
    public const double DefaultCoveredContrast = 0.01;   // eye_extractor.py | changed to 0.01 to not be used anymore
    public const double DefaultRestPitch = 0.0;          // tracking_status.py
    public const double DefaultRestYaw = 0.0;            // tracking_status.py

    // below this many samples a percentile means nothing
    public const int MinSamples = 20;

    // shut lids: the gap between the most closed open eye and the least closed shut one has to be at least this, and
    // the value stays in this range (not above LOOKING_DOWN_MIN_OPENNESS, so the looking down rule never undoes it)
    public const double MinShutGap = 0.05;
    public const double ShutMin = 0.05, ShutMax = 0.15;
    // distance and frame rate warnings
    public const double MinEyeWidthPx = 30;   // 2x MIN_EYE_WIDTH_PX in tracking_status.py
    public const double MinFps = 25;

    /// <summary>
    /// open: the grid's frames sitting normally (eyes on the dots, open, all over the screen). closed: the usable frames
    /// (after the reaction time) of every eye check step that passed its check. A step that never passed is left out,
    /// so nothing is calculated from bad data.
    /// </summary>
    public static EyeCheckOutcome Analyze(IReadOnlyList<FrameSample> open, IReadOnlyDictionary<EyeCheckStepKind, IReadOnlyList<FrameSample>> closed)
    {
        var values = new List<EyeValueReport>();
        var warnings = new List<string>();
        var grid = open.Where(f => f.FaceFound).ToList();
        var shut = closed.Values.SelectMany(f => f).Where(f => f.FaceFound).ToList();

        // ---- REST_PITCH / REST_YAW: the head angle when sitting normally, during the grid
        double? restPitch = null, restYaw = null;
        if (grid.Count >= MinSamples)
        {
            restPitch = Math.Round(Stats.Percentile(grid.Select(f => f.Pitch).ToList(), 50), 1);
            restYaw = Math.Round(Stats.Percentile(grid.Select(f => f.Yaw).ToList(), 50), 1);
            const string why = "Measured while you looked at the grid's dots, compared with facing the camera straight on. " +
                               "Head turns are measured from here, so a camera above or below the screen doesn't count as your head being turned.";
            values.Add(new EyeValueReport
            {
                Name = "REST_PITCH", Label = "Resting head angle, up/down", Used = restPitch.Value, Default = DefaultRestPitch, Tuned = true,
                Measured = new() { ["median"] = restPitch.Value, ["frames"] = grid.Count },
                Explanation = $"{restPitch:0.#}°. {why}",
            });
            values.Add(new EyeValueReport
            {
                Name = "REST_YAW", Label = "Resting head angle, left/right", Used = restYaw.Value, Default = DefaultRestYaw, Tuned = true,
                Measured = new() { ["median"] = restYaw.Value, ["frames"] = grid.Count },
                Explanation = $"{restYaw:0.#}°. {why}",
            });

            double? width = Spread.Of(grid.SelectMany(f => new[] { f.Left?.WidthPx, f.Right?.WidthPx }).Where(w => w > 0))?.Median;
            if (width < MinEyeWidthPx)
                warnings.Add($"Your eyes are {width:0} px wide in the camera image. Under {MinEyeWidthPx:0} px the iris is only a few pixels, " +
                             "so tracking is less precise. Move closer or use a higher camera resolution.");
            double? fps = Spread.Of(grid.Select(f => (double?)f.CaptureFps).Where(v => v > 0))?.Median;
            if (fps < MinFps)
                warnings.Add($"The camera delivered {fps:0} frames a second. Under {MinFps:0} is usually not enough light, " +
                             "or a camera that can't go faster at this resolution.");
        }
        else
        {
            foreach (var (name, restLabel) in new[] { ("REST_PITCH", "Resting head angle, up/down"), ("REST_YAW", "Resting head angle, left/right") })
                values.Add(new EyeValueReport
                {
                    Name = name, Label = restLabel, Used = 0, Default = 0, Tuned = false,
                    Explanation = "0°, the default (facing the camera straight on). Not measured, the grid had too few frames with your face.",
                });
        }

        // ---- LIDS_SHUT_OPENNESS (open: both eyes during the grid, blinks left out, closed: both eyes in the steps). It has
        // to sit below every open eye, looking at the bottom of the screen included (the lids come down then), and above
        // every shut one, so the extremes: 1st percentile open, 99th closed
        const string label = "Closed eyes, lid check";
        const string what = "Lids closer together than this (gap between the lids / eye width) count as closed.";
        var openLids = Values(grid.Where(NotBlinking), f => [f.Left?.Openness, f.Right?.Openness]);
        var shutLids = Values(shut, f => [f.Left?.Openness, f.Right?.Openness]);
        double? lids = null;
        if (openLids.Count < MinSamples || shutLids.Count < MinSamples)
        {
            values.Add(NotMeasured("LIDS_SHUT_OPENNESS", label, DefaultLidsShutOpenness,
                                   openLids.Count < MinSamples ? "the grid had too few frames with your eyes open" : "closing your eyes didn't work"));
        }
        else
        {
            double openLow = Math.Round(Stats.Percentile(openLids, 1), 3);
            double shutHigh = Math.Round(Stats.Percentile(shutLids, 99), 3);
            double gap = Math.Round(openLow - shutHigh, 3);
            var measured = new Dictionary<string, double>
            {
                ["openLowest"] = openLow, ["closedHighest"] = shutHigh, ["gap"] = gap, ["gapNeeded"] = MinShutGap,
                ["openSamples"] = openLids.Count, ["closedSamples"] = shutLids.Count,
            };
            if (gap < MinShutGap)
                values.Add(new EyeValueReport
                {
                    Name = "LIDS_SHUT_OPENNESS", Label = label, Used = DefaultLidsShutOpenness, Default = DefaultLidsShutOpenness, Tuned = false,
                    Measured = measured,
                    Explanation = $"{DefaultLidsShutOpenness}, the default. {what} Your open eyes measured from {openLow:0.00} (looking at the bottom " +
                                  $"of the screen brings the lids down) and your closed eyes up to {shutHigh:0.00}, " +
                                  $"{(gap < 0 ? "they overlap" : $"only {gap:0.00} apart")}. At least {MinShutGap:0.00} is needed, so it wasn't changed. " +
                                  "Closing your eyes all the way helps.",
                });
            else
            {
                lids = Math.Round(Math.Clamp((openLow + shutHigh) / 2, ShutMin, ShutMax), 3);
                values.Add(new EyeValueReport
                {
                    Name = "LIDS_SHUT_OPENNESS", Label = label, Used = lids.Value, Default = DefaultLidsShutOpenness, Tuned = true,
                    Measured = measured,
                    Explanation = $"{lids:0.000} instead of the default {DefaultLidsShutOpenness}. {what} Your open eyes measured at least " +
                                  $"{openLow:0.00} and your closed eyes at most {shutHigh:0.00}, this is halfway between" +
                                  (lids.Value == ShutMax ? $", but at most {ShutMax} (below that the eyes looking down count as open)." : "."),
                });
            }
        }

        // ---- the wink values aren't tuned, the eye check only closes both eyes
        const string winks = "Not tuned: it tells a wink (one eye closed) from reflections on glasses, and the eye check only closes both eyes.";
        values.Add(new EyeValueReport
        {
            Name = "IRIS_HIDDEN_RATIO", Label = "One eye closed, iris check", Used = DefaultIrisHiddenRatio, Default = DefaultIrisHiddenRatio,
            Tuned = false, Explanation = $"{DefaultIrisHiddenRatio}, the default. {winks}",
        });
        values.Add(new EyeValueReport
        {
            Name = "LIDS_OPEN_OPENNESS", Label = "One eye closed, lid check", Used = DefaultLidsOpenOpenness, Default = DefaultLidsOpenOpenness,
            Tuned = false, Explanation = $"{DefaultLidsOpenOpenness}, the default. {winks}",
        });

        // ---- COVERED_CONTRAST isn't tuned: that needs a hand over the eye, and mouSEE is for people who can't use their hands.
        // it keeps its default, which still catches hair or something else in front of an eye
        values.Add(new EyeValueReport
        {
            Name = "COVERED_CONTRAST", Label = "Covered eye", Used = DefaultCoveredContrast, Default = DefaultCoveredContrast, Tuned = false,
            Explanation = $"{DefaultCoveredContrast}, the default. An eye counts as covered when there's almost no contrast left in it. " +
                          "Not measured, that would need a hand over the eye. It still notices hair or something else in front of an eye.",
        });

        var thresholds = new EyeThresholds { LidsShutOpenness = lids, RestPitch = restPitch, RestYaw = restYaw };
        return new EyeCheckOutcome(thresholds, values, warnings);
    }

    // a blink during the grid isn't an open eye
    private static bool NotBlinking(FrameSample f) =>
        f.Left is { Closed: false } l && f.Right is { Closed: false } r
        && (l.Blink ?? 0) < EyeCheckSteps.BothClosedBlink && (r.Blink ?? 0) < EyeCheckSteps.BothClosedBlink;

    private static EyeValueReport NotMeasured(string name, string label, double defaultValue, string why) => new()
    {
        Name = name, Label = label, Used = defaultValue, Default = defaultValue, Tuned = false,
        Explanation = $"{defaultValue}, the default. Not measured, {why}.",
    };

    private static List<double> Values(IEnumerable<FrameSample> frames, Func<FrameSample, double?[]> pick) =>
        frames.SelectMany(pick).OfType<double>().Where(double.IsFinite).ToList();
}
