using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;

namespace app.Calibration.Saving;

// Lists the saved calibrations: pick the one to use, delete one, or make a new one.
// With none saved yet it's the "do you want to calibrate?" question on startup.
// Opens with Ctrl+Alt+C, or c in the camera window
public partial class CalibrationListWindow : Window
{
    private readonly App _app = (App)Application.Current;
    private readonly Speaker _speaker = new();
    private bool _askedOutLoud;

    // one row in the list
    public sealed record Row(string Id, string Name, string Detail, Visibility InUseVisibility);

    public CalibrationListWindow()
    {
        InitializeComponent();
        _app.Calibrations.Changed += Refresh;
        _app.Settings.Changed += OnSettingsChanged;
        // the startup question by looking: right to calibrate, left for default values
        Choice.Chosen += yes =>
        {
            if (yes) StartNew();
            else Close();
        };
        Closed += (_, _) =>
        {
            _app.Calibrations.Changed -= Refresh;
            _app.Settings.Changed -= OnSettingsChanged;
            _speaker.Dispose();
        };
        Refresh();
        BuildHeadModes();
    }

    // one radio button per head mode, numbered, with what it does under it
    private void BuildHeadModes()
    {
        int number = 1;
        foreach (var mode in HeadModeExtensions.All)
        {
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = $"{number++}. {mode.Label()}", Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = mode.Describe(), Foreground = (Brush)new BrushConverter().ConvertFrom("#9A9A9A")!, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            var button = new RadioButton { Content = text, GroupName = "HeadMode", Tag = mode, Margin = new Thickness(0, 0, 0, 8), VerticalContentAlignment = VerticalAlignment.Top };
            button.Checked += (_, _) => _app.Settings.Update(s => s with { Head = s.Head with { Mode = mode } });
            HeadModeList.Children.Add(button);
        }
        foreach (string key in NudgeSettings)
            NudgeSliders.Children.Add(new SettingSlider(SettingsCatalog.Find(key), _app.Settings));
        foreach (string key in GuideSettings)
            GuideSliders.Children.Add(new SettingSlider(SettingsCatalog.Find(key), _app.Settings));
        ShowHeadMode(_app.Settings.Current.Head.Mode);
    }

    // the nudge numbers you can change here, the most useful first
    private static readonly string[] GuideSettings = ["overlay.positionHintPercent", "overlay.tiltHintDegrees"];
    private static readonly string[] NudgeSettings =
        ["head.nudgeSettleMs", "head.nudgeStrength", "head.nudgeReleasePercent", "head.nudgeDeadZone"];

    private void ShowHeadMode(HeadMode mode)
    {
        foreach (var button in HeadModeList.Children.OfType<RadioButton>())
            button.IsChecked = (HeadMode)button.Tag == mode;
        NudgePanel.Visibility = mode.Nudges() ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Refresh()
    {
        var store = _app.Calibrations;
        string? selected = (ProfileList.SelectedItem as Row)?.Id;

        ProfileList.ItemsSource = store.All
            .Select(p => new Row(p.Id, p.Name, Describe(p), p == store.Active ? Visibility.Visible : Visibility.Collapsed))
            .ToList();
        ProfileList.SelectedItem = ProfileList.Items.Cast<Row>().FirstOrDefault(r => r.Id == selected)
                                   ?? ProfileList.Items.Cast<Row>().FirstOrDefault();

        bool none = store.All.Count == 0;
        TitleText.Text = none ? "Calibrate mouSEE?" : "Calibrations";
        IntroText.Text = none
            ? "You haven't calibrated yet. Calibration takes about three minutes: an eye check, then you look at a dot as it moves around the screen. " +
              "Without it mouSEE uses default values, so the cursor won't line up well with where you look."
            : "mouSEE uses the calibration marked in use. Pick another one to switch, or make a new one if " +
              "the lighting, your seat or the screen changed.";
        NewButton.Content = none ? "Calibrate now" : "New calibration";
        CloseButton.Content = none ? "Use default values" : "Close";
        ProfileList.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
        UseButton.Visibility = DeleteButton.Visibility = TestButton.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
        // only matters while there's none, once one is saved it never asks
        AskCheckBox.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
        AskCheckBox.IsChecked = _app.Settings.Current.Calibration.AskWhenMissing;
        UpdateButtons();

        if (none)
        {
            Choice.Ask("Calibrate now", "Use default values");
            // the question is for people who may not be able to use a mouse, so say it too, once
            if (!_askedOutLoud)
            {
                _askedOutLoud = true;
                _ = _speaker.SayAsync("mouSEE isn't calibrated yet. To calibrate now, look to the right and hold it for three seconds. " +
                                      "To use default values, look to the left.", CancellationToken.None);
            }
        }
        else
        {
            Choice.Stop();
        }
    }

    private string Describe(CalibrationProfile p)
    {
        string made = $"Made {p.CreatedAt.LocalDateTime.ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture)}";
        // a calibration only fits the screen it was made on
        bool otherScreen = p.ScreenWidth > 0 &&
                           (Math.Abs(p.ScreenWidth - SystemParameters.PrimaryScreenWidth) > 1 ||
                            Math.Abs(p.ScreenHeight - SystemParameters.PrimaryScreenHeight) > 1);
        return otherScreen ? $"{made}, on a {p.ScreenWidth:0} x {p.ScreenHeight:0} screen" : made;
    }

    private void UpdateButtons()
    {
        var row = ProfileList.SelectedItem as Row;
        UseButton.IsEnabled = row is not null && row.InUseVisibility != Visibility.Visible;
        DeleteButton.IsEnabled = row is not null;
        FineTuneButton.IsEnabled = _app.Calibrations.Active is not null;
    }

    private void OnSettingsChanged(AppSettings old, AppSettings now)
    {
        if (old.Calibration != now.Calibration) AskCheckBox.IsChecked = now.Calibration.AskWhenMissing;
        if (old.Head.Mode != now.Head.Mode) ShowHeadMode(now.Head.Mode);
    }

    private void ProfileList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateButtons();

    private void ProfileList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => UseSelected();

    private void UseButton_Click(object sender, RoutedEventArgs e) => UseSelected();

    private void UseSelected()
    {
        if (ProfileList.SelectedItem is Row row) _app.Calibrations.Use(row.Id);
    }

    private void NewButton_Click(object sender, RoutedEventArgs e) => StartNew();

    private void TestButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _app.StartAccuracyTest();
    }

    private void FineTuneButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _app.StartFineTune();
    }

    private void StartNew()
    {
        Close();
        _app.StartCalibration();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not Row row) return;
        bool inUse = row.InUseVisibility == Visibility.Visible;
        string question = inUse && _app.Calibrations.All.Count > 1
            ? $"Delete \"{row.Name}\"? It's the one in use, mouSEE switches to the one you used before it."
            : inUse
                ? $"Delete \"{row.Name}\"? It's your only calibration, mouSEE goes back to default values."
                : $"Delete \"{row.Name}\"?";
        if (MessageBox.Show(this, question, "Delete calibration", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _app.Calibrations.Delete(row.Id);
    }

    private void AskCheckBox_Click(object sender, RoutedEventArgs e)
    {
        bool ask = AskCheckBox.IsChecked == true;
        _app.Settings.Update(s => s with { Calibration = s.Calibration with { AskWhenMissing = ask } });
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
