using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using app.Calibration;

namespace app.UI;

public partial class CalibrationWindow : Window
{
    private readonly Ellipse[] _targets;
    private int _activeTarget = -1;
    private bool _previewComplete;
    private bool _sessionStarted;
    private bool _sessionEnded;
    private int? _backendCompletedTargets;
    private int? _backendTotalTargets;
    private string? _backendStatus;

    public event Action? CalibrationStarted;
    public event Action<CalibrationTarget>? TargetPresented;
    public event Action<CalibrationTarget>? TargetAccepted;
    public event Action? CalibrationCompleted;
    public event Action? CalibrationCancelled;

    public CalibrationWindow()
    {
        InitializeComponent();
        _targets = [Target00, Target01, Target02, Target10, Target11, Target12, Target20, Target21, Target22];
        UpdatePreview();
        Closing += (_, _) =>
        {
            if (_sessionStarted && !_sessionEnded)
            {
                _sessionEnded = true;
                CalibrationCancelled?.Invoke();
            }
        };
    }

    public void ReportBackendProgress(int completedTargets, int totalTargets)
    {
        if (totalTargets <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalTargets));
        }

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => ReportBackendProgress(completedTargets, totalTargets));
            return;
        }

        _backendTotalTargets = totalTargets;
        _backendCompletedTargets = Math.Clamp(completedTargets, 0, totalTargets);
        UpdatePreview();
    }

    public void ReportBackendStatus(string status)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => ReportBackendStatus(status));
            return;
        }

        _backendStatus = status;
        UpdatePreview();
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_previewComplete || _activeTarget < 0)
        {
            BeginSession();
            return;
        }

        TargetAccepted?.Invoke(GetActiveTarget());
        if (_activeTarget < _targets.Length - 1)
        {
            _activeTarget++;
            UpdatePreview();
            TargetPresented?.Invoke(GetActiveTarget());
            return;
        }

        _previewComplete = true;
        _sessionEnded = true;
        UpdatePreview();
        CalibrationCompleted?.Invoke();
    }

    private void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        if (_previewComplete)
        {
            _previewComplete = false;
        }

        if (_activeTarget > 0)
        {
            _activeTarget--;
            _sessionEnded = false;
            UpdatePreview();
            TargetPresented?.Invoke(GetActiveTarget());
            return;
        }

        UpdatePreview();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void BeginSession()
    {
        _activeTarget = 0;
        _previewComplete = false;
        _sessionStarted = true;
        _sessionEnded = false;
        _backendCompletedTargets = null;
        _backendTotalTargets = null;
        _backendStatus = null;

        CalibrationStarted?.Invoke();
        UpdatePreview();
        TargetPresented?.Invoke(GetActiveTarget());
    }

    private CalibrationTarget GetActiveTarget()
    {
        int row = _activeTarget / 3;
        int column = _activeTarget % 3;
        return new CalibrationTarget(_activeTarget, (column + 0.5) / 3, (row + 0.5) / 3);
    }

    private void UpdatePreview()
    {
        for (int i = 0; i < _targets.Length; i++)
        {
            bool active = i == _activeTarget;
            _targets[i].Opacity = active ? 0 : 1;
        }

        if (_activeTarget >= 0)
        {
            Grid.SetRow(ActiveTargetLogo, _activeTarget / 3);
            Grid.SetColumn(ActiveTargetLogo, _activeTarget % 3);
            ActiveTargetLogo.Visibility = Visibility.Visible;
        }
        else
        {
            ActiveTargetLogo.Visibility = Visibility.Collapsed;
        }

        bool started = _activeTarget >= 0;
        PhaseText.Text = _previewComplete ? "PREVIEW COMPLETE" : started ? "STEP 02  /  TARGETS" : "STEP 01  /  PREPARE";
        HeadlineText.Text = _previewComplete ? "Sequence previewed" : started ? $"Target {_activeTarget + 1} of {_targets.Length}" : "Set up your viewing position";
        DescriptionText.Text = _previewComplete
            ? "The nine-point sequence is ready to connect to calibration capture when that service is available."
            : started
                ? "This is a visual walkthrough only. Use Next to move the highlighted marker across the target field."
                : "Use the target field to preview the calibration flow. Keep your head comfortably still and centered on screen.";

        TargetCountText.Text = _previewComplete ? "COMPLETE" : started ? $"POINT {_activeTarget + 1:00} / 09" : "READY";
        ProgressCountText.Text = $"{(_previewComplete ? _targets.Length : Math.Max(0, _activeTarget + 1))} / {_targets.Length}";
        SequenceProgress.Value = _previewComplete ? _targets.Length : Math.Max(0, _activeTarget + 1);
        CurrentTargetText.Text = _previewComplete ? "Preview finished" : started ? $"Point {_activeTarget + 1}" : "Waiting to begin";
        TargetHintText.Text = _previewComplete
            ? "No gaze samples were taken or saved."
            : started
                ? "The highlighted point is for layout preview; gaze capture is not active."
                : "Start the preview to highlight the first point in the target field.";

        if (_backendTotalTargets is int totalTargets && _backendCompletedTargets is int completedTargets)
        {
            SequenceProgress.Maximum = totalTargets;
            SequenceProgress.Value = completedTargets;
            ProgressCountText.Text = $"{completedTargets} / {totalTargets}";
            TargetCountText.Text = $"CAPTURED {completedTargets:00} / {totalTargets:00}";
        }

        if (_backendStatus is not null)
        {
            TargetHintText.Text = _backendStatus;
        }

        PreviousButton.IsEnabled = _activeTarget > 0 || _previewComplete;
        NextButton.Content = _previewComplete ? "Restart preview" : started ? "Next target" : "Begin preview";
    }
}