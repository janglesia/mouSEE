using System.Windows;
using System.Windows.Controls;
using app.Gaze;

namespace app.UI;

// Yes / no by looking: right for yes, left for no, held for 3 seconds (LookChoice). Put one in a window,
// call Ask with what yes and no mean, and handle Chosen. Buttons, Enter and Esc keep working next to it,
// this is for people who can't use them.
public partial class ChoicePrompt : UserControl
{
    private readonly App _app = (App)Application.Current;
    private readonly LookChoice _choice = new();
    private bool _listening;

    /// <summary>true for yes, false for no. Fires once per Ask.</summary>
    public event Action<bool>? Chosen;

    public ChoicePrompt()
    {
        InitializeComponent();
        _choice.Changed += Redraw;
        _choice.Chosen += yes =>
        {
            Stop();
            Chosen?.Invoke(yes);
        };
        Unloaded += (_, _) => Stop();
    }

    /// <summary>Shows the question's two answers and starts listening. Nothing counts until the eyes have been in the middle.</summary>
    public void Ask(string yes, string no)
    {
        YesCaption.Text = yes;
        NoCaption.Text = no;
        _choice.Reset();
        if (!_listening)
        {
            _app.Tracker.MessageReceived += _choice.OnFrame;
            _listening = true;
        }
        Visibility = Visibility.Visible;
        Redraw();
    }

    /// <summary>Stops listening and hides it.</summary>
    public void Stop()
    {
        if (_listening)
        {
            _app.Tracker.MessageReceived -= _choice.OnFrame;
            _listening = false;
        }
        Visibility = Visibility.Collapsed;
    }

    private void Redraw()
    {
        double yes = _choice.Side == LookSide.Right ? _choice.Progress : 0;
        double no = _choice.Side == LookSide.Left ? _choice.Progress : 0;
        YesFill.Width = yes * ((FrameworkElement)YesFill.Parent).ActualWidth;
        NoFill.Width = no * ((FrameworkElement)NoFill.Parent).ActualWidth;
        HintText.Text = _choice.Armed
            ? $"Look to the right for yes, to the left for no, and hold it for {LookChoice.HoldMs / 1000} seconds."
            : "Look at the middle of the screen first.";
    }
}
