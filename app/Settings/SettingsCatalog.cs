using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace app.Settings;

public enum SettingKind { Toggle, Number, Choice }

// one dropdown entry. Key is what's written in settings.json
public sealed record SettingChoice(object Value, string Key, string Label);

/// <summary>
/// One setting, read from the [Setting] on its property. A settings screen can build itself from SettingsCatalog.All:
/// a checkbox for Toggle, a slider for Number (Min, Max, Step), a dropdown for Choice (Choices),
/// and save with settings.Set(info, value).
/// </summary>
public sealed class SettingInfo
{
    internal SettingInfo(PropertyInfo sectionProperty, string sectionLabel, PropertyInfo property, SettingAttribute attr)
    {
        SectionProperty = sectionProperty;
        Property = property;
        Section = sectionLabel;
        Key = $"{SettingsCatalog.JsonName(sectionProperty.Name)}.{SettingsCatalog.JsonName(property.Name)}";
        Label = attr.Label;
        Description = attr.Description;
        Unit = attr.Unit;
        Min = attr.Min;
        Max = attr.Max;
        Step = attr.Step;
        Type = property.PropertyType;
        Kind = Type == typeof(bool) ? SettingKind.Toggle : Type.IsEnum ? SettingKind.Choice : SettingKind.Number;
        WholeNumber = Type == typeof(int);
        Choices = Type.IsEnum
            ? Enum.GetValues(Type).Cast<object>().Select(v => new SettingChoice(v, SettingsCatalog.JsonValue(v),
                Type.GetField(v.ToString()!)!.GetCustomAttribute<ChoiceAttribute>()?.Label ?? v.ToString()!)).ToList()
            : [];
        Default = Get(new AppSettings());
    }

    internal PropertyInfo SectionProperty { get; }
    internal PropertyInfo Property { get; }

    /// <summary>Where it is in settings.json, e.g. "clicking.dwellMs".</summary>
    public string Key { get; }
    public string Section { get; }
    public string Label { get; }
    public string Description { get; }
    public string? Unit { get; }
    public SettingKind Kind { get; }
    public Type Type { get; }
    public double Min { get; }
    public double Max { get; }
    public double Step { get; }
    public bool WholeNumber { get; }
    public IReadOnlyList<SettingChoice> Choices { get; }
    public object Default { get; }

    public object Get(AppSettings settings) =>
        Property.GetValue(SectionProperty.GetValue(settings) ?? Activator.CreateInstance(SectionProperty.PropertyType))!;

    /// <summary>A copy of settings with this one changed. value can be a number, bool, the enum or its Key;
    /// it's clamped to Min/Max, anything that doesn't fit falls back to the default.</summary>
    public AppSettings With(AppSettings settings, object? value)
    {
        object fixedValue = Fix(value);
        var section = SectionProperty.GetValue(settings) ?? Activator.CreateInstance(SectionProperty.PropertyType)!;
        section = SettingsCatalog.Copy(section);
        Property.SetValue(section, fixedValue);
        var copy = (AppSettings)SettingsCatalog.Copy(settings);
        SectionProperty.SetValue(copy, section);
        return copy;
    }

    private object Fix(object? value)
    {
        switch (Kind)
        {
            case SettingKind.Toggle:
                return value is bool b ? b : Default;

            case SettingKind.Choice:
                if (value is not null && value.GetType() == Type && Enum.IsDefined(Type, value)) return value;
                if (value is string s && Choices.FirstOrDefault(c => c.Key == s) is { } choice) return choice.Value;
                return Default;

            default:
                double d;
                try { d = Convert.ToDouble(value, CultureInfo.InvariantCulture); }
                catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException) { return Default; }
                if (!double.IsFinite(d)) return Default;
                if (!double.IsNaN(Min)) d = Math.Max(d, Min);
                if (!double.IsNaN(Max)) d = Math.Min(d, Max);
                // the cast to object matters, without it the int would be turned back into a double
                return WholeNumber ? (object)(int)Math.Round(d, MidpointRounding.AwayFromZero) : d;
        }
    }
}

public static class SettingsCatalog
{
    // camelCase in the file, enums as their JsonStringEnumMemberName ("1080p", "dwell")
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    /// <summary>Every setting, in the order they're declared in AppSettings.cs.</summary>
    public static IReadOnlyList<SettingInfo> All { get; } = Build();

    public static SettingInfo Find(string key) => All.First(s => s.Key == key);

    private static List<SettingInfo> Build()
    {
        var list = new List<SettingInfo>();
        foreach (var section in typeof(AppSettings).GetProperties())
        {
            string label = section.GetCustomAttribute<SectionAttribute>()?.Label ?? section.Name;
            foreach (var prop in section.PropertyType.GetProperties())
            {
                if (prop.GetCustomAttribute<SettingAttribute>() is { } attr)
                    list.Add(new SettingInfo(section, label, prop, attr));
            }
        }
        return list;
    }

    internal static AppSettings Normalize(AppSettings settings)
    {
        var result = settings;
        foreach (var s in All) result = s.With(result, s.Get(result));
        return result;
    }

    internal static string JsonName(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);

    internal static string JsonValue(object value) => JsonSerializer.Serialize(value, value.GetType(), Json).Trim('"');

    // records only hold values, so a shallow copy is enough
    private static readonly MethodInfo CloneMethod =
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    internal static object Copy(object record) => CloneMethod.Invoke(record, null)!;
}
