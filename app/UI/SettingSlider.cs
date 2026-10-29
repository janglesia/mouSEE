using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace app.UI;

// A slider for one number setting, built from its [Setting] (label, range, step, unit, description), saving as it moves.
// The calibrations window uses it for the head nudge numbers. A settings screen can use it for any number setting:
// new SettingSlider(SettingsCatalog.Find("clicking.dwellMs"), app.Settings)
public sealed class SettingSlider : StackPanel
{
    private static readonly Brush LabelBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
    private static readonly Brush DimBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A));

    private readonly SettingInfo _setting;
    private readonly SettingsService _settings;
    private readonly Slider _slider;
    private readonly TextBlock _value;
    private bool _updating;

    public SettingSlider(SettingInfo setting, SettingsService settings)
    {
        _setting = setting;
        _settings = settings;
        Margin = new Thickness(0, 0, 0, 10);

        var header = new DockPanel();
        _value = new TextBlock { Foreground = LabelBrush, FontSize = 12 };
        DockPanel.SetDock(_value, Dock.Right);
        header.Children.Add(_value);
        header.Children.Add(new TextBlock { Text = setting.Label, Foreground = LabelBrush, FontSize = 12, FontWeight = FontWeights.SemiBold });
        Children.Add(header);

        _slider = new Slider
        {
            Minimum = setting.Min,
            Maximum = setting.Max,
            SmallChange = double.IsNaN(setting.Step) ? 1 : setting.Step,
            LargeChange = double.IsNaN(setting.Step) ? 1 : setting.Step * 4,
            TickFrequency = double.IsNaN(setting.Step) ? 1 : setting.Step,
            IsSnapToTickEnabled = !double.IsNaN(setting.Step),
            Margin = new Thickness(0, 2, 0, 0),
        };
        _slider.ValueChanged += (_, e) =>
        {
            ShowValue(e.NewValue);
            if (!_updating) _settings.Set(_setting, e.NewValue);
        };
        Children.Add(_slider);
        Children.Add(new TextBlock { Text = setting.Description, Foreground = DimBrush, FontSize = 11, TextWrapping = TextWrapping.Wrap });

        Refresh();
        _settings.Changed += OnChanged;
        Unloaded += (_, _) => _settings.Changed -= OnChanged;
    }

    private void OnChanged(AppSettings old, AppSettings now)
    {
        if (!Equals(_setting.Get(old), _setting.Get(now))) Refresh();
    }

    // from the saved value, without saving it again
    private void Refresh()
    {
        _updating = true;
        _slider.Value = Convert.ToDouble(_settings.Get(_setting), CultureInfo.InvariantCulture);
        _updating = false;
        ShowValue(_slider.Value);
    }

    private void ShowValue(double value) =>
        _value.Text = (_setting.WholeNumber ? value.ToString("0", CultureInfo.CurrentCulture) : value.ToString("0.##", CultureInfo.CurrentCulture))
                      + (_setting.Unit is { } unit ? " " + unit : "");
}
