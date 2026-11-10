# Action wheel

The action wheel lets a user choose a mouse action by looking at a wedge. It opens at the current gaze position after the user looks steadily at one spot for one second, or manually with **Ctrl+Alt+W**. While it is open, gaze selects wheel options instead of moving the system cursor.

Opening it by looking only works while the cursor follows your eyes (**Ctrl+Alt+M**, and tracking is good), and only in the **Eyes only** head mode. In the two head nudge modes, resting your eyes on a spot already makes the bubble hold there, so both would go off at once. **Ctrl+Alt+W** works in every mode.

Look at a wedge for about 900 ms to select it. The selected option is highlighted, its description appears in the center, and a progress arc shows the dwell. Looking into the center or away from the wheel for 150 ms resets the selection. A shorter glance keeps it and its time, so one stray frame of the shaky gaze doesn't start it over.

The timing is in `GazeDwell` (`app/Input/GazeDwell.cs`), with the numbers in `WheelSettings` (`app/Settings/AppSettings.cs`): 1000 ms within 35 px to open, 900 ms to select, 150 ms to count as looking away, and 800 ms after closing before it can open again. They're fixed for now. `WheelSettings` says what to do to make them settings. The center marks the cursor target; after a non-cancel choice, the cursor is moved back to that point.

## Options

| Option | Description |
|---|---|
| Left Click | Standard single mouse click |
| Double Click | Double-click at current cursor |
| Right Click | Open context menus |
| Drag | Hold left mouse button until released |
| Scroll | Enter scroll mode; look up/down to scroll |
| Keyboard | Open an on-screen keyboard |
| Cancel | Close the wheel without performing an action |

Cancel closes the wheel without raising an action-selection event. The action names and descriptions are displayed by the wheel; the corresponding mouse, scroll, drag, and keyboard behaviors still need to be connected in `OverlayWindow`.
