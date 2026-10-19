import json
import math
import tempfile
import unittest
from pathlib import Path

from tracker.eye_extractor import extract_eyes, extract_feature_vector
from tracker.gaze_model import (estimate_gaze, estimate_gaze_from_features, eye_angle, GazeParams,
                                params_from_dict, load_params, DEFAULT_PARAMS,
                                K_H, K_V, MAX_SIN, GAIN_X, GAIN_Y)
# reuse the fake landmark builder
from tests.test_eye_extractor import build_eye


# an eye dict shaped like the ones extract_eyes() builds, with only the fields gaze_model reads
def make_eye(h=0.0, v=0.0, closed=False, covered=False):
    return {"offset": {"h": h, "v": v}, "closed": closed, "covered": covered}


# eyes dict like extract_eyes() returns. an eye that is None is left out, like when the validator drops it.
def make_eyes(left=None, right=None):
    eyes = {"gaze": {"h": 9.9, "v": 9.9, "raw": {"h": 9.9, "v": 9.9}}}
    if left is not None:
        eyes["left"] = left
    if right is not None:
        eyes["right"] = right
    return eyes


# the iris offset an eye turned by this many degrees should produce (the model's geometry run forward)
def offset_for(degrees, k=K_H):
    return k * math.sin(math.radians(degrees))


# the same, shaped like the result of extract_feature_vector()
def make_features(left_h=0.0, left_v=0.0, right_h=0.0, right_v=0.0):
    return {
        "valid": True,
        "timestamp": None,
        "features": [left_h, left_v, 0.3, right_h, right_v, 0.3],
        "left": {"iris_x": left_h, "iris_y": left_v, "eye_open": 0.3},
        "right": {"iris_x": right_h, "iris_y": right_v, "eye_open": 0.3},
    }


class EyeAngleTest(unittest.TestCase):
    # the whole point of the model: undo offset = k * sin(angle)
    def test_inverts_the_eyeball_geometry(self):
        for degrees in (-40, -10, 0, 10, 40):
            with self.subTest(degrees=degrees):
                self.assertAlmostEqual(math.degrees(eye_angle(offset_for(degrees), K_H)), degrees, places=6)

    def test_zero_offset_is_zero_degrees(self):
        self.assertEqual(eye_angle(0.0, K_H), 0.0)

    # the iris moves less per degree the further it turns, so a linear map would be too small at the edges
    def test_angle_grows_faster_than_the_offset(self):
        self.assertGreater(eye_angle(0.5, K_H), 2 * eye_angle(0.25, K_H))

    def test_is_symmetric(self):
        self.assertAlmostEqual(eye_angle(-0.3, K_H), -eye_angle(0.3, K_H))

    # an offset bigger than the geometry allows must not crash asin
    def test_huge_offset_is_clamped(self):
        limit = math.asin(MAX_SIN)
        self.assertAlmostEqual(eye_angle(50.0, K_H), limit)
        self.assertAlmostEqual(eye_angle(-50.0, K_H), -limit)

    def test_larger_k_means_a_smaller_angle(self):
        self.assertLess(eye_angle(0.3, 1.0), eye_angle(0.3, 0.7))


class EstimateGazeTest(unittest.TestCase):
    def test_centered_iris_is_straight_ahead(self):
        result = estimate_gaze(make_eyes(make_eye(), make_eye()))
        self.assertTrue(result["valid"])
        self.assertAlmostEqual(result["h_deg"], 0.0)
        self.assertAlmostEqual(result["v_deg"], 0.0)
        self.assertAlmostEqual(result["x"], 0.5)
        self.assertAlmostEqual(result["y"], 0.5)
        self.assertEqual(result["eyes_used"], 2)

    def test_recovers_a_known_horizontal_angle(self):
        eye = make_eye(h=offset_for(12))
        result = estimate_gaze(make_eyes(eye, eye))
        self.assertAlmostEqual(result["h_deg"], 12.0, places=2)
        self.assertAlmostEqual(result["v_deg"], 0.0)

    def test_recovers_a_known_vertical_angle(self):
        eye = make_eye(v=offset_for(8, K_V))
        result = estimate_gaze(make_eyes(eye, eye))
        self.assertAlmostEqual(result["v_deg"], 8.0, places=2)
        self.assertAlmostEqual(result["h_deg"], 0.0)

    # + is the person's right and down, - is left and up
    def test_signs(self):
        right = estimate_gaze(make_eyes(make_eye(h=0.2), make_eye(h=0.2)))
        left = estimate_gaze(make_eyes(make_eye(h=-0.2), make_eye(h=-0.2)))
        down = estimate_gaze(make_eyes(make_eye(v=0.1), make_eye(v=0.1)))
        up = estimate_gaze(make_eyes(make_eye(v=-0.1), make_eye(v=-0.1)))

        self.assertGreater(right["h_deg"], 0)
        self.assertGreater(right["x"], 0.5)
        self.assertLess(left["h_deg"], 0)
        self.assertLess(left["x"], 0.5)
        self.assertGreater(down["v_deg"], 0)
        self.assertGreater(down["y"], 0.5)
        self.assertLess(up["v_deg"], 0)
        self.assertLess(up["y"], 0.5)

    def test_opposite_offsets_mirror_around_the_center(self):
        a = estimate_gaze(make_eyes(make_eye(h=0.25, v=0.1), make_eye(h=0.25, v=0.1)))
        b = estimate_gaze(make_eyes(make_eye(h=-0.25, v=-0.1), make_eye(h=-0.25, v=-0.1)))
        self.assertAlmostEqual(a["h_deg"], -b["h_deg"])
        self.assertAlmostEqual(a["v_deg"], -b["v_deg"])
        self.assertAlmostEqual(a["x"] - 0.5, 0.5 - b["x"], places=3)
        self.assertAlmostEqual(a["y"] - 0.5, 0.5 - b["y"], places=3)

    # horizontal and vertical don't affect each other
    def test_axes_are_independent(self):
        only_h = estimate_gaze(make_eyes(make_eye(h=0.2), make_eye(h=0.2)))
        both = estimate_gaze(make_eyes(make_eye(h=0.2, v=0.1), make_eye(h=0.2, v=0.1)))
        self.assertAlmostEqual(only_h["h_deg"], both["h_deg"])
        self.assertAlmostEqual(only_h["x"], both["x"])

    # the placeholder gains: +-15 degrees horizontal and +-10 degrees vertical span the whole screen
    def test_default_gains_span_the_screen(self):
        h = offset_for(15)
        v = offset_for(10, K_V)
        right = estimate_gaze(make_eyes(make_eye(h=h), make_eye(h=h)))
        left = estimate_gaze(make_eyes(make_eye(h=-h), make_eye(h=-h)))
        down = estimate_gaze(make_eyes(make_eye(v=v), make_eye(v=v)))
        up = estimate_gaze(make_eyes(make_eye(v=-v), make_eye(v=-v)))
        self.assertAlmostEqual(right["x"], 1.0, places=3)
        self.assertAlmostEqual(left["x"], 0.0, places=3)
        self.assertAlmostEqual(down["y"], 1.0, places=3)
        self.assertAlmostEqual(up["y"], 0.0, places=3)

    # the screen position is where the line of sight meets the screen: center + gain * tan(angle)
    def test_screen_position_is_gain_times_tan_of_the_angle(self):
        eye = make_eye(h=offset_for(7), v=offset_for(-4, K_V))
        result = estimate_gaze(make_eyes(eye, eye))
        self.assertAlmostEqual(result["x"], 0.5 + GAIN_X * math.tan(math.radians(7)), places=3)
        self.assertAlmostEqual(result["y"], 0.5 + GAIN_Y * math.tan(math.radians(-4)), places=3)

    def test_extreme_offset_stays_finite(self):
        result = estimate_gaze(make_eyes(make_eye(h=50.0, v=-50.0), make_eye(h=50.0, v=-50.0)))
        self.assertTrue(result["valid"])
        for key in ("h_deg", "v_deg", "x", "y"):
            self.assertTrue(math.isfinite(result[key]))
        self.assertLessEqual(abs(result["h_deg"]), math.degrees(math.asin(MAX_SIN)) + 0.01)

    # both eyes are converted to angles first, then averaged
    def test_both_eyes_are_averaged(self):
        result = estimate_gaze(make_eyes(make_eye(h=0.1), make_eye(h=0.3)))
        expected = math.degrees((eye_angle(0.1, K_H) + eye_angle(0.3, K_H)) / 2)
        self.assertAlmostEqual(result["h_deg"], expected, places=2)
        self.assertEqual(result["eyes_used"], 2)

    def test_per_eye_breakdown(self):
        result = estimate_gaze(make_eyes(make_eye(h=0.1, v=0.05), make_eye(h=0.3, v=-0.05)))
        self.assertAlmostEqual(result["left"]["h_deg"], math.degrees(eye_angle(0.1, K_H)), places=2)
        self.assertAlmostEqual(result["right"]["h_deg"], math.degrees(eye_angle(0.3, K_H)), places=2)
        self.assertAlmostEqual(result["left"]["v_deg"], math.degrees(eye_angle(0.05, K_V)), places=2)
        self.assertAlmostEqual(result["right"]["v_deg"], math.degrees(eye_angle(-0.05, K_V)), places=2)

    # the smoothed debug numbers in eyes["gaze"] are not an input (make_eyes fills them with 9.9)
    def test_ignores_the_gaze_field(self):
        result = estimate_gaze(make_eyes(make_eye(), make_eye()))
        self.assertAlmostEqual(result["h_deg"], 0.0)
        self.assertAlmostEqual(result["v_deg"], 0.0)

    def test_per_eye_horizontal_baselines_correct_offsets(self):
        params = GazeParams(left_h_baseline=0.10, right_h_baseline=-0.05)
        target_offset = offset_for(10)
        eyes = make_eyes(left=make_eye(h=target_offset + 0.10), right=make_eye(h=target_offset - 0.05))
        result = estimate_gaze(eyes, params)
        self.assertTrue(result["valid"])
        self.assertAlmostEqual(result["left"]["h_deg"], 10.0, places=2)
        self.assertAlmostEqual(result["right"]["h_deg"], 10.0, places=2)
        self.assertAlmostEqual(result["h_deg"], 10.0, places=2)

    def test_per_eye_vertical_baselines_correct_offsets(self):
        params = GazeParams(left_v_baseline=0.05, right_v_baseline=-0.03)
        target_offset = offset_for(8, K_V)
        eyes = make_eyes(left=make_eye(v=target_offset + 0.05), right=make_eye(v=target_offset - 0.03))
        result = estimate_gaze(eyes, params)
        self.assertAlmostEqual(result["left"]["v_deg"], 8.0, places=2)
        self.assertAlmostEqual(result["right"]["v_deg"], 8.0, places=2)
        self.assertAlmostEqual(result["v_deg"], 8.0, places=2)

    def test_eye_disagreement_is_reported_in_diagnostic_mode(self):
        result = estimate_gaze(make_eyes(left=make_eye(h=0.1), right=make_eye(h=0.3)))
        self.assertTrue(result["valid"])
        self.assertEqual(result["eyes_used"], 2)
        self.assertEqual(result["reason"], "Diagnostic: eyes disagree")
        self.assertEqual(result["confidence"], "diagnostic_both")
        self.assertGreater(result["disagree_h_deg"], GazeParams().max_disagree_h_deg)


class UnusableEyeTest(unittest.TestCase):
    # a wink: the open eye alone gives the estimate, the closed eye's garbage doesn't leak in
    def test_closed_eye_is_ignored(self):
        winked = estimate_gaze(make_eyes(make_eye(h=0.9, v=0.9, closed=True), make_eye(h=0.1)))
        open_only = estimate_gaze(make_eyes(right=make_eye(h=0.1)))
        self.assertTrue(winked["valid"])
        self.assertEqual(winked["eyes_used"], 1)
        self.assertAlmostEqual(winked["h_deg"], open_only["h_deg"])
        self.assertAlmostEqual(winked["v_deg"], open_only["v_deg"])
        self.assertIsNone(winked["left"])

    # MediaPipe makes up the numbers of a covered eye
    def test_covered_eye_is_ignored(self):
        result = estimate_gaze(make_eyes(make_eye(h=0.1), make_eye(h=0.9, covered=True)))
        self.assertEqual(result["eyes_used"], 1)
        self.assertAlmostEqual(result["h_deg"], math.degrees(eye_angle(0.1, K_H)), places=2)
        self.assertIsNone(result["right"])

    # the validator removed one eye as a glitch, the other one is enough
    def test_missing_eye_is_fine(self):
        for eyes in (make_eyes(left=make_eye(h=0.1)), make_eyes(right=make_eye(h=0.1))):
            with self.subTest(eyes=sorted(k for k in eyes if k != "gaze")):
                result = estimate_gaze(eyes)
                self.assertTrue(result["valid"])
                self.assertEqual(result["eyes_used"], 1)

    def assertInvalid(self, result, reason):
        self.assertFalse(result["valid"])
        self.assertEqual(result["reason"], reason)
        self.assertEqual(result["eyes_used"], 0)
        for key in ("h_deg", "v_deg", "x", "y"):
            self.assertIsNone(result[key])

    def test_no_eye_data(self):
        self.assertInvalid(estimate_gaze(None), "No eye data")

    def test_both_eyes_closed(self):
        eyes = make_eyes(make_eye(closed=True), make_eye(closed=True))
        self.assertInvalid(estimate_gaze(eyes), "No usable eye")

    def test_both_eyes_covered(self):
        eyes = make_eyes(make_eye(covered=True), make_eye(covered=True))
        self.assertInvalid(estimate_gaze(eyes), "No usable eye")

    def test_one_covered_one_closed(self):
        eyes = make_eyes(make_eye(covered=True), make_eye(closed=True))
        self.assertInvalid(estimate_gaze(eyes), "No usable eye")

    def test_no_eyes_in_the_dict(self):
        self.assertInvalid(estimate_gaze(make_eyes()), "No usable eye")

    def test_non_finite_offset_is_skipped(self):
        for bad in (float("nan"), float("inf")):
            with self.subTest(bad=bad):
                result = estimate_gaze(make_eyes(make_eye(h=bad), make_eye(h=0.1)))
                self.assertEqual(result["eyes_used"], 1)
                self.assertAlmostEqual(result["h_deg"], math.degrees(eye_angle(0.1, K_H)), places=2)

    def test_both_offsets_non_finite(self):
        eyes = make_eyes(make_eye(v=float("nan")), make_eye(h=float("inf")))
        self.assertInvalid(estimate_gaze(eyes), "No usable eye")

    def test_eye_without_offset_is_skipped(self):
        eyes = make_eyes({"closed": False, "covered": False}, make_eye(h=0.1))
        self.assertEqual(estimate_gaze(eyes)["eyes_used"], 1)


class ParamsTest(unittest.TestCase):
    EYES = make_eyes(make_eye(h=0.3, v=0.1), make_eye(h=0.3, v=0.1))

    def test_params_none_uses_the_defaults(self):
        self.assertEqual(estimate_gaze(self.EYES), estimate_gaze(self.EYES, GazeParams()))

    def test_k_h_only_changes_the_horizontal_angle(self):
        default = estimate_gaze(self.EYES)
        custom = estimate_gaze(self.EYES, GazeParams(k_h=1.0))
        self.assertLess(custom["h_deg"], default["h_deg"])
        self.assertAlmostEqual(custom["v_deg"], default["v_deg"])

    def test_k_v_only_changes_the_vertical_angle(self):
        default = estimate_gaze(self.EYES)
        custom = estimate_gaze(self.EYES, GazeParams(k_v=1.0))
        self.assertLess(custom["v_deg"], default["v_deg"])
        self.assertAlmostEqual(custom["h_deg"], default["h_deg"])

    def test_screen_center_moves_the_straight_ahead_point(self):
        result = estimate_gaze(make_eyes(make_eye(), make_eye()), GazeParams(center_x=0.3, center_y=0.6))
        self.assertAlmostEqual(result["x"], 0.3)
        self.assertAlmostEqual(result["y"], 0.6)

    def test_gain_scales_the_distance_from_the_center(self):
        eye = make_eye(h=offset_for(10), v=offset_for(10, K_V))
        eyes = make_eyes(eye, eye)
        near = estimate_gaze(eyes, GazeParams(gain_x=1.0, gain_y=1.0))
        far = estimate_gaze(eyes, GazeParams(gain_x=2.0, gain_y=2.0))
        self.assertAlmostEqual(far["x"] - 0.5, 2 * (near["x"] - 0.5), places=3)
        self.assertAlmostEqual(far["y"] - 0.5, 2 * (near["y"] - 0.5), places=3)


class FeatureVectorInputTest(unittest.TestCase):
    # same numbers in, same answer out, whichever way the eyes arrive
    def test_matches_estimate_gaze(self):
        from_features = estimate_gaze_from_features(make_features(0.1, 0.05, 0.3, -0.02))
        from_eyes = estimate_gaze(make_eyes(make_eye(h=0.1, v=0.05), make_eye(h=0.3, v=-0.02)))
        self.assertEqual(from_features, from_eyes)

    def test_passes_params_through(self):
        features = make_features(0.3, 0.1, 0.3, 0.1)
        params = GazeParams(k_h=1.0)
        self.assertEqual(estimate_gaze_from_features(features, params),
                         estimate_gaze(make_eyes(make_eye(h=0.3, v=0.1), make_eye(h=0.3, v=0.1)), params))

    # extract_feature_vector says why it rejected the frame, that reason has to reach the caller
    def test_invalid_feature_vector_keeps_its_reason(self):
        rejected = {"valid": False, "reason": "Eye closed", "timestamp": None, "features": None}
        result = estimate_gaze_from_features(rejected)
        self.assertFalse(result["valid"])
        self.assertEqual(result["reason"], "Eye closed")
        self.assertIsNone(result["x"])

    def test_missing_feature_vector(self):
        for missing in (None, {}):
            with self.subTest(missing=missing):
                result = estimate_gaze_from_features(missing)
                self.assertFalse(result["valid"])
                self.assertEqual(result["reason"], "No feature data")


IRIS_POINTS = {"left": (473, 474, 475, 476, 477), "right": (468, 469, 470, 471, 472)}


# both eyes at normal size, with the iris moved inside each eye (as a fraction of the image)
def build_landmarks(iris_dx=0.0, iris_dy=0.0):
    right = build_eye("right", center=(0.65, 0.5), width=0.24, height=0.08)
    left = build_eye("left", center=(0.35, 0.5), width=0.24, height=0.08)
    for pts, name in ((left, "left"), (right, "right")):
        pts[list(IRIS_POINTS[name])] += (iris_dx, iris_dy)
    return right + left


# fake landmarks -> extract_eyes -> estimate_gaze, so the field names and signs the two modules agree on are tested
class EndToEndTest(unittest.TestCase):
    def gaze(self, iris_dx=0.0, iris_dy=0.0):
        return estimate_gaze(extract_eyes(build_landmarks(iris_dx, iris_dy), 1000, 1000))

    def test_centered_iris(self):
        result = self.gaze()
        self.assertTrue(result["valid"])
        self.assertAlmostEqual(result["h_deg"], 0.0, delta=0.05)
        self.assertAlmostEqual(result["v_deg"], 0.0, delta=0.05)
        self.assertEqual(result["eyes_used"], 2)

    def test_iris_moved_right(self):
        self.assertGreater(self.gaze(iris_dx=0.03)["h_deg"], 5)

    def test_iris_moved_left(self):
        self.assertLess(self.gaze(iris_dx=-0.03)["h_deg"], -5)

    def test_iris_moved_down(self):
        self.assertGreater(self.gaze(iris_dy=0.02)["v_deg"], 5)

    def test_iris_moved_up(self):
        self.assertLess(self.gaze(iris_dy=-0.02)["v_deg"], -5)

    # a 30 px shift in an eye that is 240 px wide is an offset of 0.25
    def test_known_shift_gives_the_expected_angle(self):
        expected = math.degrees(math.asin(0.25 / K_H))
        self.assertAlmostEqual(self.gaze(iris_dx=0.03)["h_deg"], expected, delta=0.05)

    def test_feature_vector_path_agrees(self):
        eyes = extract_eyes(build_landmarks(0.03, 0.02), 1000, 1000)
        features = extract_feature_vector(eyes)
        self.assertTrue(features["valid"])
        self.assertEqual(estimate_gaze_from_features(features), estimate_gaze(eyes))

    # a wink from the extractor's blendshapes: the open eye is used on its own
    def test_winked_eye_is_not_used(self):
        blend = {"eyeBlinkLeft": 0.9, "eyeBlinkRight": 0.05}
        eyes = extract_eyes(build_landmarks(0.03, 0.0), 1000, 1000, blend)
        result = estimate_gaze(eyes)
        self.assertEqual(result["eyes_used"], 1)
        self.assertIsNone(result["left"])


class FittedCalibrationTest(unittest.TestCase):
    """The app fits these numbers (app/Calibration/GazeGrid/GazeFit.cs) and expects estimate_gaze() to give exactly these x.
    GazeFitTests.Predictions_match_the_tracker has the same numbers, change both together."""

    PARAMS = GazeParams(k_h=3.0, left_h_baseline=-0.1417, right_h_baseline=0.13, center_x=0.4852, gain_x=3.5464)
    # median iris position per eye at the nine-point targets of a real calibration, and where they were
    TARGETS = [(-0.435, -0.322, 0.0411), (-0.133, 0.129, 0.4898), (0.291, 0.477, 0.95), (0.278, 0.484, 0.9464),
               (-0.144, 0.104, 0.4685), (-0.441, -0.286, 0.0593), (-0.374, -0.145, 0.1843), (-0.148, 0.157, 0.4974),
               (0.160, 0.465, 0.8637)]

    def test_fitted_numbers_give_what_the_app_expects(self):
        for left, right, expected in self.TARGETS:
            eyes = {"left": make_eye(h=left), "right": make_eye(h=right)}
            self.assertAlmostEqual(estimate_gaze(eyes, self.PARAMS)["x"], expected, places=3)

    def test_leaning_moves_x_by_the_head_correction(self):
        params = params_from_dict({"k_h": 3.0, "left_h_baseline": -0.1417, "right_h_baseline": 0.13, "center_x": 0.4852,
                                   "gain_x": 3.5464, "head_x_gain": 1.5, "head_x_ref": 0.47})
        eyes = {"left": make_eye(h=-0.133), "right": make_eye(h=0.129)}
        at_ref = estimate_gaze(eyes, params, face_x=0.47)["x"]
        leaned = estimate_gaze(eyes, params, face_x=0.57)["x"]

        self.assertAlmostEqual(at_ref, 0.4898, places=3)  # where the face was during calibration: no change
        self.assertAlmostEqual(leaned - at_ref, 0.15, places=3)  # 1.5 per 1.0 across the image, the face moved 0.1
        # without knowing where the face is, nothing is corrected
        self.assertAlmostEqual(estimate_gaze(eyes, params)["x"], at_ref, places=3)

    def test_fitted_up_down_comes_from_the_lids(self):
        # GazeFitTests.Up_down_is_fitted_from_the_lids fits these numbers from a real calibration
        params = params_from_dict({"y_const": 3.5225, "y_openness": -9.068, "y_iris": 5.4409})
        eyes = {"left": dict(make_eye(v=-0.05), openness=0.33), "right": dict(make_eye(v=-0.03), openness=0.31)}
        # 3.5225 - 9.068 * 0.32 + 5.4409 * -0.04
        self.assertAlmostEqual(estimate_gaze(eyes, params)["y"], 0.4031, places=3)

        # lids further apart (looking up) puts it higher on the screen
        wide = {"left": dict(make_eye(v=-0.05), openness=0.36), "right": dict(make_eye(v=-0.03), openness=0.36)}
        self.assertLess(estimate_gaze(wide, params)["y"], 0.4031)

    def test_up_down_uses_only_the_open_eye_in_a_wink(self):
        params = params_from_dict({"y_const": 3.5225, "y_openness": -9.068, "y_iris": 5.4409})
        eyes = {"left": dict(make_eye(v=-0.04), openness=0.32), "right": dict(make_eye(v=0.2, closed=True), openness=0.05)}
        self.assertAlmostEqual(estimate_gaze(eyes, params)["y"], 3.5225 - 9.068 * 0.32 + 5.4409 * -0.04, places=3)

    def test_without_fitted_up_down_the_angle_model_is_used(self):
        eyes = {"left": dict(make_eye(v=0.1), openness=0.3), "right": dict(make_eye(v=0.1), openness=0.3)}
        self.assertEqual(estimate_gaze(eyes, DEFAULT_PARAMS)["y"], estimate_gaze({"left": make_eye(v=0.1), "right": make_eye(v=0.1)})["y"])

    def test_fitted_up_down_without_openness_falls_back(self):
        params = params_from_dict({"y_const": 3.5225, "y_openness": -9.068, "y_iris": 5.4409})
        eyes = {"left": make_eye(v=0.1), "right": make_eye(v=0.1)}
        self.assertEqual(estimate_gaze(eyes, params)["y"], estimate_gaze(eyes, DEFAULT_PARAMS)["y"])

    def test_up_down_curves(self):
        # the arc: looking to the side moves y by y_arc * tan(angle)^2. uneven: + y_openness_sq * openness^2
        params = params_from_dict({"k_h": 3.0, "y_const": 3.5, "y_openness": -9.0, "y_arc": -0.6, "y_openness_sq": 4.0})
        eyes = lambda h: {"left": dict(make_eye(h=h), openness=0.3), "right": dict(make_eye(h=h), openness=0.3)}
        t = math.tan(math.asin(0.3 / 3.0))
        self.assertAlmostEqual(estimate_gaze(eyes(0.0), params)["y"], 3.5 - 9.0 * 0.3 + 4.0 * 0.09, places=3)
        self.assertAlmostEqual(estimate_gaze(eyes(0.3), params)["y"], 3.5 - 9.0 * 0.3 + 4.0 * 0.09 - 0.6 * t * t, places=3)

    def test_up_down_curves_need_a_fitted_up_down(self):
        eyes = {"left": dict(make_eye(h=0.3, v=0.1), openness=0.3), "right": dict(make_eye(h=0.3, v=0.1), openness=0.3)}
        self.assertEqual(estimate_gaze(eyes, params_from_dict({"y_arc": -0.6, "y_openness_sq": 4.0}))["y"],
                         estimate_gaze(eyes, DEFAULT_PARAMS)["y"])

    def test_looking_down_squeeze_changes_left_right_by_lid_openness(self):
        params = params_from_dict({"k_h": 3.0, "center_x": 0.5, "gain_x": 3.6, "x_squeeze": -12.0, "openness_ref": 0.32})
        eyes = lambda openness: {"left": dict(make_eye(h=0.3), openness=openness), "right": dict(make_eye(h=0.3), openness=openness)}
        t = math.tan(math.asin(0.3 / 3.0))
        self.assertAlmostEqual(estimate_gaze(eyes(0.32), params)["x"], 0.5 + 3.6 * t, places=3)  # at the reference: no change
        self.assertAlmostEqual(estimate_gaze(eyes(0.27), params)["x"], 0.5 + 3.6 * t - 12.0 * t * -0.05, places=3)

    def test_leaning_shrinks_left_right_and_moves_up_down(self):
        params = params_from_dict({"k_h": 3.0, "center_x": 0.5, "gain_x": 3.6, "head_x_gain": 0.5, "head_x_shrink": -6.0,
                                   "head_x_ref": 0.5, "y_const": 3.5, "y_openness": -9.0, "head_y_lean": 0.8})
        eyes = {"left": dict(make_eye(h=0.3), openness=0.33), "right": dict(make_eye(h=0.3), openness=0.33)}
        t = math.tan(math.asin(0.3 / 3.0))
        leaned = estimate_gaze(eyes, params, face_x=0.65)
        self.assertAlmostEqual(leaned["x"], 0.5 + 3.6 * t + 0.5 * 0.15 - 6.0 * t * 0.15, places=3)
        self.assertAlmostEqual(leaned["y"], 3.5 - 9.0 * 0.33 + 0.8 * 0.15, places=3)

    def test_head_tilt_moves_up_down_only_with_fitted_up_down(self):
        tilt = {"head_y_pitch": 0.02, "pitch_ref": -15.0}
        eyes = {"left": dict(make_eye(v=-0.04), openness=0.33), "right": dict(make_eye(v=-0.04), openness=0.33)}
        fitted = params_from_dict({"y_const": 3.5, "y_openness": -9.0, **tilt})
        self.assertAlmostEqual(estimate_gaze(eyes, fitted, face_pitch=-7.0)["y"] - estimate_gaze(eyes, fitted, face_pitch=-15.0)["y"],
                               0.16, places=3)
        # without a fitted up/down the tilt numbers do nothing
        unfitted = params_from_dict(tilt)
        self.assertEqual(estimate_gaze(eyes, unfitted, face_pitch=-7.0)["y"], estimate_gaze(eyes, unfitted)["y"])

    def test_no_head_correction_by_default(self):
        eyes = {"left": make_eye(h=0.1), "right": make_eye(h=0.1)}
        self.assertEqual(estimate_gaze(eyes, DEFAULT_PARAMS, face_x=0.9)["x"], estimate_gaze(eyes, DEFAULT_PARAMS)["x"])

    def test_bad_face_position_is_ignored(self):
        params = GazeParams(head_x_gain=1.5, head_x_ref=0.5)
        eyes = {"left": make_eye(h=0.1), "right": make_eye(h=0.1)}
        self.assertEqual(estimate_gaze(eyes, params, face_x=float("nan"))["x"], estimate_gaze(eyes, params)["x"])


class SavedCalibrationTest(unittest.TestCase):
    """params_from_dict / load_params, for the files in app/Calibration/Saved/"""

    def test_empty_is_the_defaults(self):
        self.assertEqual(params_from_dict({}), DEFAULT_PARAMS)
        self.assertEqual(params_from_dict(None), DEFAULT_PARAMS)

    def test_saved_values_replace_the_defaults(self):
        p = params_from_dict({"k_h": 0.6, "center_y": 0.45, "left_h_baseline": -0.02})
        self.assertEqual((p.k_h, p.center_y, p.left_h_baseline), (0.6, 0.45, -0.02))
        self.assertEqual(p.k_v, DEFAULT_PARAMS.k_v)

    def test_unknown_and_bad_values_are_ignored(self):
        p = params_from_dict({"k_h": "lots", "k_v": float("nan"), "something_else": 3, "gain_x": True})
        self.assertEqual(p, DEFAULT_PARAMS)

    def test_impossible_values_give_the_defaults(self):
        # k_h = 0 would divide by zero
        self.assertEqual(params_from_dict({"k_h": 0, "center_x": 0.4}), DEFAULT_PARAMS)

    def test_not_a_dict(self):
        self.assertEqual(params_from_dict([1, 2]), DEFAULT_PARAMS)

    def test_calibrated_params_change_the_estimate(self):
        eyes = {"left": make_eye(h=0.1), "right": make_eye(h=0.1)}
        default = estimate_gaze(eyes)
        shifted = estimate_gaze(eyes, params_from_dict({"center_x": 0.3}))
        self.assertAlmostEqual(shifted["x"], default["x"] - 0.2, places=3)

    def test_load_params_reads_the_gaze_part_of_the_file(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "cal.json"
            path.write_text(json.dumps({"version": 1, "name": "desk", "gaze": {"k_v": 0.8}}), encoding="utf-8")
            self.assertEqual(load_params(path).k_v, 0.8)

    def test_load_params_with_no_gaze_part(self):
        # what the app saves until calibration works the numbers out
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "cal.json"
            path.write_text('{"version": 1, "gaze": {}}', encoding="utf-8")
            self.assertEqual(load_params(path), DEFAULT_PARAMS)

    def test_load_params_broken_file_raises(self):
        # face_tracker.py catches this and uses the defaults
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "cal.json"
            path.write_text("{ not json", encoding="utf-8")
            with self.assertRaises(ValueError):
                load_params(path)


if __name__ == "__main__":
    unittest.main()
