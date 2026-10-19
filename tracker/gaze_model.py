"""
Geometric gaze estimation.

Takes the output of eye_extractor.extract_eyes() (or extract_feature_vector()) and estimates where the
gaze is directed. No calibration and no smoothing.

Not handled yet: head movement.
The numbers in GazeParams are placeholders until calibration provides them per person.
"""

import json
import math
from dataclasses import dataclass, fields

# k from the module docstring. Starting guesses, calibration should tune them per person.
# may need to make it different for each eye depending on geometry of the face
K_H = 0.7
K_V = 0.7

# keeps asin() in its domain if an offset is bigger than the geometry allows (about 72 degrees)
MAX_SIN = 0.95

# Placeholders until calibration: straight ahead is the middle of the screen, and +-15 degrees
# horizontal / +-10 degrees vertical spans it.
SCREEN_CENTER_X = 0.5
SCREEN_CENTER_Y = 0.5
GAIN_X = 0.5 / math.tan(math.radians(15))
GAIN_Y = 0.5 / math.tan(math.radians(10))

# Maximum allowed difference between the two eyes' angle estimates.
# Horizontal and vertical thresholds are independent because the
# two axes may have different noise characteristics.
MAX_DISAGREE_H_DEG = 12.0
MAX_DISAGREE_V_DEG = 8.0

@dataclass(frozen=True)
class GazeParams:
    """The numbers the model needs. Calibration will make one of these per person."""
    k_h: float = K_H
    k_v: float = K_V

    # Neutral-gaze offsets for each eye, measured by calibration.
    left_h_baseline: float = 0.0
    left_v_baseline: float = 0.0
    right_h_baseline: float = 0.0
    right_v_baseline: float = 0.0

    center_x: float = SCREEN_CENTER_X
    center_y: float = SCREEN_CENTER_Y

    gain_x: float = GAIN_X
    gain_y: float = GAIN_Y

    # Leaning left or right moves the head, and the eyes turn the other way to stay on the same spot, so the same
    # iris position means a different place on the screen. x moves by head_x_gain for every 1.0 the middle of the
    # face moves across the camera image (0-1, mirrored like everything else), from head_x_ref, where it was during
    # calibration. 0 = no correction (not calibrated, or calibrated before this existed)
    head_x_gain: float = 0.0
    head_x_ref: float = 0.5

    # Up/down fitted by calibration: y = y_const + y_openness * lid openness + y_iris * vertical iris offset, both
    # averaged over the usable eyes. The iris barely moves up and down in v (it's measured from the middle of the lids,
    # which follow the eye), but the lids do: more open looking up, less looking down. When y_openness and y_iris are
    # both 0 (not calibrated) y comes from the angle model below (k_v, gain_y) like before
    y_const: float = 0.5
    y_openness: float = 0.0
    y_iris: float = 0.0

    # Looking down, the iris moves less from side to side (the lid comes down over it), so left/right gets squeezed
    # together at the bottom of the screen. x gets x_squeeze * tan(angle) * (lid openness - openness_ref) on top,
    # openness_ref being the openness during calibration. 0 = off
    x_squeeze: float = 0.0
    openness_ref: float = 0.3

    # The head step of the calibration (leaning and tilting while looking at dots). Leaning doesn't only shift x
    # (head_x_gain), it shrinks it too: + head_x_shrink * tan(angle) * |how far the face moved|. And for up/down,
    # + head_y_pitch * (head pitch - pitch_ref) + head_y_lean * |how far the face moved|. Only with a fitted up/down.
    # All 0 = off
    head_x_shrink: float = 0.0
    head_y_pitch: float = 0.0
    head_y_lean: float = 0.0
    pitch_ref: float = 0.0

    max_disagree_h_deg: float = MAX_DISAGREE_H_DEG
    max_disagree_v_deg: float = MAX_DISAGREE_V_DEG


DEFAULT_PARAMS = GazeParams()


# ---------------------------------------------------------
# HELPERS
# ---------------------------------------------------------

def _r(x, n=4):
    return round(float(x), n)


def _finite_number(value):
    """Return True if value is a finite number, excluding booleans."""
    if isinstance(value, bool):
        return False

    try:
        return math.isfinite(float(value))
    except (TypeError, ValueError, OverflowError):
        return False


def _invalid(
    reason,
    disagree_h=None,
    disagree_v=None,
    left=None,
    right=None,
    h_deg=None,
    v_deg=None,
):
    """
    Return a consistently structured invalid result.

    Per-eye estimates and disagreement measurements are preserved
    whenever they are available, even if the combined estimate fails.
    """
    return {
        "valid": False,
        "reason": reason,
        "confidence": None,
        "h_deg": h_deg,
        "v_deg": v_deg,
        "x": None,
        "y": None,
        "eyes_used": 0,
        "disagree_h_deg": disagree_h,
        "disagree_v_deg": disagree_v,
        "left": left,
        "right": right,
    }


def _validate_params(params):
    """Check that parameters contain sensible finite values."""
    positive_values = (
        params.k_h,
        params.k_v,
    )

    finite_values = (
        params.k_h,
        params.k_v,
        params.center_x,
        params.center_y,
        params.gain_x,
        params.gain_y,
        params.max_disagree_h_deg,
        params.max_disagree_v_deg,
        params.left_h_baseline,
        params.left_v_baseline,
        params.right_h_baseline,
        params.right_v_baseline,
        params.head_x_gain,
        params.head_x_ref,
        params.y_const,
        params.y_openness,
        params.y_iris,
        params.x_squeeze,
        params.openness_ref,
        params.head_x_shrink,
        params.head_y_pitch,
        params.head_y_lean,
        params.pitch_ref,
    )

    if not all(_finite_number(value) for value in finite_values):
        return "Gaze parameters must all be finite numbers"

    if any(float(value) <= 0 for value in positive_values):
        return "k_h and k_v must be greater than zero"

    if params.max_disagree_h_deg < 0:
        return "Horizontal disagreement threshold cannot be negative"

    if params.max_disagree_v_deg < 0:
        return "Vertical disagreement threshold cannot be negative"

    return None


def eye_angle(offset, k):
    """Eye rotation in radians for an iris offset, see the module docstring."""
    ratio = float(offset) / float(k)
    ratio = max(-MAX_SIN, min(MAX_SIN, ratio))

    return math.asin(ratio)


def _usable_offsets(eyes):
    """[(name, h, v)] for every eye with numbers worth using: present, open, not covered, finite.
    A covered eye's numbers are made up by MediaPipe, a closed eye's iris is hidden by the lid."""
    found = []

    if not isinstance(eyes, dict):
        return found

    for name in ("left", "right"):
        eye = eyes.get(name)

        if not isinstance(eye, dict):
            continue

        if eye.get("closed") or eye.get("covered"):
            continue

        offset = eye.get("offset")

        if not isinstance(offset, dict):
            continue

        h = offset.get("h")
        v = offset.get("v")

        if not _finite_number(h) or not _finite_number(v):
            continue

        found.append((name, float(h), float(v)))

    return found


def _mean_openness(eyes, offsets):
    """Lid openness averaged over the usable eyes, None if none of them has one."""
    openness = [eyes[name].get("openness") for name, _, _ in offsets]
    openness = [float(o) for o in openness if _finite_number(o)]
    return sum(openness) / len(openness) if openness else None


def _fitted_y(offsets, openness, params):
    """y from the calibrated up/down numbers (y_const, y_openness, y_iris), from the usable eyes' lid openness and
    vertical iris offset. None if there are no such numbers, or no openness to use."""
    if params.y_openness == 0.0 and params.y_iris == 0.0:
        return None
    if openness is None:
        return None
    v = sum(v for _, _, v in offsets) / len(offsets)
    return params.y_const + params.y_openness * openness + params.y_iris * v


def _screen_position(h, v, params, face_x=None, fitted_y=None, openness=None, face_pitch=None):
    """Convert gaze angles into normalized screen coordinates. face_x: the middle of the face in the camera
    image (0-1), face_pitch: the head pitch in degrees, for the head corrections (head_*). None leaves them out.
    fitted_y: y from _fitted_y, used instead of the angle when there is one. openness: the usable eyes'
    average lid openness, for the looking down squeeze (x_squeeze)."""
    t = math.tan(h)
    x = params.center_x + params.gain_x * t
    y = params.center_y + params.gain_y * math.tan(v) if fitted_y is None else fitted_y
    if openness is not None:
        x += params.x_squeeze * t * (openness - params.openness_ref)
    if _finite_number(face_x):
        moved = float(face_x) - params.head_x_ref
        x += params.head_x_gain * moved + params.head_x_shrink * t * abs(moved)
        if fitted_y is not None:
            y += params.head_y_lean * abs(moved)
    if fitted_y is not None and _finite_number(face_pitch):
        y += params.head_y_pitch * (float(face_pitch) - params.pitch_ref)

    return _r(x), _r(y)


def _eye_diagnostics(per_eye):
    """Format individual eye angle estimates for output/debugging."""
    result = {
        "left": None,
        "right": None,
    }

    for name, (h, v) in per_eye.items():
        result[name] = {
            "h_deg": _r(math.degrees(h), 3),
            "v_deg": _r(math.degrees(v), 3),
        }

    return result


# ---------------------------------------------------------
# MAIN GAZE ESTIMATION
# ---------------------------------------------------------

def estimate_gaze(eyes, params=None, face_x=None, face_pitch=None):
    """
    Estimate gaze direction and normalized screen position.

    face_x: the middle of the face in the camera image (0-1, faces[0].box), face_pitch: the head pitch (faces[0].pose),
    for the head corrections (GazeParams.head_*). None leaves them out.

    Returns a dictionary containing:
        valid      bool
        reason     why not, when valid is False ("No eye data", "No usable eye")
        confidence:
            "both"      - both eyes were usable and agreed.
            "single_eye" - only one eye was usable.
        h_deg      horizontal gaze angle in degrees, + is right
        v_deg      vertical gaze angle in degrees, + is down
        x, y       where that lands on the screen, normalized 0-1 (see GazeParams, placeholders until calibrated)
        eyes_used  1 or 2
        disagree_h_deg, disagree_v_deg:
            Absolute difference between the eyes' angle estimates,
            when both eyes were usable.
        left, right:
            Per-eye angle estimates, retained even when the combined
            estimate is rejected due to disagreement.
    """
    if eyes is None:
        return _invalid("No eye data")

    params = params or DEFAULT_PARAMS

    if not isinstance(params, GazeParams):
        return _invalid("Invalid gaze parameters")

    parameter_error = _validate_params(params)

    if parameter_error:
        return _invalid(parameter_error)

    offsets = _usable_offsets(eyes)

    if not offsets:
        return _invalid("No usable eye")

    # Convert each usable eye's normalized offsets to angles.
    baselines = {
        "left": (
            params.left_h_baseline,
            params.left_v_baseline,
        ),
        "right": (
            params.right_h_baseline,
            params.right_v_baseline,
        ),
    }

    try:
        per_eye = {}

        for name, h_offset, v_offset in offsets:
            h_baseline, v_baseline = baselines[name]

            corrected_h = h_offset - h_baseline
            corrected_v = v_offset - v_baseline

            per_eye[name] = (
                eye_angle(corrected_h, params.k_h),
                eye_angle(corrected_v, params.k_v),
            )

    except (TypeError, ValueError, OverflowError, ZeroDivisionError):
        return _invalid("Could not convert eye offsets to angles")

    diagnostics = _eye_diagnostics(per_eye)
    openness = _mean_openness(eyes, offsets)
    fitted_y = _fitted_y(offsets, openness, params)

    # -----------------------------------------------------
    # SINGLE-EYE ESTIMATE
    # -----------------------------------------------------
    if len(per_eye) == 1:
        name = next(iter(per_eye))
        h, v = per_eye[name]

        x, y = _screen_position(h, v, params, face_x, fitted_y, openness, face_pitch)

        return {
            "valid": True,
            "reason": None,
            "confidence": "single_eye",
            "h_deg": _r(math.degrees(h), 3),
            "v_deg": _r(math.degrees(v), 3),
            "x": x,
            "y": y,
            "eyes_used": 1,
            "disagree_h_deg": None,
            "disagree_v_deg": None,
            "left": diagnostics["left"],
            "right": diagnostics["right"],
        }

    # -----------------------------------------------------
    # TWO-EYE ESTIMATE
    # -----------------------------------------------------
    left_h, left_v = per_eye["left"]
    right_h, right_v = per_eye["right"]

    disagree_h = abs(math.degrees(left_h - right_h))
    disagree_v = abs(math.degrees(left_v - right_v))

    disagree_h = _r(disagree_h, 3)
    disagree_v = _r(disagree_v, 3)

    # Reject the combined estimate if either axis exceeds its
    # independently configured disagreement threshold.
    #if (
    #    disagree_h > params.max_disagree_h_deg
    #    or disagree_v > params.max_disagree_v_deg
    #):
    #    # Diagnostic average only; valid remains False.
    #    diagnostic_h = (left_h + right_h) / 2.0
    #    diagnostic_v = (left_v + right_v) / 2.0

    #    return _invalid(
    #        reason="Eyes disagree",
    #        disagree_h=disagree_h,
    #        disagree_v=disagree_v,
    #        h_deg=_r(math.degrees(diagnostic_h), 3),
    #        v_deg=_r(math.degrees(diagnostic_v), 3),
    #        left=diagnostics["left"],
    #        right=diagnostics["right"],
    #   # )
    # TEMPORARY FOR DIAGNOTIC PURPOSES
    eyes_disagree = (
        disagree_h > params.max_disagree_h_deg
        or disagree_v > params.max_disagree_v_deg
    )

    # Both eyes agree sufficiently, so average their angles.
    h = (left_h + right_h) / 2.0
    v = (left_v + right_v) / 2.0

    x, y = _screen_position(h, v, params, face_x, fitted_y, openness, face_pitch)

    return {
        "valid": True,
        #"reason": None,
        #"confidence": "both",
        "reason": "Diagnostic: eyes disagree" if eyes_disagree else None,
        "confidence": "diagnostic_both" if eyes_disagree else "both",
        "h_deg": _r(math.degrees(h), 3),
        "v_deg": _r(math.degrees(v), 3),
        "x": x,
        "y": y,
        "eyes_used": 2,
        "disagree_h_deg": disagree_h,
        "disagree_v_deg": disagree_v,
        "left": diagnostics["left"],
        "right": diagnostics["right"],
    }


# ---------------------------------------------------------
# FEATURE-RESULT ADAPTER
# ---------------------------------------------------------

def estimate_gaze_from_features(feature_result, params=None):
    """Same as estimate_gaze, from the result of extract_feature_vector(). That needs both eyes open,
    so this does too. Calibration will work with these vectors, so it can call the model the same way."""
    if not isinstance(feature_result, dict) or not feature_result.get("valid"):
        reason = (
            feature_result.get("reason", "No feature data")
            if isinstance(feature_result, dict)
            else "No feature data"
        )
        return _invalid(reason)

    eyes = {}

    for name in ("left", "right"):
        eye_features = feature_result.get(name)

        if not isinstance(eye_features, dict):
            return _invalid(f"Missing {name} eye features")

        iris_x = eye_features.get("iris_x")
        iris_y = eye_features.get("iris_y")

        if not _finite_number(iris_x) or not _finite_number(iris_y):
            return _invalid(f"Invalid {name} eye features")

        eyes[name] = {
            "offset": {
                "h": float(iris_x),
                "v": float(iris_y),
            },
            "closed": False,
            "covered": False,
        }

    return estimate_gaze(eyes, params)


# ---------------------------------------------------------
# SAVED CALIBRATIONS
# ---------------------------------------------------------

def params_from_dict(values):
    """GazeParams with the numbers a calibration saved (same names as the GazeParams fields).
    Anything missing, unknown or not a finite number keeps its default, so an empty dict gives DEFAULT_PARAMS."""
    if not isinstance(values, dict):
        return DEFAULT_PARAMS
    known = {f.name for f in fields(GazeParams)}
    picked = {name: float(value) for name, value in values.items()
              if name in known and _finite_number(value)}
    params = GazeParams(**picked)
    # e.g. k_h = 0 would divide by zero later. throw the whole thing away rather than half of it
    return DEFAULT_PARAMS if _validate_params(params) else params


def load_params(path):
    """GazeParams from a saved calibration file (app/Calibration/Saved/*.json, the numbers are under "gaze")."""
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    return params_from_dict(data.get("gaze") if isinstance(data, dict) else None)
