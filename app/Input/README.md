# Input/

## CursorController

`CursorController` moves the actual Windows cursor using the Win32 `SetCursorPos` API.

### Usage

```csharp
var controller = new CursorController();

// Move to screen center (normalized coordinates 0-1)
controller.MoveToNormalized(0.5, 0.5);

// Move to top-left corner
controller.MoveToNormalized(0, 0);

// Move to bottom-right corner
controller.MoveToNormalized(1, 1);
```

### API

- `MoveToNormalized(double x, double y)` - Moves the cursor to normalized coordinates where (0,0) is top-left and (1,1) is bottom-right. Coordinates are automatically clamped to [0, 1]. Returns true if the cursor was moved successfully, false otherwise.

### Coordinate System

The controller uses normalized (0-1) coordinates, which match the coordinate system used throughout the app (e.g., for the gaze bubble). These are converted to screen pixels using `SystemParameters.PrimaryScreenWidth` and `SystemParameters.PrimaryScreenHeight`.

### Testing

The `ConvertToScreenCoordinates` method is internal and can be tested directly without calling the Win32 API. See `tests/app.Tests/Input/CursorControllerTests.cs`.

### Integration Notes

This is the infrastructure for cursor movement. It is not yet connected to gaze tracking. When gaze is connected:

- `CursorController` will be instantiated in `OverlayWindow`
- It will be called in `UpdateGazeBubble` when `TrackingMonitor.CanMoveCursor` is true
- It will use the same (x, y) coordinates that are currently used for the gaze bubble (after fine-tune, re-center, smoothing and head nudge)

### Related Settings

- `CursorSettings.Smoothing`, `CursorSettings.Sensitivity` - Smoothing the bubble, which the cursor follows ([docs/bubble/SMOOTHING.md](../../docs/bubble/SMOOTHING.md))
- `CursorSettings.ShowGazeBubble` - Whether to show the gaze bubble overlay
- `TrackingMonitor.CanMoveCursor` - Property that indicates when the cursor may move (only when tracking is stable)

## GazeDwell

`GazeDwell` picks a target the gaze stays on long enough, for the action wheel (opening it, and choosing an option).

- `Update(ms, target)`: the target the gaze is on (any number, null for none). It returns true once, when the time is up.
- `UpdateSpot(ms, x, y, radius)`: the same for a resting gaze. The target is a circle around where the gaze came to rest, and `Anchor` says where that is.
- It starts over only after the gaze has been off the target for `LeaveMs`, so one stray frame doesn't. Staying on another target that long moves the timer there.
- `DwellMs` and `LeaveMs` can change at any time. The wheel currently uses separate internal dwell durations, wheel-specific radius, leave, and cooldown values in `WheelSettings`. Tests: `tests/app.Tests/Input/GazeDwellTests.cs`.

## MouseClicker

`MouseClicker` performs a real Windows left-click with `SendInput`, at the current cursor position.
It implements `IClickInjector.LeftClick()` so triggers (dwell, blink) and tests can use a fake.

- Returns true only if Windows accepted both the press and the release.
- `OverlayWindow.TryClick()` is the one gate: only while `IsCursorMovementEnabled && TrackingMonitor.CanMoveCursor`.
- Temporary test hotkey: Ctrl+Alt+L (clicks after 1 s, so Ctrl+Alt are released first).
- Windows ignores injected clicks on elevated windows unless mouSEE runs elevated.

