# Action wheel

The action wheel lets a user choose a mouse action by looking at a wedge. It opens at the current gaze position after the user looks steadily at one spot for one second, or manually with **Ctrl+Alt+W**. While it is open, gaze selects wheel options instead of moving the system cursor.

Look at a wedge for about 900 ms to select it. The selected option is highlighted, its description appears in the center, and a progress arc shows the dwell. Looking into the center or away from the wheel resets the selection. The center marks the cursor target; after a non-cancel choice, the cursor is moved back to that point.

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
