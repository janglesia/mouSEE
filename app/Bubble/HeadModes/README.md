# HeadModes/

What moving your head does to the bubble, three modes to try against each other, picked in the calibrations window. See [docs/bubble/HEAD-MODES.md](../../../docs/bubble/HEAD-MODES.md).

| | |
|---|---|
| `HeadMode.cs` | the three modes (Eyes only, Eyes + head nudge, Eyes + head nudge without head correction) and which of the two switches each turns on |
| `HeadNudge.cs` | the nudge: the eyes stop on a spot, the bubble holds, turning or tilting the head moves it |

The head correction happens in the tracker (`tracker/gaze_model.py`), `--no-head-compensation` turns it off. Tests: `tests/app.Tests/Bubble/HeadModes/`.
