using app.Input;

namespace app.Tests.Input;

file sealed class FakeScrollInjector : IScrollInjector
{
    public List<int> Deltas { get; } = [];

    public bool Scroll(int wheelDelta)
    {
        Deltas.Add(wheelDelta);
        return true;
    }
}

public class ScrollModeTests
{
    [Fact]
    public void Activation_requires_the_configured_dwell()
    {
        var injector = new FakeScrollInjector();
        var mode = new ScrollModeController(injector, new ScrollSettings
        {ActivationDwellMs = 900,});

        mode.Update(100, ScrollZone.Activation);
        Assert.False(mode.IsActive);

        mode.Update(1000, ScrollZone.Activation);
        Assert.True(mode.IsActive);
    }

    [Fact]
    public void Leaving_activation_during_dwell_restarts_it()
    {
        var injector = new FakeScrollInjector();
        var mode = new ScrollModeController(injector, new ScrollSettings{ActivationDwellMs = 900, LeaveMs = 150,});

        mode.Update(100, ScrollZone.Activation);
        mode.Update(300, ScrollZone.Outside);
        mode.Update(466, ScrollZone.Outside);

        Assert.False(mode.IsActive);
        Assert.Equal(0, mode.ActivationProgress);
    }

    [Fact]
    public void Upward_zone_emits_positive_wheel_deltas()
    {
        var injector = new FakeScrollInjector();
        var mode = new ScrollModeController(injector, new ScrollSettings{ActivationDwellMs = 100, TickIntervalMs = 250,});

        mode.Update(100, ScrollZone.Activation);
        mode.Update(200, ScrollZone.Activation);
        mode.Update(250, ScrollZone.Up);

        Assert.Equal([120], injector.Deltas);
    }

    [Fact]
    public void Downward_zone_emits_negative_wheel_deltas()
    {
        var injector = new FakeScrollInjector();
        var mode = new ScrollModeController(injector, new ScrollSettings{ActivationDwellMs = 100, TickIntervalMs = 250,});

        mode.Update(100, ScrollZone.Activation);
        mode.Update(200, ScrollZone.Activation);
        mode.Update(250, ScrollZone.Down);

        Assert.Equal([-120], injector.Deltas);
    }

    [Fact]
    public void Both_directions_use_the_same_default_notch()
    {
        var settings = new ScrollSettings();

        Assert.Equal(120, settings.WheelDelta);
        Assert.Equal(250, settings.TickIntervalMs);
    }

    [Fact]
    public void Hidden_zone_panel_disables_activation_and_direction()
    {
        var injector = new FakeScrollInjector();
        var mode = new ScrollModeController(injector);

        mode.SetZonePanelVisible(false);
        mode.Update(100, ScrollZone.Activation);
        mode.Update(1000, ScrollZone.Activation);
        mode.Update(1250, ScrollZone.Down);

        Assert.False(mode.IsActive);
        Assert.Equal(ScrollDirection.None, mode.Direction);
        Assert.Empty(injector.Deltas);
    }
}