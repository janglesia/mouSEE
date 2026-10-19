namespace app.Settings;

// Label and Description are what a settings screen shows. Min and Max also clamp the value
// whenever settings are loaded or changed, so they're the only place a range is written down
[AttributeUsage(AttributeTargets.Property)]
public sealed class SettingAttribute(string label, string description) : Attribute
{
    public string Label { get; } = label;
    public string Description { get; } = description;
    public double Min { get; set; } = double.NaN;
    public double Max { get; set; } = double.NaN;
    public double Step { get; set; } = double.NaN; // slider step, doesn't round the saved value
    public string? Unit { get; set; }
}

// heading for a group of settings
[AttributeUsage(AttributeTargets.Property)]
public sealed class SectionAttribute(string label) : Attribute
{
    public string Label { get; } = label;
}

// how an enum value is shown in a dropdown
[AttributeUsage(AttributeTargets.Field)]
public sealed class ChoiceAttribute(string label) : Attribute
{
    public string Label { get; } = label;
}
