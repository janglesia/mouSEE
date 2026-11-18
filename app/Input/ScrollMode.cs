namespace app.Input;

// UI-neutral zone identities used by the scroll-mode panel
public enum ScrollZone
{
    Outside,
    Activation,
    Up,
    Down,
}

// maps normalized gaze coordinates to the scroll panel's zone
// the concrete implementation must be done after UI
public interface IScrollZoneHitTester
{
    ScrollZone HitTest(double normalizedX, double normalizedY);
}

// the direction most recently selected while scroll mode is active
public enum ScrollDirection
{
    None,
    Up,
    Down,
}

// coordinates activation dwell and repeated upward/downward page scrolling
// the scroll zone panel is permanently available through a collapsible tab.
// opening the panel makes activation available
// the activation zone must receive a continuous dwell before
// the upwards/downwards zones becomes active. 
public sealed class ScrollModeController
{
    private readonly IScrollInjector _injector;
    private readonly GazeDwell _activationDwell;
    private long _lastTickMs = long.MinValue / 2;

    public ScrollModeController(IScrollInjector injector, ScrollSettings? settings = null)
    {
        _injector = injector;
        
        if(settings == null){
            Settings = new ScrollSettings();
        }
        else{
            Settings = settings;
        }
        
        _activationDwell = new GazeDwell(Settings.ActivationDwellMs, Settings.LeaveMs);
    }

    public ScrollSettings Settings { get; private set; }
    public bool IsZonePanelVisible { get; private set; } = true;
    public bool IsActive { get; private set; }
    public double ActivationProgress => _activationDwell.Progress;
    public ScrollDirection Direction { get; private set; } = ScrollDirection.None;

    // updates scroll-mode configuration without recreating the dwell controller
    public void ApplySettings(ScrollSettings settings)
    {
        Settings = settings;
        _activationDwell.DwellMs = settings.ActivationDwellMs;
        _activationDwell.LeaveMs = settings.LeaveMs;
    }

    // updates the state of the collapsible zone panel
    // UI is responsible for drawing, hit-testing, and invoking this method from the tab
    public void SetZonePanelVisible(bool visible)
    {
        IsZonePanelVisible = visible;
        Direction = ScrollDirection.None;
        _lastTickMs = long.MinValue / 2;

        if(!visible){
            IsActive = false;
            _activationDwell.Reset();
        }
    }

    // processes the zone selected by the current gaze position
    // call this repeatedly while tracking is producing gaze updates
    public void Update(long nowMs, ScrollZone zone)
    {
        if(!IsZonePanelVisible){
            return;
        }

        if(!IsActive){
            bool activated;
            if(zone == ScrollZone.Activation){
                activated = _activationDwell.Update(nowMs, 0);
            }
            else{
                activated = _activationDwell.Update(nowMs, null);
            }

            if(activated){
                IsActive = true;
            }

            return;
        }

        Direction = zone switch
        {
            ScrollZone.Up => ScrollDirection.Up,
            ScrollZone.Down => ScrollDirection.Down,
            _ => ScrollDirection.None,
        };

        if(Direction == ScrollDirection.None){
            return;
        }

        if(_lastTickMs != long.MinValue / 2 && nowMs - _lastTickMs < Settings.TickIntervalMs){
            return;
        }

        int wheelDelta;
        if(Direction == ScrollDirection.Up){
            wheelDelta = Settings.WheelDelta;
        }
        else{
            wheelDelta = -Settings.WheelDelta;
        }

        _injector.Scroll(wheelDelta);
        _lastTickMs = nowMs;
    }

    // resets activation and direction (currently not yet implemented the exit/cancel behavior)
    public void Reset()
    {
        IsActive = false;
        Direction = ScrollDirection.None;
        _lastTickMs = long.MinValue / 2;
        _activationDwell.Reset();
    }
}