# Calibration and Validation Targets Specification

This document outlines the normalized coordinates, mapping formulas, and implementation details for the calibration and validation targets used in `GridWindow.xaml.cs`.

---

## 1. Normalized Calibration Points

The calibration routine uses 20 normalized points: 5 columns by 4 rows, 7% from the edges (`EdgeMargin = 0.07`, `Columns = 5`, `Rows = 4`, built by `BuildTargets()`). It was 9 points (3 by 3, 8% from the edges) until 10/19/2026; with only 3 rows the gaze fit couldn't see the curves in up/down, see [GAZE-FIT.md](GAZE-FIT.md#the-grid).

```
x:  0.07    0.285   0.50    0.715   0.93
y:  0.07
    0.3567
    0.6433
    0.93
```

### Traversal Pattern & Sequence
Snake order, so the target never jumps back across the screen: the first row left to right, the second right to left, the third left to right, the fourth right to left.

1-5. top row, `(0.07, 0.07)` to `(0.93, 0.07)`
6-10. second row, `(0.93, 0.3567)` to `(0.07, 0.3567)`
11-15. third row, `(0.07, 0.6433)` to `(0.93, 0.6433)`
16-20. bottom row, `(0.93, 0.93)` to `(0.07, 0.93)`

After the grid comes the head step: 8 dots while leaning and tilting (`HeadPose.Step` in `app/Calibration/GazeGrid/CalibrationTarget.cs`).

---

## 2. Validation Targets

In addition to the primary calibration grid, 4 extra validation targets are evaluated in this sequence:
- $(0.25, 0.25)$ (Top-Left quadrant)
- $(0.75, 0.25)$ (Top-Right quadrant)
- $(0.75, 0.75)$ (Bottom-Right quadrant)
- $(0.25, 0.75)$ (Bottom-Left quadrant)

---

## 3. Pixel Coordinate Calculation

For any target coordinate $(X, Y)$ and a screen resolution of width $W$ and height $H$, the actual position is calculated as:
- $x = X \times W$
- $y = Y \times H$

### Example: Standard 1920 $\times$ 1080 Screen (at 100% Windows Scaling)
- columns: $x$ = 134.4, 547.2, 960.0, 1372.8, 1785.6
- rows: $y$ = 75.6, 385.2, 694.8, 1004.4

---

## 4. Important Implementation Notes

* **Windows Scaling & WPF Units:** 
  The pixel example above holds strictly at 100% Windows scaling. Because WPF operates in **device-independent pixels** rather than physical pixels, scaling affects coordinate values. For example, at 125% scaling, a nominal 1920 $\times$ 1080 screen yields $1536 \times 864$ WPF units, meaning the $0.07$ coordinate lands at $107.5 \times 60.5$. 
  * *Action Item:* Anyone measuring calibration/validation errors must pull dimensions using `SystemParameters.PrimaryScreenWidth` and `SystemParameters.PrimaryScreenHeight` rather than relying on raw physical resolutions.

* **Logo Center Alignment:** 
  Each target coordinate represents the exact center point of the logo dot, as the underlying code centers the logo element directly on $X \times W$ and $Y \times H$. The window covers the entire primary screen, making the normalized fractions relative to the full screen bounds.

* **Animation Side Effect (Clipping):** 
  The logo initiates its entrance animation at 1.5$\times$ scale before shrinking down. On screens shorter than roughly $1125$ units, the outer ring of the top and bottom targets may experience minor pixel clipping at the very beginning of the shrink animation. This is purely cosmetic and entirely harmless, as the target is fully visible by the time the ring begins filling.