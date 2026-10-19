# Geometric Gaze Estimation Method

This method converts iris movement and maps it to a location on the screen.

## Input

| Parameter | Description |
| --- | --- |
| `h`, `v` | Horizontal and vertical iris displacement normalized to eye size. |
| `k_h`, `k_v` | Conversion factors from horizontal and vertical iris displacement to eyeball angular displacement. |
| `center_x`, `center_y` | Location of the screen center relative to the person, normalized to screen size. Values range from 0 to 1. |
| `gain_x`, `gain_y` | Distance from the person to the screen normalized to screen size. |

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
