namespace app.Calibration.EyeCheck;

/// <summary>What the eye check worked out: the values for the tracker (null = keep the default) and a report for each.</summary>
public sealed record EyeCheckOutcome(EyeThresholds Thresholds, List<EyeValueReport> Values, List<string> Warnings)
{
    /// <summary>True if a closed eye value changed, then it's worth checking them (step 7).</summary>
    public bool TunedDetection => Thresholds.IrisHiddenRatio is not null || Thresholds.LidsOpenOpenness is not null;
}

/// <summary>
/// Turns the eye check's recordings into values, the formulas from docs/calibration/EYE-CALIBRATION.md.
/// Percentiles, not min/max, so one glitched frame doesn't move a limit.
/// </summary>
public static class EyeCheckAnalysis
{
    // the tracker's defaults, picked by hand from short recordings of one person. used when the eye check can't
    // work out a better value. EyeCheckAnalysisTests checks they still match the Python files
    public const double DefaultIrisHiddenRatio = 0.65;   // eye_extractor.py
    public const double DefaultLidsOpenOpenness = 0.26;  // eye_extractor.py
    public const double DefaultCoveredContrast = 0.15;   // eye_extractor.py
    public const double DefaultMaxPitch = 30.0;          // tracking_status.py
    public const double DefaultMaxYaw = 35.0;            // tracking_status.py
    public const double DefaultRestPitch = 0.0;          // tracking_status.py
    public const double DefaultRestYaw = 0.0;            // tracking_status.py

    // below this many samples a percentile means nothing
    public const int MinSamples = 20;

    public const double MinIrisGap = 0.1;
    public const double IrisMin = 0.5, IrisMax = 0.85;
    public const double MinLidGap = 0.02;
    public const double LidsMin = 0.12, LidsMax = 0.4;
    // MAX_PITCH / MAX_YAW have to allow the furthest the head turned looking at the corners, plus this
    public const double HeadMarginDeg = 10;
    // distance and frame rate warnings
    public const double MinEyeWidthPx = 30;   // 2x MIN_EYE_WIDTH_PX in tracking_status.py
    public const double MinFps = 25;

    /// <summary>recordings: the usable frames (after the reaction time) of every step that passed its check.
    /// A step that never passed is left out, so nothing is calculated from bad data.</summary>
    public static EyeCheckOutcome Analyze(IReadOnlyDictionary<EyeCheckStepKind, IReadOnlyList<FrameSample>> recordings)
    {
        var values = new List<EyeValueReport>();
        var warnings = new List<string>();
        var steps = EyeCheckSteps.All.ToDictionary(s => s.Kind);
        IReadOnlyList<FrameSample> Frames(EyeCheckStepKind kind) =>
            recordings.TryGetValue(kind, out var f) ? f.Where(x => x.FaceFound).ToList() : [];

        var rest = Frames(EyeCheckStepKind.Rest);
        var look = Frames(EyeCheckStepKind.LookAround);
        // steps 2-6, the ones with a closed eye
        EyeCheckStepKind[] closedSteps = [EyeCheckStepKind.CloseLeft, EyeCheckStepKind.CloseRight, EyeCheckStepKind.CloseLeftHeadUp,
                                          EyeCheckStepKind.CloseRightHeadDown, EyeCheckStepKind.CloseBoth];
        List<double> ClosedEyes(Func<EyeSample, double?> pick)
        {
            var list = new List<double>();
            foreach (var kind in closedSteps)
            {
                var sides = steps[kind].ClosedEyes;
                list.AddRange(Values(Frames(kind), f => sides.Select(s => f.EyeOn(s) is { } e ? pick(e) : null).ToArray()));
            }
            return list;
        }

        // ---- REST_PITCH / REST_YAW: the head angle when sitting normally (step 0)
        double? restPitch = null, restYaw = null;
        if (rest.Count >= MinSamples)
        {
            restPitch = Math.Round(Stats.Percentile(rest.Select(f => f.Pitch).ToList(), 50), 1);
            restYaw = Math.Round(Stats.Percentile(rest.Select(f => f.Yaw).ToList(), 50), 1);
            const string why = "Measured while you sat normally, compared with facing the camera straight on. " +
                               "Head turns are measured from here, so a camera above or below the screen doesn't count as your head being turned.";
            values.Add(new EyeValueReport
            {
                Name = "REST_PITCH", Label = "Resting head angle, up/down", Used = restPitch.Value, Default = DefaultRestPitch, Tuned = true,
                Measured = new() { ["median"] = restPitch.Value, ["frames"] = rest.Count },
                Explanation = $"{restPitch:0.#}°. {why}",
            });
            values.Add(new EyeValueReport
            {
                Name = "REST_YAW", Label = "Resting head angle, left/right", Used = restYaw.Value, Default = DefaultRestYaw, Tuned = true,
                Measured = new() { ["median"] = restYaw.Value, ["frames"] = rest.Count },
                Explanation = $"{restYaw:0.#}°. {why}",
            });

            double? width = Spread.Of(rest.SelectMany(f => new[] { f.Left?.WidthPx, f.Right?.WidthPx }).Where(w => w > 0))?.Median;
            if (width < MinEyeWidthPx)
                warnings.Add($"Your eyes are {width:0} px wide in the camera image. Under {MinEyeWidthPx:0} px the iris is only a few pixels, " +
                             "so tracking is less precise. Move closer or use a higher camera resolution.");
            double? fps = Spread.Of(rest.Select(f => (double?)f.CaptureFps).Where(v => v > 0))?.Median;
            if (fps < MinFps)
                warnings.Add($"The camera delivered {fps:0} frames a second. Under {MinFps:0} is usually not enough light, " +
                             "or a camera that can't go faster at this resolution.");
        }
        else
        {
            foreach (var (name, label) in new[] { ("REST_PITCH", "Resting head angle, up/down"), ("REST_YAW", "Resting head angle, left/right") })
                values.Add(new EyeValueReport
                {
                    Name = name, Label = label, Used = 0, Default = 0, Tuned = false,
                    Explanation = "0°, the default (facing the camera straight on). Not measured, the first step didn't work.",
                });
        }

        // ---- MAX_PITCH / MAX_YAW: looking at the corners must not count as head turned (step 1)
        double? maxPitch = null, maxYaw = null;
        if (look.Count >= MinSamples && restPitch is not null)
        {
            double pitchUsed = Math.Round(Stats.Percentile(look.Select(f => Math.Abs(f.Pitch - restPitch.Value)).ToList(), 99), 1);
            double yawUsed = Math.Round(Stats.Percentile(look.Select(f => Math.Abs(f.Yaw - restYaw!.Value)).ToList(), 99), 1);
            maxPitch = Math.Round(Math.Max(DefaultMaxPitch, pitchUsed + HeadMarginDeg), 1);
            maxYaw = Math.Round(Math.Max(DefaultMaxYaw, yawUsed + HeadMarginDeg), 1);
            values.Add(HeadLimit("MAX_PITCH", "Head turn limit, up/down", "up/down", maxPitch.Value, DefaultMaxPitch, pitchUsed));
            values.Add(HeadLimit("MAX_YAW", "Head turn limit, left/right", "left/right", maxYaw.Value, DefaultMaxYaw, yawUsed));
        }
        else
        {
            values.Add(NotMeasured("MAX_PITCH", "Head turn limit, up/down", DefaultMaxPitch, "°", "the \"follow the dot\" step didn't work"));
            values.Add(NotMeasured("MAX_YAW", "Head turn limit, left/right", DefaultMaxYaw, "°", "the \"follow the dot\" step didn't work"));
        }

        // ---- IRIS_HIDDEN_RATIO (open: step 1, closed: the closed eyes in steps 2-6)
        const string irisLabel = "Closed eye, iris check";
        const string irisWhat = "An eye counts as closed when its iris ratio (how bright the iris spot is compared with the rest of the eye) is above this.";
        var openIris = Values(look, f => [f.Left?.IrisRatio, f.Right?.IrisRatio]);
        var closedIris = ClosedEyes(e => e.IrisRatio);
        double? iris = null;
        if (openIris.Count < MinSamples || closedIris.Count < MinSamples)
        {
            values.Add(NotMeasured("IRIS_HIDDEN_RATIO", irisLabel, DefaultIrisHiddenRatio, "", "the open or closed eye steps didn't work"));
        }
        else
        {
            double openHigh = Math.Round(Stats.Percentile(openIris, 99), 3);
            double closedLow = Math.Round(Stats.Percentile(closedIris, 5), 3);
            double gap = Math.Round(closedLow - openHigh, 3);
            var measured = new Dictionary<string, double>
            {
                ["openHighest"] = openHigh, ["closedLowest"] = closedLow, ["gap"] = gap, ["gapNeeded"] = MinIrisGap,
                ["openSamples"] = openIris.Count, ["closedSamples"] = closedIris.Count,
            };
            if (gap < MinIrisGap)
                values.Add(new EyeValueReport
                {
                    Name = "IRIS_HIDDEN_RATIO", Label = irisLabel, Used = DefaultIrisHiddenRatio, Default = DefaultIrisHiddenRatio, Tuned = false,
                    Measured = measured,
                    Explanation = $"{DefaultIrisHiddenRatio}, the default. {irisWhat} Your open eyes measured up to {openHigh:0.00} and your closed eyes " +
                                  $"from {closedLow:0.00}, {(gap < 0 ? "they overlap" : $"only {gap:0.00} apart")}. At least {MinIrisGap:0.00} is needed " +
                                  "to tell them apart reliably, so it wasn't changed. This happens with glasses (they reflect the screen onto the iris) " +
                                  "and in dim light. The lid check still tells open from closed.",
                });
            else
            {
                iris = Math.Round(Math.Clamp((openHigh + closedLow) / 2, IrisMin, IrisMax), 3);
                values.Add(new EyeValueReport
                {
                    Name = "IRIS_HIDDEN_RATIO", Label = irisLabel, Used = iris.Value, Default = DefaultIrisHiddenRatio, Tuned = true,
                    Measured = measured,
                    Explanation = $"{iris:0.000} instead of the default {DefaultIrisHiddenRatio}. {irisWhat} Your open eyes measured at most " +
                                  $"{openHigh:0.00} and your closed eyes at least {closedLow:0.00}, this is halfway between.",
                });
            }
        }

        // ---- LIDS_OPEN_OPENNESS (open: both eyes in step 1, closed: the closed eyes in steps 2-6). It has to sit above
        // every closed eye (or winks get missed) and below every open one, looking down included (the lid comes down a
        // bit then), so the extremes: 1st percentile open, 99th closed
        const string lidsLabel = "Closed eye, lid check";
        const string lidsWhat = "Lids at least this far apart (gap between the lids / eye width) always count as open, whatever the iris check " +
                                "says, so reflections on glasses can't make an open eye look closed.";
        var openLids = Values(look, f => [f.Left?.Openness, f.Right?.Openness]);
        var closedLids = ClosedEyes(e => e.Openness);
        double? lids = null;
        if (openLids.Count < MinSamples || closedLids.Count < MinSamples)
        {
            values.Add(NotMeasured("LIDS_OPEN_OPENNESS", lidsLabel, DefaultLidsOpenOpenness, "", "the open or closed eye steps didn't work"));
        }
        else
        {
            double openLow = Math.Round(Stats.Percentile(openLids, 1), 3);
            double closedHigh = Math.Round(Stats.Percentile(closedLids, 99), 3);
            double gap = Math.Round(openLow - closedHigh, 3);
            var measured = new Dictionary<string, double>
            {
                ["openLowest"] = openLow, ["closedHighest"] = closedHigh, ["gap"] = gap, ["gapNeeded"] = MinLidGap,
                ["openSamples"] = openLids.Count, ["closedSamples"] = closedLids.Count,
            };
            if (gap < MinLidGap)
                values.Add(new EyeValueReport
                {
                    Name = "LIDS_OPEN_OPENNESS", Label = lidsLabel, Used = DefaultLidsOpenOpenness, Default = DefaultLidsOpenOpenness, Tuned = false,
                    Measured = measured,
                    Explanation = $"{DefaultLidsOpenOpenness}, the default. {lidsWhat} Your open eyes measured from {openLow:0.00} (looking down " +
                                  $"brings the lid down) and your closed eyes up to {closedHigh:0.00}, {(gap < 0 ? "they overlap" : $"only {gap:0.00} apart")}. " +
                                  $"At least {MinLidGap:0.00} is needed, so it wasn't changed. Closing your eyes all the way in the closed eye steps helps.",
                });
            else
            {
                lids = Math.Round(Math.Clamp((openLow + closedHigh) / 2, LidsMin, LidsMax), 3);
                values.Add(new EyeValueReport
                {
                    Name = "LIDS_OPEN_OPENNESS", Label = lidsLabel, Used = lids.Value, Default = DefaultLidsOpenOpenness, Tuned = true,
                    Measured = measured,
                    Explanation = $"{lids:0.000} instead of the default {DefaultLidsOpenOpenness}. {lidsWhat} Your open eyes measured at least " +
                                  $"{openLow:0.00} and your closed eyes at most {closedHigh:0.00}, this is halfway between.",
                });
            }
        }

        // ---- COVERED_CONTRAST isn't tuned: that needs a hand over the eye, and mouSEE is for people who can't use their hands.
        // it keeps its default, which still catches hair or something else in front of an eye
        values.Add(new EyeValueReport
        {
            Name = "COVERED_CONTRAST", Label = "Covered eye", Used = DefaultCoveredContrast, Default = DefaultCoveredContrast, Tuned = false,
            Explanation = $"{DefaultCoveredContrast}, the default. An eye counts as covered when there's almost no contrast left in it. " +
                          "Not measured, that would need a hand over the eye. It still notices hair or something else in front of an eye.",
        });

        var thresholds = new EyeThresholds
        {
            IrisHiddenRatio = iris,
            LidsOpenOpenness = lids,
            RestPitch = restPitch,
            RestYaw = restYaw,
            MaxPitch = maxPitch,
            MaxYaw = maxYaw,
        };
        return new EyeCheckOutcome(thresholds, values, warnings);
    }

    private static EyeValueReport HeadLimit(string name, string label, string direction, double used, double defaultValue, double turned)
    {
        double needed = Math.Round(turned + HeadMarginDeg, 1);
        string what = "Turning your head further than this from resting counts as turned away, and the cursor stops.";
        return new EyeValueReport
        {
            Name = name, Label = label, Used = used, Default = defaultValue, Tuned = used > defaultValue,
            Measured = new() { ["turnedFurthest"] = turned, ["margin"] = HeadMarginDeg, ["needed"] = needed },
            Explanation = used > defaultValue
                ? $"Raised from the default {defaultValue:0.#}° to {used:0.#}°. {what} Following the dot to the corners your head moved up to " +
                  $"{turned:0.#}° {direction}, plus {HeadMarginDeg:0}° to spare so looking at a corner never counts as turned away."
                : $"{used:0.#}°, the default. {what} Following the dot to the corners your head moved up to {turned:0.#}° {direction}. " +
                  $"The limit has to be at least that plus {HeadMarginDeg:0}° ({needed:0.#}°), so the default already covers it.",
        };
    }

    private static EyeValueReport NotMeasured(string name, string label, double defaultValue, string unit, string why) => new()
    {
        Name = name, Label = label, Used = defaultValue, Default = defaultValue, Tuned = false,
        Explanation = $"{defaultValue}{unit}, the default. Not measured, {why}.",
    };

    private static List<double> Values(IEnumerable<FrameSample> frames, Func<FrameSample, double?[]> pick) =>
        frames.SelectMany(pick).OfType<double>().Where(double.IsFinite).ToList();
}
