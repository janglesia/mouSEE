# Scroll mode backend handoff

This document describes the scroll backend work completed for MOU-64, MOU-65, and MOU-66.
It also records what must be completed by the scroll-zone UI tickets and the other tickets
that are explicitly outside this implementation.

No scroll UI has been added or modified. The repository is left with the backend code and
integration hooks described below.

## Implemented behavior

### Activation and scroll state

[app/Input/ScrollMode.cs](../app/Input/ScrollMode.cs) contains:

- `ScrollZone`: `Outside`, `Activation`, `Up`, and `Down`
- `ScrollDirection`: `None`, `Up`, and `Down`
- `IScrollZoneHitTester`: the UI-neutral contract for mapping normalized gaze coordinates to a scroll zone
- `ScrollModeController`: activation dwell, active-state, direction-state, and repeated-scroll timing

The controller uses the existing [GazeDwell](../app/Input/GazeDwell.cs) implementation.
Activation requires the gaze to remain in `ScrollZone.Activation` for
`ScrollSettings.ActivationDwellMs`. Brief movement is tolerated according to
`ScrollSettings.LeaveMs`.

After activation:

- `ScrollZone.Up` produces a positive wheel delta.
- `ScrollZone.Down` produces a negative wheel delta.
- Any other zone sets `Direction` to `None`.
- Wheel ticks are emitted no more often than `ScrollSettings.TickIntervalMs`.

### Windows wheel input

[app/Input/MouseScroller.cs](../app/Input/MouseScroller.cs) contains
`IScrollInjector` and `MouseScroller`.

`MouseScroller.Scroll(int wheelDelta)` sends `MOUSEEVENTF_WHEEL` through `SendInput`.
The sign convention is:

- positive: scroll upward
- negative: scroll downward

Both directions use the same absolute value from `ScrollSettings.WheelDelta`.

### Backend defaults

[app/Settings/AppSettings.cs](../app/Settings/AppSettings.cs) contains
`ScrollSettings` with these defaults:

| Setting | Default | Meaning |
|---|---:|---|
| `ActivationDwellMs` | 900 ms | Time required in the center activation zone |
| `LeaveMs` | 150 ms | Time to tolerate briefly leaving a zone |
| `WheelDelta` | 120 | Standard Windows wheel notch used by up and down |
| `TickIntervalMs` | 250 ms | Minimum time between repeated wheel ticks |

These values are internal for now. They are not part of `AppSettings`, the settings
catalog, settings storage, or the settings UI.

### Overlay integration

[app/UI/OverlayWindow.xaml.cs](../app/UI/OverlayWindow.xaml.cs) currently
contains:

- one `ScrollModeController`
- one `MouseScroller`
- `IScrollZoneHitTester? ScrollZoneHitTester`
- `UpdateScrollZone(long nowMs, ScrollZone zone)`
- `SetScrollZonePanelVisible(bool visible)`
- gaze-loop code that sends the classified zone to the controller while the action wheel is closed

The code in `UpdateGazeBubble` calls the hit-tester on each gaze update when it is assigned.
The normalized gaze X and Y values are divided by `GazeCanvas.ActualWidth` and
`GazeCanvas.ActualHeight` before `HitTest` is called.

### Tests

[tests/app.Tests/Input/ScrollModeTests.cs](../tests/app.Tests/Input/ScrollModeTests.cs)
contains fake-injector tests for:

- activation dwell
- restarting after gaze leaves activation
- positive upward wheel delta
- negative downward wheel delta
- equal default wheel-notch magnitude
- hidden-panel behavior

## What the UI team must complete

The following work is not implemented and is required before the scroll zones become
visible and usable:

1. Add a scroll-zone control to [app/UI/OverlayWindow.xaml](../app/UI/OverlayWindow.xaml).
2. Draw the panel permanently with a collapsible left-side tab.
3. When the tab reveals the panel, call:
   ```csharp
   SetScrollZonePanelVisible(true);
   ```
4. When the tab hides the panel, call:
   ```csharp
   SetScrollZonePanelVisible(false);
   ```
5. Implement a concrete class that implements:
   ```csharp
   public interface IScrollZoneHitTester
   {
       ScrollZone HitTest(double normalizedX, double normalizedY);
   }
   ```
6. Assign the implementation to:
   ```csharp
   ScrollZoneHitTester = ...;
   ```
7. The hit-tester must return:
   - `ScrollZone.Activation` for the center `S` region
   - `ScrollZone.Up` for the region above the activation region
   - `ScrollZone.Down` for the region below the activation region
   - `ScrollZone.Outside` everywhere else
8. The UI team should not duplicate activation or wheel injection. It should only map screen
   geometry to a zone and call the existing overlay hit-tester/zone-update path.

The concrete control, visual layout, hit-test geometry, and collapsible tab belong to the
scroll-zone/UI implementation tickets, principally MOU-62 and MOU-68.

## Current handoff state

The actual scroll-zone control is absent. Because no concrete `IScrollZoneHitTester` is
assigned, the overlay currently reports `ScrollZone.Outside` and activation scrolling cannot
start from the live gaze loop.

The action wheel still contains the obsolete Scroll entry:

- [app/UI/ActionWheel.xaml.cs:17](../app/UI/ActionWheel.xaml.cs)
- [app/UI/ActionWheel.xaml.cs:46](../app/UI/ActionWheel.xaml.cs)

It must not be used to enter scroll mode. Scroll mode is designed around the separate,
always-present scroll-zone panel.

The action-wheel Scroll handler in
[app/UI/OverlayWindow.xaml.cs:402](../app/UI/OverlayWindow.xaml.cs)
and the Scroll row in [ActionWheel.md:29](Wheel/ActionWheel.md)
also remain stale and must be removed by the follow-up cleanup ticket or implementation
work that reconciles the action wheel with this behavior.

## Explicitly excluded follow-up work

The following was not implemented by MOU-64, MOU-65, or MOU-66:

- MOU-67: stopping scrolling outside directional zones
- MOU-69: exit and cancel scroll-mode behavior
- MOU-62 and MOU-68: zone UI layout, tab UI, and UI-side tuning
- MOU-78 and MOU-98: settings screen UI and settings API
- MOU-99: tuning scrolling speed and thresholds in backend/settings
- MOU-58: preventing accidental action-wheel activation

The backend intentionally leaves panel visibility, exit/cancel, and outside-zone behavior
for those later responsibilities. `Reset()` is present as a backend reset primitive, but it
is not the implementation of MOU-69.

## Integration contract

Future UI code should use this sequence:

```csharp
// Reveal/hide the scroll panel.
SetScrollZonePanelVisible(true);

// From each gaze frame, after the UI maps normalized gaze coordinates to a zone:
UpdateScrollZone(nowMs, ScrollZone.Activation);

// Or, for active scroll mode:
UpdateScrollZone(nowMs, ScrollZone.Up);
UpdateScrollZone(nowMs, ScrollZone.Down);
UpdateScrollZone(nowMs, ScrollZone.Outside);
```

The UI must not call `MouseScroller` directly. That keeps wheel input behind
`ScrollModeController` and allows activation timing, repeated ticks, and future exit behavior
to remain centralized.
