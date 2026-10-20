# Geometric Gaze Estimation Method

This method converts iris movement and maps it to a location on the screen.

## Input

| Parameter | Description |
| --- | --- |
| `h`, `v` | Horizontal and vertical iris displacement from the middle of the eye corners, normalized to half the eye width. `v` used to be measured from the middle of the lids, see [GAZE-FIT.md](GAZE-FIT.md#up-and-down). |
| `k_h`, `k_v` | Conversion factors from horizontal and vertical iris displacement to eyeball angular displacement. |
| `center_x`, `center_y` | Location of the screen center relative to the person, normalized to screen size. Values range from 0 to 1. |
| `gain_x`, `gain_y` | Distance from the person to the screen normalized to screen size. |
| `left_h_baseline`, `right_h_baseline` (and `_v_`) | Where each iris sits when looking at the middle of the screen, subtracted from `h` / `v` first. The eyes turn in a bit, so they differ per eye. |
| `head_x_gain`, `head_x_ref` | Leaning left/right: `x` moves by `head_x_gain` for every 1.0 the middle of the face moves across the camera image, from `head_x_ref`. 0 = no correction. |
| `x_squeeze`, `openness_ref` | Looking down squeezes left/right: `x` gets `x_squeeze * tan(θ_h) * (lid openness - openness_ref)` on top. 0 = off. |
| `head_x_shrink`, `head_y_pitch`, `head_y_lean`, `pitch_ref` | From the head step of the calibration: leaning shrinks left/right (`head_x_shrink * tan(θ_h) * abs(face moved)`) and moves up/down (`head_y_lean * abs(face moved)`), tilting the head moves up/down (`head_y_pitch * (pitch - pitch_ref)`). The y ones only with a fitted up/down. 0 = off. |
| `y_const`, `y_openness`, `y_iris` | Up/down from the lids instead of the angle: `y = y_const + y_openness * lid openness + y_iris * v`, both averaged over the open eyes. Calibrations recorded before `v` came from the eye corners have `y_iris` 0 (up/down from the lids only). When `y_openness` and `y_iris` are 0 (not calibrated) `y` comes from `k_v` / `gain_y` below. |
| `y_arc`, `y_openness_sq` | Curves on the fitted up/down, 0 when the calibration didn't find them: `+ y_arc * tan(left/right angle)²` (the sides of the screen reading higher or lower than the middle) and `+ y_openness_sq * openness²` (stretched more at one end). See SAVED-CALIBRATIONS.md, "The curves". |

All of these are fitted per person by the calibration, see [GAZE-FIT.md](GAZE-FIT.md). The values below are what's used without a calibration.

## Placeholder Assumptions

| Parameter | Assumed Value | Description |
| --- | --- | --- |
| `k_h`, `k_v` | `0.7` | Placeholder conversion factors. |
| `center_x`, `center_y` | `0.5` | The screen center is straight ahead of the person. |
| `gain_x` | 0.5 / tan(15°) | The screen spans ±15° horizontally. |
| `gain_y` | 0.5 / tan(10°) | The screen spans ±10° vertically. |
| `max_sin` | `0.95` | Prevents unreasonable iris displacement. |

## Output

| Parameter | Description |
| --- | --- |
| `valid` | Indicates whether each eye is open and usable, rather than closed or blocked. |
| `h_deg`, `v_deg` | Gaze direction in degrees. |
| `x`, `y` | Screen location projected from the gaze direction. |

## Mathematical Formulation and Gaze Estimation

### 1. Convert Iris Displacement to Gaze Direction Angle

```text
sin(θ_h) = h / k_h
sin(θ_v) = v / k_v
```

Therefore:

```text
θ_h = arcsin(h / k_h)
θ_v = arcsin(v / k_v)
```

The inverse sine function returns angles in radians. Convert them to degrees for the output:

```text
h_deg = θ_h × 180 / π
v_deg = θ_v × 180 / π
```

### 2. Convert Gaze Direction Angle to Screen Location

```text
x = center_x + gain_x × tan(θ_h)
y = center_y + gain_y × tan(θ_v)
```

Use the angles in radians when evaluating `tan()` in code.
