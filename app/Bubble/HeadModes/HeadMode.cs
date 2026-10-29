using System.Reflection;
using System.Text.Json.Serialization;

namespace app.Bubble.HeadModes;

// What moving your head does to the bubble, three ways to try against each other. Picked in the calibrations window
// (setting head.mode). See docs/bubble/HEAD-MODES.md
//
// There are two separate switches, and each mode is a combination of them:
//   head correction: the calibration's head corrections (leaning, tilting) take head movement out of the eye reading,
//                    so the bubble stays where the eyes look. Done in the tracker (gaze_model.py),
//                    --no-head-compensation turns it off
//   head nudge:      once the eyes settle on a spot the bubble holds (yellow), and turning or tilting the head pushes it
//                    from there (HeadNudge.cs). Done in the app, on the overlay's bubble
//
//                    head correction   head nudge
//   EyesOnly         on                off          how it was before the head modes
//   EyesAndNudge     on                on           the head only moves the bubble on purpose, while it's held
//   NudgeNoCorrection off              on           the head always moves it, by accident too. For comparing
//
// The names in settings.json ("cancel", "both", "nudge") are from before the modes were renamed, and stay so saved
// settings keep working.
[JsonConverter(typeof(JsonStringEnumConverter<HeadMode>))]
public enum HeadMode
{
    [JsonStringEnumMemberName("cancel"), Choice("Eyes only")] EyesOnly,
    [JsonStringEnumMemberName("both"), Choice("Eyes + head nudge")] EyesAndNudge,
    [JsonStringEnumMemberName("nudge"), Choice("Eyes + head nudge, no head correction")] NudgeNoCorrection,
}

public static class HeadModeExtensions
{
    public static IReadOnlyList<HeadMode> All { get; } = Enum.GetValues<HeadMode>();

    /// <summary>Head correction: whether the tracker takes head movement out of the eye reading.</summary>
    public static bool Cancels(this HeadMode mode) => mode is HeadMode.EyesOnly or HeadMode.EyesAndNudge;

    /// <summary>Head nudge: whether turning or tilting the head pushes the bubble once the eyes settle.</summary>
    public static bool Nudges(this HeadMode mode) => mode is HeadMode.EyesAndNudge or HeadMode.NudgeNoCorrection;

    /// <summary>Its name, the same as in a settings screen (the Choice on it).</summary>
    public static string Label(this HeadMode mode) =>
        typeof(HeadMode).GetField(mode.ToString())?.GetCustomAttribute<ChoiceAttribute>()?.Label ?? mode.ToString();

    /// <summary>One line on what it does, for the calibrations window.</summary>
    public static string Describe(this HeadMode mode) => mode switch
    {
        HeadMode.EyesOnly => "Only your eyes move the bubble. If you lean or tilt your head, the calibration corrects for it, " +
                             "so the bubble stays where you look.",
        HeadMode.EyesAndNudge => "Like Eyes only, plus: when your eyes rest on a spot, the bubble turns yellow and holds. Then turn " +
                                 "or tilt your head toward where you want it and it moves there. Look somewhere else to let go. " +
                                 "Your head only moves the bubble on purpose, while it's yellow.",
        _ => "The same nudge, but without the head correction: leaning or tilting also moves the white bubble, by accident. " +
             "Only for comparing: if this feels steadier than Eyes + head nudge, the head correction isn't helping you.",
    };
}
