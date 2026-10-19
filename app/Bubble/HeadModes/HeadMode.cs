using System.Reflection;
using System.Text.Json.Serialization;

namespace app.Bubble.HeadModes;

// What moving your head does to the bubble, three ways to try against each other. Picked in the calibrations window
// (setting head.mode). See docs/bubble/HEAD-MODES.md
//
// There are two separate parts, and each mode turns them on or off:
//   cancelling: the calibration's head corrections (leaning, tilting) take head movement out of the gaze, so the
//               bubble stays where the eyes look. Done in the tracker (gaze_model.py), --no-head-compensation turns it off
//   nudging:    once the eyes settle on a spot, turning or tilting the head moves the bubble from there (HeadNudge.cs).
//               Done in the app, on the overlay's bubble
[JsonConverter(typeof(JsonStringEnumConverter<HeadMode>))]
public enum HeadMode
{
    // 1. how it was before the head modes: cancelling only
    [JsonStringEnumMemberName("cancel"), Choice("Cancel head movement")] Cancel,
    // 2. nudging only, the tracker doesn't cancel head movement
    [JsonStringEnumMemberName("nudge"), Choice("Nudge with your head")] Nudge,
    // 3. cancelling and nudging
    [JsonStringEnumMemberName("both"), Choice("Cancel and nudge")] Both,
}

public static class HeadModeExtensions
{
    public static IReadOnlyList<HeadMode> All { get; } = Enum.GetValues<HeadMode>();

    /// <summary>Whether the tracker takes head movement out of the gaze (the calibration's head corrections).</summary>
    public static bool Cancels(this HeadMode mode) => mode is HeadMode.Cancel or HeadMode.Both;

    /// <summary>Whether turning or tilting the head nudges the bubble once the eyes settle.</summary>
    public static bool Nudges(this HeadMode mode) => mode is HeadMode.Nudge or HeadMode.Both;

    /// <summary>Its name, the same as in a settings screen (the Choice on it).</summary>
    public static string Label(this HeadMode mode) =>
        typeof(HeadMode).GetField(mode.ToString())?.GetCustomAttribute<ChoiceAttribute>()?.Label ?? mode.ToString();

    /// <summary>One line on what it does, for the calibrations window.</summary>
    public static string Describe(this HeadMode mode) => mode switch
    {
        HeadMode.Cancel => "The bubble follows your eyes only. Leaning and tilting your head are cancelled out by the calibration, " +
                           "so the bubble stays where you look. How mouSEE worked so far.",
        HeadMode.Nudge => "Your eyes get the bubble close. Once they settle on a spot, turn or tilt your head toward where you want it " +
                          "and it moves there. Head movement isn't cancelled out.",
        _ => "Both: head movement is cancelled out, and once your eyes settle, turning or tilting your head nudges the bubble.",
    };
}
