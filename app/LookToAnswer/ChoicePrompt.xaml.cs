using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace app.LookToAnswer;

// Yes / no by looking: right for yes, left for no, held for 3 seconds (LookChoice). Put one in a window,
// call Ask with what yes and no mean, and handle Chosen. Buttons, Enter and Esc keep working next to it,
// this is for people who can't use them.
//
// On a screen where you look around before answering (dots near the edges), use AskAfterLooking instead: looking
// left or right only counts after the eyes have been closed for a moment (ClosedEyesGate), so looking at a dot
// near the edge isn't taken as an answer.
public partial class ChoicePrompt : UserControl
{
    private readonly App _app = (App)Application.Current;
    private readonly LookChoice _choice = new();
    private readonly ClosedEyesGate _gate = new();
    private bool _listening;
    // AskAfterLooking, and the eyes haven't been closed yet
    private bool _waitingForDone;
    // since when the eyes have to come to the middle (a new question, or the eyes were just closed)
    private readonly Stopwatch _sinceAsked = new();
    private bool _wantsMiddle;

    // the eyes usually are in the middle already and it counts on the first frame, so the middle dot only shows when
    // they weren't for this long. otherwise it flashes up for a frame every time
    public const int MiddleDotAfterMs = 400;

    /// <summary>true for yes, false for no. Fires once per Ask.</summary>
    public event Action<bool>? Chosen;

    /// <summary>AskAfterLooking: the eyes were closed, looking left or right counts from now. Say the question here.</summary>
    public event Action? Ready;

    /// <summary>true while it waits for the eyes to come to the middle of the screen, for the window's MiddleDot (MiddleDot.Follow).</summary>
    public event Action<bool>? WantsMiddleChanged;

    /// <summary>Set by MiddleDot.Follow: the window shows a dot in the middle, so the hint can point at it.</summary>
    public bool HasMiddleDot { get; set; }

    public ChoicePrompt()
    {
        InitializeComponent();
        _choice.Changed += Redraw;
        _choice.Chosen += yes =>
        {
            Stop();
            Chosen?.Invoke(yes);
        };
        _gate.Changed += Redraw;
        _gate.Done += () =>
        {
            _waitingForDone = false;
            _choice.Reset();
            _sinceAsked.Restart();
            Speaker.Ding();
            Redraw();
            Ready?.Invoke();
        };
        Unloaded += (_, _) => Stop();
    }

    /// <summary>Shows the question's two answers and starts listening. Nothing counts until the eyes have been in the middle.</summary>
    public void Ask(string yes, string no) => Start(yes, no, waitForDone: false);

    /// <summary>Like Ask, but for a screen you look around on first: left and right only count after the eyes have been
    /// closed for ClosedEyesGate.HoldMs. Ready fires then.</summary>
    public void AskAfterLooking(string yes, string no) => Start(yes, no, waitForDone: true);

    /// <summary>Stops listening and hides it.</summary>
    public void Stop()
    {
        if (_listening)
        {
            _app.Tracker.MessageReceived -= OnFrame;
            _listening = false;
        }
        _waitingForDone = false;
        Visibility = Visibility.Collapsed;
        SetWantsMiddle(false);
    }

    private void Start(string yes, string no, bool waitForDone)
    {
        YesCaption.Text = yes;
        NoCaption.Text = no;
        _waitingForDone = waitForDone;
        _choice.Reset();
        _gate.Reset();
        _sinceAsked.Restart();
        if (!_listening)
        {
            _app.Tracker.MessageReceived += OnFrame;
            _listening = true;
        }
        Visibility = Visibility.Visible;
        Redraw();
    }

    // while waiting for "done looking" only the closed eyes count, so looking at the sides does nothing
    private void OnFrame(TrackerMessage msg)
    {
        if (_waitingForDone) _gate.OnFrame(msg);
        else _choice.OnFrame(msg);
        SetWantsMiddle(!_waitingForDone && !_choice.Armed && _sinceAsked.ElapsedMilliseconds >= MiddleDotAfterMs);
    }

    private void SetWantsMiddle(bool wants)
    {
        if (wants == _wantsMiddle) return;
        _wantsMiddle = wants;
        WantsMiddleChanged?.Invoke(wants);
    }

    private void Redraw()
    {
        NoSide.Opacity = YesSide.Opacity = _waitingForDone ? 0.4 : 1;
        DoneBar.Visibility = _waitingForDone ? Visibility.Visible : Visibility.Collapsed;
        DoneFill.Width = _gate.Progress * DoneBar.Width;

        double yes = !_waitingForDone && _choice.Side == LookSide.Right ? _choice.Progress : 0;
        double no = !_waitingForDone && _choice.Side == LookSide.Left ? _choice.Progress : 0;
        YesFill.Width = yes * ((FrameworkElement)YesFill.Parent).ActualWidth;
        NoFill.Width = no * ((FrameworkElement)NoFill.Parent).ActualWidth;
        HintText.Text = _waitingForDone
            ? $"Look around as long as you like. When you're done, close your eyes for {ClosedEyesGate.HoldMs / 1000.0:0.#} seconds to answer."
            : _choice.Armed
                ? $"Look to the right for yes, to the left for no, and hold it for {LookChoice.HoldMs / 1000} seconds."
                : HasMiddleDot ? "Look at the dot in the middle of the screen first." : "Look at the middle of the screen first.";
    }
}
