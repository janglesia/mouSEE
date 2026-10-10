"""
Eye / iris extraction and tracking.
"""
import json
import math
import sys
import time
import numpy as np
import cv2

# These names refer to the anatomical eye, not the left/right side of the mirrored camera preview.
# upper_lid / lower_lid are the three landmarks centered on the middle of each lid.
EYES = {
    "right": {
        "left_corner": 133, "right_corner": 33,
        "upper_lid": (158, 159, 160), "lower_lid": (144, 145, 153),
        "iris": 468, "iris_ring": (469, 470, 471, 472), "blink": "eyeBlinkRight",
        "outline": (33, 7, 163, 144, 145, 153, 154, 155, 133, 173, 157, 158, 159, 160, 161, 246)
    },
    "left": {
        "left_corner": 263, "right_corner": 362,
        "upper_lid": (387, 386, 385), "lower_lid": (374, 373, 380),
        "iris": 473, "iris_ring": (474, 475, 476, 477), "blink": "eyeBlinkLeft",
        "outline": (362, 382, 381, 380, 374, 373, 390, 249, 263, 466, 388, 387, 386, 385, 384, 398)
    },
}

# Blendshape eye-blink score above this is considered closed.
CLOSED_BLINK = 0.5

# Fallback eye openness threshold when blendshapes aren't available.
# This is a normalized eyelid-gap / eye-width ratio.
CLOSED_OPENNESS = 0.15

# Winks (one eye closed). MediaPipe links the eyes, so for a wink the blink score only reaches
# ~0.2-0.35 and the lid points stay ~2/3 open. Compare the eyes to each other instead:
# closed if this eye is less than WINK_OPENNESS_RATIO as open as the other one AND its blink
# score is WINK_BLINK_DIFF higher. Tuned on a recording: winks 100% detected, open eyes 0%.
# Only used without a camera image, with one IRIS_HIDDEN_RATIO decides (this breaks when the head tilts up).
WINK_OPENNESS_RATIO = 0.8
WINK_BLINK_DIFF = 0.08

# Covered eyes (hand, hair...). MediaPipe guesses where a hidden eye is and reports it as a
# normal open eye, so this has to come from the image. Contrast = std / mean brightness inside
# the eye outline. From a recording: open eye 0.45-0.7, winked eye 0.2-0.66, hand over the
# eye 0.02-0.07. Below this counts as covered.
COVERED_CONTRAST = 0.15

# Closed eyes, from the image. Tilting the head up raises MediaPipe's blink score for the OPEN eye
# too, so the blink-based wink rule stops working (a recording: winks caught 1-4% of the time with
# the head tilted up). Instead check if the iris is visible: iris ratio = brightness of the iris
# area / brightness of the rest of the eye. Open eye (dark iris on white) 0.1-0.5 at any head
# angle, closed eye (lid, no iris) 0.7-1.1. Above this counts as closed. 0.6 gave single-frame false
# alarms in dimmer light, 0.65 still caught 100% of head-up winks in the recording.
IRIS_HIDDEN_RATIO = 0.65

# Debugging, will probably change as needed
H_THRESH = 0.12
V_THRESH = 0.08
SMOOTHING_ALPHA = 0.20

# Minimum valid eye width in pixels.
MIN_EYE_WIDTH_PX = 1.0

# Thresholds for frame validation
MAX_EYE_WIDTH_JUMP = 0.35
MAX_GAZE_JUMP_H = 0.75
MAX_GAZE_JUMP_V = 0.75
MIN_IRIS_DIAMETER_PX = 3.0
# a jump that lasts this many frames in a row is real movement (e.g. leaning in), not a glitch
MAX_REJECTIONS = 5

def _r(x, n=4):
    return round(float(x), n)

def _mean_point(points):
    return np.mean(points, axis=0)

def _distance(a, b):
    """Euclidean distance between two 2D points."""
    return float(np.linalg.norm(a - b))

# For debugging, won't actually control the cursor 
def classify(h, v):
    dh = abs(h) / H_THRESH
    dv = abs(v) / V_THRESH

    if dh < 1.0 and dv < 1.0:
        return "center"

    if dh >= dv:
        return "right" if h > 0 else "left"

    return "down" if v > 0 else "up"

# Reduce MediaPip jitter
class GazeSmoother:
    def __init__(self, alpha=SMOOTHING_ALPHA):
        self.alpha, self.value = alpha, None
    def reset(self):
        self.value = None
    def update(self, h, v):
        x = np.array([h, v])
        self.value = x if self.value is None else self.alpha * x + (1 - self.alpha) * self.value
        return tuple(self.value)

# checks previous valid eye to compare to new read eye
class EyeValidator:
    def __init__(self):
        self.prev_eye = None
        self.rejections = 0  # rejected frames in a row

    def reset(self):
        self.prev_eye = None
        self.rejections = 0

    def is_valid(self, eye):
        # check that the iris diameter is a reasonable size. applies to every frame, even the first
        if eye["iris"]["diameter_px"] < MIN_IRIS_DIAMETER_PX:
            return False

        # Accept the first valid eye as the reference.
        if self.prev_eye is None:
            return True

        prev_eye = self.prev_eye

        # Check for sudden jumps in eye width or gaze direction
        width_jump = abs(eye["width_px"] - prev_eye["width_px"]) / max(prev_eye["width_px"], 1.0)

        # if the eye width jump was too large to be reasonable, reject the new eye data
        if width_jump > MAX_EYE_WIDTH_JUMP:
            return False

        # iris position is meaningless while the lid (or a hand) covers it, so skip the gaze check
        if eye["closed"] or eye.get("covered"):
            return True

        # Check that gaze direction does not jump unreasonably far from previous frame
        gaze_jump_h = abs(eye["offset"]["h"] - prev_eye["offset"]["h"])
        gaze_jump_v = abs(eye["offset"]["v"] - prev_eye["offset"]["v"])

        # if the gaze jump is too large to be reasonable, reject the new eye data
        if gaze_jump_h > MAX_GAZE_JUMP_H or gaze_jump_v > MAX_GAZE_JUMP_V:
            return False

        # if it surpasses all checks, return true
        return True

    # returns True if the eye should be used. accepted open eyes become the new reference
    def update(self, eye):
        # comparing against an old reference forever would lock the eye out after a real change
        # (leaning closer makes the eye wider), so after enough rejections in a row start over
        if self.rejections >= MAX_REJECTIONS:
            self.reset()

        if not self.is_valid(eye):
            self.rejections += 1
            return False

        self.rejections = 0
        # don't use a closed or covered eye as the reference, after a blink compare to the eye from before it
        if not eye["closed"] and not eye.get("covered"):
            self.prev_eye = eye
        return True

def _eye_crop(image, outline_px):
    """Grayscale crop around the eye, a mask of the eye outline in it, and the crop's offset.
    Only converts the small crop, not the whole frame."""
    x, y, w, h = cv2.boundingRect(outline_px)
    x0, y0 = max(x, 0), max(y, 0)
    crop = image[y0:y + h, x0:x + w]
    if crop.size == 0:
        return None
    gray = cv2.cvtColor(crop, cv2.COLOR_BGR2GRAY) if crop.ndim == 3 else crop
    mask = np.zeros(gray.shape, np.uint8)
    cv2.fillPoly(mask, [outline_px - (x0, y0)], 255)
    return gray, mask, np.array([x0, y0])

def eye_contrast(image, outline_px):
    """std / mean brightness inside the eye outline, see COVERED_CONTRAST.

    image: the camera frame (BGR). outline_px: the eye outline in that image's pixel coordinates.
    """
    eye = _eye_crop(image, outline_px)
    if eye is None:
        return None
    gray, mask, _ = eye
    values = gray[mask > 0]
    if values.size < 10:
        return None
    return float(values.std() / max(values.mean(), 1.0))

def iris_ratio(image, outline_px, iris_center_px, iris_radius_px):
    """Brightness of the iris area / brightness of the rest of the eye, see IRIS_HIDDEN_RATIO.
    Same coordinates as eye_contrast."""
    eye = _eye_crop(image, outline_px)
    if eye is None:
        return None
    gray, mask, offset = eye
    cx, cy = (np.asarray(iris_center_px) - offset).astype(int)
    # a bit smaller than the iris so it doesn't catch the white around it
    iris = np.zeros_like(mask)
    cv2.circle(iris, (int(cx), int(cy)), max(2, int(iris_radius_px * 0.7)), 255, -1)
    iris_values = gray[(iris > 0) & (mask > 0)]
    # and the rest of the eye with some space around the iris
    around = cv2.dilate(iris, np.ones((5, 5), np.uint8), iterations=2)
    rest_values = gray[(around == 0) & (mask > 0)]
    if iris_values.size < 3 or rest_values.size < 3:
        return None
    return float(iris_values.mean() / max(rest_values.mean(), 1.0))

def extract_eyes(landmarks, width, height, blendshapes=None, gaze_smoother=None, eye_validator=None,
                 image=None):
    # image: optional camera frame as captured (BGR, NOT mirrored, same size as width x height).
    # only used to check whether an eye is covered.
    if landmarks is None:
        return None

    try:
        lm = np.asarray(landmarks, dtype=float)[:, :2]
    except (TypeError, ValueError):
        return None

    if lm.ndim != 2 or lm.shape[1] != 2:
        return None

    if len(lm) <= 477:
        return None

    if width <= 0 or height <= 0:
        return None

    # Convert normalized MediaPipe coordinates to pixels.
    frame_size = np.array([width, height], dtype=float)
    px = lm * frame_size
    out = {}

    # loop through each eye
    for name, e in EYES.items():
        try: # try catch block for reading in the eye landmarks to prevent crashing
            left_corner = px[e["left_corner"]]
            right_corner = px[e["right_corner"]]
            axis = right_corner - left_corner
            eye_width = float(np.linalg.norm(axis))

            if eye_width < MIN_EYE_WIDTH_PX:
                continue

            # u = direction along the eye from left_corner to right_corner. v = perpendicular vector, vertical axis
            u = axis / eye_width
            v = np.array([-u[1], u[0]])
            half_width = eye_width / 2.0
            eye_center = (left_corner + right_corner) / 2.0

            # Try to reduce jitter by using the surrounding iris landmarks
            iris_indices = (e["iris"], *e["iris_ring"])
            iris_points = px[list(iris_indices)]
            iris_center_landmark = px[e["iris"]]
            iris_center = _mean_point(iris_points)

            iris_radius = np.linalg.norm(
                px[list(e["iris_ring"])] - iris_center,
                axis=1,
            )

            iris_diameter = float(np.mean(iris_radius) * 2.0)

            upper_lid_points = px[list(e["upper_lid"])]
            lower_lid_points = px[list(e["lower_lid"])]
            upper_lid = _mean_point(upper_lid_points)
            lower_lid = _mean_point(lower_lid_points)

            lid_mid = (upper_lid + lower_lid) / 2.0

            # Actual geometric eyelid gap.
            lid_gap = _distance(upper_lid, lower_lid)

            # Normalize by eye width so this measurement is reasonably independent of distance from the camera.
            openness = lid_gap / eye_width

            # Horizontal: -1 far to one corner, 0 is centered, +1 far to the other corner
            # Vertical: relative to the midpoint of the upper/lower lids
            h_offset = float((iris_center - eye_center) @ u / half_width)
            v_offset = float((iris_center - lid_mid) @ v / half_width)

            iris_to_center = iris_center - eye_center
            iris_angle = math.degrees(math.atan2(iris_to_center @ v, iris_to_center @ u))
            iris_distance = float(np.linalg.norm(iris_to_center) / half_width)

            blink = None
            if blendshapes:
                blink_value = blendshapes.get(e["blink"])

                if blink_value is not None:
                    try:
                        blink = float(blink_value)
                    except (TypeError, ValueError):
                        blink = None

            if blink is not None:
                closed = blink > CLOSED_BLINK
            else:
                closed = openness < CLOSED_OPENNESS

            contrast = iris_visibility = None
            if image is not None:
                # landmarks are mirrored, the image isn't, so flip x back
                outline_px = px[list(e["outline"])].copy()
                outline_px[:, 0] = width - outline_px[:, 0]
                outline_px = outline_px.astype(np.int32)
                iris_center_px = (width - iris_center[0], iris_center[1])
                contrast = eye_contrast(image, outline_px)
                iris_visibility = iris_ratio(image, outline_px, iris_center_px, iris_diameter / 2.0)
            covered = contrast is not None and contrast < COVERED_CONTRAST
            if covered:
                # a hand pressing on the lid can push the blink score up, a covered eye is never a wink/blink
                closed = False
            elif iris_visibility is not None:
                # the image shows directly whether the iris is there, more reliable than the blink
                # score (which tilting the head throws off)
                closed = iris_visibility > IRIS_HIDDEN_RATIO

            out[name] = {
                "outline": (lm[list(e["outline"])].round(4).tolist()),
                "corners": {
                    "left": lm[e["left_corner"]].round(4).tolist(),
                    "right": lm[e["right_corner"]].round(4).tolist(),
                },
                "lids": {
                    "upper": lm[list(e["upper_lid"])].round(4).tolist(),
                    "lower": lm[list(e["lower_lid"])].round(4).tolist(),
                    "upper_center": (upper_lid / frame_size).round(4).tolist(),
                    "lower_center": (lower_lid / frame_size).round(4).tolist(),
                },
                "iris": {
                    # MediaPipe center landmark.
                    "landmark_center": ((iris_center_landmark / frame_size).round(4).tolist()),

                    # Averaged center that the gaze calculations use.
                    "center": (iris_center / frame_size).round(4).tolist(),
                    "diameter_px": _r(iris_diameter, 1),

                    "ring": (lm[list(e["iris_ring"])].round(4).tolist()),
                },
                "offset": {"h": _r(h_offset), "v": _r(v_offset), "angle": _r(iris_angle, 1), "distance": _r(iris_distance)},
                "width_px": _r(eye_width, 1),
                "lid_gap_px": _r(lid_gap, 1),
                "openness": _r(openness),
                "blink": (
                    None
                    if blink is None
                    else _r(blink, 3)
                ),
                "closed": bool(closed),
                "contrast": None if contrast is None else _r(contrast, 3),
                "iris_ratio": None if iris_visibility is None else _r(iris_visibility, 3),
                "covered": bool(covered),
            }

        # skip this eye and only use the other eye if something goes wrong
        except(IndexError, TypeError, ValueError):
            continue

    # winks need both eyes measured before deciding, see WINK_OPENNESS_RATIO
    if "left" in out and "right" in out:
        for name, other in (("left", "right"), ("right", "left")):
            eye, other_eye = out[name], out[other]
            # comparing against a covered eye means nothing, MediaPipe made it up.
            # and when the image already showed whether the iris is there, that decided it
            if eye["closed"] or eye["covered"] or other_eye["covered"] or eye["iris_ratio"] is not None:
                continue
            less_open = eye["openness"] < WINK_OPENNESS_RATIO * other_eye["openness"]
            # without blendshapes only the openness can be compared
            blink_higher = (eye["blink"] is None or other_eye["blink"] is None
                            or eye["blink"] - other_eye["blink"] > WINK_BLINK_DIFF)
            if less_open and blink_higher:
                eye["closed"] = True

    # eye validator call, drop the eye if it looks like a glitch.
    # after the wink check because the validator treats closed eyes differently
    if eye_validator is not None:
        for name in ("left", "right"):
            if name in out and not eye_validator[name].update(out[name]):
                del out[name]

    # if both eyes are causing problems, return None
    if not out:
        return None

    open_eyes = [
        out[name]
        for name in ("left", "right")
        if name in out and not out[name]["closed"] and not out[name]["covered"]
    ]

    if open_eyes:
        # Average both eyes rather than relying on one eye.
        raw_h = float(
            np.mean([
                eye["offset"]["h"]
                for eye in open_eyes
            ])
        )

        raw_v = float(
            np.mean([
                eye["offset"]["v"]
                for eye in open_eyes
            ])
        )

        # Difference between the eyes can be useful for diagnosing tracking problems.
        if len(open_eyes) == 2:
            binocular_h_error = abs(out["left"]["offset"]["h"] - out["right"]["offset"]["h"])
            binocular_v_error = abs(out["left"]["offset"]["v"]- out["right"]["offset"]["v"])
        else:
            binocular_h_error = None
            binocular_v_error = None

        # Optional temporal smoothing.
        if gaze_smoother is not None:
            smooth_h, smooth_v = gaze_smoother.update(raw_h, raw_v)
        else:
            smooth_h = raw_h
            smooth_v = raw_v

        # h / v are smoothed when a smoother is given; raw is always unsmoothed.
        out["gaze"] = {
            "raw": {"h": _r(raw_h), "v": _r(raw_v)},
            "h": _r(smooth_h), "v": _r(smooth_v),
            "direction": classify(smooth_h, smooth_v),
            "binocular_error": {
                "h": ( None
                    if binocular_h_error is None
                    else _r(binocular_h_error)
                ),
                "v": ( None
                    if binocular_v_error is None
                    else _r(binocular_v_error)
                ),
            },
            "eyes_used": len(open_eyes),
        }

    else:
        # no usable eye: both closed, or covered (or one of each)
        any_covered = any(out[name]["covered"] for name in ("left", "right") if name in out)
        out["gaze"] = {
            "raw": { "h": None, "v": None}, "h": None, "v": None,
            "direction": "covered" if any_covered else "closed",
            "binocular_error": {"h": None, "v": None}, "eyes_used": 0,
        }

    return out

def extract_feature_vector(eyes, timestamp=None):
    """
    Extract normalized eye/iris feature vector for geometric gaze model and calibration.

    Returns a structured result with a fixed-order 6-dimensional feature vector:
    [left_iris_x, left_iris_y, left_eye_open, right_iris_x, right_iris_y, right_eye_open]

    Naming convention: "left" and "right" refer to the subject's anatomical left/right eye,
    consistent with the EYES dictionary and MediaPipe's naming. The camera preview is mirrored,
    but the eye labels remain anatomical.

    Args:
        eyes: Output from extract_eyes(), containing per-eye geometry data
        timestamp: Optional frame timestamp or identifier

    Returns:
        dict with fields:
            - valid (bool): True if both eyes are usable and features are valid
            - reason (str, optional): Explanation when valid=False
            - timestamp: The input timestamp, if provided
            - features (list[float]): 6-element vector when valid, None otherwise
            - left (dict, optional): Per-eye breakdown when valid
            - right (dict, optional): Per-eye breakdown when valid
    """
    if eyes is None:
        return {"valid": False, "reason": "No eye data", "timestamp": timestamp, "features": None}

    # Both eyes must be present and not closed for this initial implementation
    if "left" not in eyes or "right" not in eyes:
        return {"valid": False, "reason": "Both eyes required", "timestamp": timestamp, "features": None}

    left_eye = eyes["left"]
    right_eye = eyes["right"]

    # a covered eye's numbers are made up by MediaPipe
    if left_eye.get("covered", False) or right_eye.get("covered", False):
        return {"valid": False, "reason": "Eye covered", "timestamp": timestamp, "features": None}

    # Check if either eye is closed
    if left_eye.get("closed", False) or right_eye.get("closed", False):
        return {"valid": False, "reason": "Eye closed", "timestamp": timestamp, "features": None}

    # Check for missing or non-finite measurements
    def check_eye_features(eye):
        required = ["offset", "openness", "width_px"]
        for key in required:
            if key not in eye:
                return False
        if "h" not in eye["offset"] or "v" not in eye["offset"]:
            return False
        # Check for non-finite values
        h = eye["offset"]["h"]
        v = eye["offset"]["v"]
        openness = eye["openness"]
        width = eye["width_px"]
        if not all(isinstance(x, (int, float)) for x in [h, v, openness, width]):
            return False
        if not all(math.isfinite(x) for x in [h, v, openness, width]):
            return False
        if width <= 0:
            return False
        return True

    if not check_eye_features(left_eye) or not check_eye_features(right_eye):
        return {"valid": False, "reason": "Invalid eye measurements", "timestamp": timestamp, "features": None}

    # Extract the feature vector components
    # iris_x: horizontal offset from eye center, normalized by half eye width (from extract_eyes)
    # iris_y: vertical offset from eyelid midpoint, normalized by half eye width (from extract_eyes)
    # eye_open: eyelid separation normalized by eye width (from extract_eyes)
    left_iris_x = float(left_eye["offset"]["h"])
    left_iris_y = float(left_eye["offset"]["v"])
    left_eye_open = float(left_eye["openness"])

    right_iris_x = float(right_eye["offset"]["h"])
    right_iris_y = float(right_eye["offset"]["v"])
    right_eye_open = float(right_eye["openness"])

    # Final validation: check eye openness threshold
    if left_eye_open < CLOSED_OPENNESS or right_eye_open < CLOSED_OPENNESS:
        return {"valid": False, "reason": "Eye nearly closed", "timestamp": timestamp, "features": None}

    features = [left_iris_x, left_iris_y, left_eye_open,
                 right_iris_x, right_iris_y, right_eye_open]

    result = {
        "valid": True,
        "timestamp": timestamp,
        "features": features,
        "left": {
            "iris_x": left_iris_x,
            "iris_y": left_iris_y,
            "eye_open": left_eye_open,
        },
        "right": {
            "iris_x": right_iris_x,
            "iris_y": right_iris_y,
            "eye_open": right_eye_open,
        },
    }

    return result

def draw_eyes(view, eyes, frame_w):
    if eyes is None:
        return

    fh, fw = view.shape[:2]

    # The preview keeps the camera's aspect ratio, so one scale covers both axes.
    scale = fw / frame_w

    def to_px(point):
        return (int(point[0] * fw), int(point[1] * fh))

    for name in ("left", "right"):
        # if an eye is not detected, skip drawing it
        if name not in eyes:
            continue

        e = eyes[name]
        pts = np.array([to_px(p) for p in e["outline"]], dtype=np.int32)
        cv2.polylines(view, [pts], True, (0, 255, 255), 1)
        for p in e["corners"].values():
            cv2.circle(view, to_px(p), 2, (50, 255, 50), -1)
        for p in e["lids"]["upper"]:
            cv2.circle(view, to_px(p), 2, (0, 165, 255), -1)
        for p in e["lids"]["lower"]:
            cv2.circle(view, to_px(p), 2, (0, 165, 255), -1)

        iris_ring = np.array([to_px(p) for p in e["iris"]["ring"]], dtype=np.int32)
        cv2.polylines(view, [iris_ring], True, (255, 105, 180), 1)

        cx, cy = e["iris"]["center"]
        radius = max(2, int(e["iris"]["diameter_px"] * scale / 2.0))
        cv2.circle(view, (int(cx * fw), int(cy * fh)), radius, (255, 105, 180), 1)
        cv2.circle(view, (int(cx * fw), int(cy * fh)), 2, (0, 0, 255), -1)

        state = "COVERED" if e.get("covered") else "CLOSED" if e["closed"] else "OPEN"

        cv2.putText(view, f"{name}: {state}", (int(e["corners"]["left"][0] * fw) - 20, int(e["corners"]["left"][1] * fh) - 10,), 
                    cv2.FONT_HERSHEY_SIMPLEX, 0.45, (255, 255, 255), 1)

    g = eyes["gaze"]

    if g["h"] is None:
        label = "closed"

    else:
        label = f"{g['direction']}  h {g['h']:+.3f}  v {g['v']:+.3f}"

    cv2.putText(view, f"gaze: {label}", (10, 60), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 0), 2)

# Debugging stuff, will delete later
def print_eye_debug(eyes):
    if eyes is None:
        return

    left, right, gaze = eyes["left"], eyes["right"], eyes["gaze"]

    def state(eye):
        return "CLOSED" if eye["closed"] else "OPEN  "

    if gaze["h"] is None:
        gaze_text = "CLOSED"
    else:
        gaze_text = f"{gaze['direction']:<7} h={gaze['h']:+.3f} v={gaze['v']:+.3f}"

    left_h = left["offset"]["h"]
    left_v = left["offset"]["v"]
    right_h = right["offset"]["h"]
    right_v = right["offset"]["v"]
    binocular = gaze["binocular_error"]

    if binocular["h"] is None:
        binocular_text = "-"
    else:
        binocular_text = (
            f"h={binocular['h']:.3f} "
            f"v={binocular['v']:.3f}"
        )


def _main():
    frame_w = None
    frame_h = None
    last_print = 0.0

    # One smoother persists across frames.
    gaze_smoother = GazeSmoother(alpha=SMOOTHING_ALPHA)

    # One validator, per eye, persists across frames.
    eye_validator = {
            "left": EyeValidator(),
            "right": EyeValidator(),
    }

    for line in sys.stdin:
        try:
            msg = json.loads(line)
        except (ValueError, TypeError):
            continue

        if msg.get("type") == "ready":
            frame_w = msg.get("width")
            frame_h = msg.get("height")

            # New stream -> reset smoothing.
            gaze_smoother.reset()
            eye_validator["left"].reset()
            eye_validator["right"].reset()
            continue

        if msg.get("type") == "error":
            print("tracker error:", msg.get("message"), file=sys.stderr)
            continue

        if msg.get("type") != "frame":
            continue

        if not frame_w or not frame_h:
            continue

        faces = msg.get("faces")

        if not faces:
            gaze_smoother.reset()
            eye_validator["left"].reset()
            eye_validator["right"].reset()
            continue

        # For now we track the first detected face.
        face = faces[0]

        landmarks = face.get("landmarks")

        if landmarks is None:
            continue

        eyes = extract_eyes(landmarks, frame_w, frame_h, face.get("blendshapes"), gaze_smoother=gaze_smoother, eye_validator=eye_validator)

        if eyes is None:
            continue

        now = time.monotonic()

        if now - last_print >= 0.1:
            last_print = now
            print_eye_debug(eyes)

if __name__ == "__main__":
    _main()