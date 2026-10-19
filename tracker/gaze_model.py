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


def _screen_position(h, v, params):
    """Convert gaze angles into normalized screen coordinates."""
    x = params.center_x + params.gain_x * math.tan(h)
    y = params.center_y + params.gain_y * math.tan(v)

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

def estimate_gaze(eyes, params=None):
    """
    Estimate gaze direction and normalized screen position.

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

    # -----------------------------------------------------
    # SINGLE-EYE ESTIMATE
    # -----------------------------------------------------
    if len(per_eye) == 1:
        name = next(iter(per_eye))
        h, v = per_eye[name]

        x, y = _screen_position(h, v, params)

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

    x, y = _screen_position(h, v, params)

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
