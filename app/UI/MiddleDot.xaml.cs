using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace app.UI;

// The dot to look at whenever a screen asks you to look at the middle: the eye check's first step and its head tilt
// steps, the re-center, and while a look left / right question waits for your eyes to come to the middle first.
// Put one in a full-screen window's top Grid (it centres itself) and call Show. Its colour is worked out from the
// background behind it (UiColors.MiddleDot), again whenever that background changes.
public partial class MiddleDot : UserControl
{
    // what wants it shown right now (a step, a question...), it's visible while anything does
    private readonly HashSet<object> _wantedBy = [];
    private FrameworkElement? _backgroundOwner;
    private DependencyPropertyDescriptor? _watching;

    public MiddleDot()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Watch();
            Recolor();
        };
        Unloaded += (_, _) => _watching?.RemoveValueChanged(_backgroundOwner!, OnBackgroundChanged);
    }

    /// <summary>Shows it while anything wants it: Show(this, true) from a step, Show(prompt, false) when the question is answered.</summary>
    public void Show(object wantedBy, bool show)
    {
        if (show) _wantedBy.Add(wantedBy);
        else _wantedBy.Remove(wantedBy);
        Visibility = _wantedBy.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (Visibility == Visibility.Visible) Recolor();
    }

    /// <summary>Shows it while this question waits for the eyes to come to the middle.</summary>
    public void Follow(ChoicePrompt prompt)
    {
        prompt.HasMiddleDot = true;
        prompt.WantsMiddleChanged += wants => Show(prompt, wants);
    }

    private void Recolor()
    {
        var background = BackgroundBehind();
        var dot = new SolidColorBrush(UiColors.MiddleDot(background));
        Ring.Stroke = Center.Fill = dot;
        Halo.Fill = new SolidColorBrush(background);
    }

    private void OnBackgroundChanged(object? sender, EventArgs e) => Recolor();

    // the first solid background going up from here: a Panel's, a Control's (the window), or a Border's
    private Color BackgroundBehind()
    {
        for (DependencyObject? at = VisualTreeHelper.GetParent(this); at is not null; at = VisualTreeHelper.GetParent(at))
        {
            var brush = at switch
            {
                Panel p => p.Background,
                Control c => c.Background,
                Border b => b.Background,
                _ => null,
            };
            if (brush is SolidColorBrush { Color.A: > 0 } solid) return solid.Color;
        }
        return Colors.Black;
    }

    // keep the colour right when the background changes (the eye check goes bright for one step)
    private void Watch()
    {
        for (DependencyObject? at = VisualTreeHelper.GetParent(this); at is not null; at = VisualTreeHelper.GetParent(at))
        {
            var property = at switch
            {
                Panel p when p.Background is SolidColorBrush { Color.A: > 0 } => Panel.BackgroundProperty,
                Control c when c.Background is SolidColorBrush { Color.A: > 0 } => Control.BackgroundProperty,
                _ => null,
            };
            if (property is null) continue;
            _backgroundOwner = (FrameworkElement)at;
            _watching = DependencyPropertyDescriptor.FromProperty(property, at.GetType());
            _watching.AddValueChanged(_backgroundOwner, OnBackgroundChanged);
            return;
        }
    }
}
