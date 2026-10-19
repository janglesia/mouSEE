# HeadModes/

What moving your head does to the bubble, three modes to try against each other, picked in the calibrations window. See [docs/bubble/HEAD-MODES.md](../../../docs/bubble/HEAD-MODES.md).

| | |
|---|---|
| `HeadMode.cs` | the three modes (cancel, nudge, both) and which part each turns on |
| `HeadNudge.cs` | the nudge: the eyes stop on a spot, the bubble holds, turning or tilting the head moves it |

Cancelling happens in the tracker (`tracker/gaze_model.py`), `--no-head-compensation` turns it off. Tests: `tests/app.Tests/Bubble/HeadModes/`.
