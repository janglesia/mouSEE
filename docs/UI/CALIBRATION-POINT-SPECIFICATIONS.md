# Calibration and Validation Targets Specification

This document outlines the normalized coordinates, mapping formulas, and implementation details for the calibration and validation targets used in `CalibrationWindow.xaml.cs`.

---

## 1. Normalized Calibration Points

The calibration routine uses 9 normalized points mapped across the screen. 

```
(0.08, 0.08)    (0.50, 0.08)    (0.92, 0.08)
(0.08, 0.50)    (0.50, 0.50)    (0.92, 0.50)  <-- Note: Middle row runs right to left in code sequence
(0.08, 0.92)    (0.50, 0.92)    (0.92, 0.92)
```

### Traversal Pattern & Sequence
The targets are rendered in the following order:
1. `top-left` $(0.08, 0.08)$
2. `top-center` $(0.50, 0.08)$
3. `top-right` $(0.92, 0.08)$
4. `middle-right` $(0.92, 0.50)$
5. `center` $(0.50, 0.50)$
6. `middle-left` $(0.08, 0.50)$
7. `bottom-left` $(0.08, 0.92)$
8. `bottom-center` $(0.50, 0.92)$
9. `bottom-right` $(0.92, 0.92)$

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
- $(153.6, 86.4)$
- $(960.0, 86.4)$
- $(1766.4, 86.4)$
- $(1766.4, 540.0)$
- $(960.0, 540.0)$
- $(153.6, 540.0)$
- $(153.6, 993.6)$
- $(960.0, 993.6)$
- $(1766.4, 993.6)$

---

## 4. Important Implementation Notes

* **Windows Scaling & WPF Units:** 
  The pixel example above holds strictly at 100% Windows scaling. Because WPF operates in **device-independent pixels** rather than physical pixels, scaling affects coordinate values. For example, at 125% scaling, a nominal 1920 $\times$ 1080 screen yields $1536 \times 864$ WPF units, meaning the $0.08$ coordinate lands at $122.9 \times 69.1$. 
  * *Action Item:* Anyone measuring calibration/validation errors must pull dimensions using `SystemParameters.PrimaryScreenWidth` and `SystemParameters.PrimaryScreenHeight` rather than relying on raw physical resolutions.

* **Logo Center Alignment:** 
  Each target coordinate represents the exact center point of the logo dot, as the underlying code centers the logo element directly on $X \times W$ and $Y \times H$. The window covers the entire primary screen, making the normalized fractions relative to the full screen bounds.

* **Animation Side Effect (Clipping):** 
  The logo initiates its entrance animation at 1.5$\times$ scale before shrinking down. On screens shorter than roughly $1125$ units, the outer ring of the top and bottom targets may experience minor pixel clipping at the very beginning of the shrink animation. This is purely cosmetic and entirely harmless, as the target is fully visible by the time the ring begins filling.