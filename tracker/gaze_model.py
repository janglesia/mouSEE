"""
Geometric gaze estimation.

Takes the output of eye_extractor.extract_eyes() (or extract_feature_vector()) and estimates where the
gaze is directed. No calibration and no smoothing.

Not handled yet: head movement.
The numbers in GazeParams are placeholders until calibration provides them per person.
"""

import math
from dataclasses import dataclass

# k from the module docstring. Starting guesses, calibration should tune them per person.
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


@dataclass(frozen=True)
class GazeParams:
    """The numbers the model needs. Calibration will make one of these per person."""
    k_h: float = K_H
    k_v: float = K_V
    center_x: float = SCREEN_CENTER_X
    center_y: float = SCREEN_CENTER_Y
    gain_x: float = GAIN_X
    gain_y: float = GAIN_Y


DEFAULT_PARAMS = GazeParams()


def _r(x, n=4):
    return round(float(x), n)


def eye_angle(offset, k):
    """Eye rotation in radians for an iris offset, see the module docstring."""
    return math.asin(max(-MAX_SIN, min(MAX_SIN, offset / k)))


def _invalid(reason):
    return {"valid": False, "reason": reason,
            "h_deg": None, "v_deg": None, "x": None, "y": None, "eyes_used": 0}


def _usable_offsets(eyes):
    """[(name, h, v)] for every eye with numbers worth using: present, open, not covered, finite.
    A covered eye's numbers are made up by MediaPipe, a closed eye's iris is hidden by the lid."""
    found = []
    for name in ("left", "right"):
        eye = eyes.get(name)
        if not eye or eye.get("closed") or eye.get("covered"):
            continue
        try:
            h = float(eye["offset"]["h"])
            v = float(eye["offset"]["v"])
        except (KeyError, TypeError, ValueError):
            continue
        if math.isfinite(h) and math.isfinite(v):
            found.append((name, h, v))
    return found


def estimate_gaze(eyes, params=None):
    """
    eyes: the dict from extract_eyes(), or None. Only eyes.left / eyes.right are read: the smoothed
    eyes["gaze"] numbers are for the debug label and are not used here.

    Returns a dict with the same keys whether it worked or not:
        valid      bool
        reason     why not, when valid is False ("No eye data", "No usable eye")
        h_deg      horizontal gaze angle in degrees, + is right
        v_deg      vertical gaze angle in degrees, + is down
        x, y       where that lands on the screen, normalized 0-1 (see GazeParams, placeholders until calibrated)
        eyes_used  1 or 2
        left, right  (only when that eye was used) that eye's own h_deg / v_deg
    """
    if eyes is None:
        return _invalid("No eye data")

    offsets = _usable_offsets(eyes)
    if not offsets:
        return _invalid("No usable eye")

    params = params or DEFAULT_PARAMS
    per_eye = {name: (eye_angle(h, params.k_h), eye_angle(v, params.k_v)) for name, h, v in offsets}
    h = sum(a[0] for a in per_eye.values()) / len(per_eye)
    v = sum(a[1] for a in per_eye.values()) / len(per_eye)

    result = {
        "valid": True,
        "h_deg": _r(math.degrees(h), 3),
        "v_deg": _r(math.degrees(v), 3),
        "x": _r(params.center_x + params.gain_x * math.tan(h)),
        "y": _r(params.center_y + params.gain_y * math.tan(v)),
        "eyes_used": len(per_eye),
    }
    for name, (eye_h, eye_v) in per_eye.items():
        result[name] = {"h_deg": _r(math.degrees(eye_h), 3), "v_deg": _r(math.degrees(eye_v), 3)}
    return result


def estimate_gaze_from_features(feature_result, params=None):
    """Same as estimate_gaze, from the result of extract_feature_vector(). That needs both eyes open,
    so this does too. Calibration will work with these vectors, so it can call the model the same way."""
    if not feature_result or not feature_result.get("valid"):
        return _invalid((feature_result or {}).get("reason", "No feature data"))

    eyes = {
        name: {"offset": {"h": feature_result[name]["iris_x"], "v": feature_result[name]["iris_y"]},
               "closed": False, "covered": False}
        for name in ("left", "right")
    }
    return estimate_gaze(eyes, params)