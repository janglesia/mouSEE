import unittest

import numpy as np

from tracker.eye_extractor import extract_eyes, extract_feature_vector, EyeValidator, MAX_REJECTIONS


RIGHT_EYE = {
    "left_corner": 133,
    "right_corner": 33,
    "upper_lid": (158, 159, 160),
    "lower_lid": (144, 145, 153),
    "iris": 468,
    "iris_ring": (469, 470, 471, 472),
    "blink": "eyeBlinkRight",
    "outline": (33, 7, 163, 144, 145, 153, 154, 155, 133, 173, 157, 158, 159, 160, 161, 246),
}

LEFT_EYE = {
    "left_corner": 263,
    "right_corner": 362,
    "upper_lid": (387, 386, 385),
    "lower_lid": (374, 373, 380),
    "iris": 473,
    "iris_ring": (474, 475, 476, 477),
    "blink": "eyeBlinkLeft",
    "outline": (362, 382, 381, 380, 374, 373, 390, 249, 263, 466, 388, 387, 386, 385, 384, 398),
}

# build fake eye landmarks for testing
def build_eye(eye, center=(0.5, 0.5), width=0.28, height=0.08):
    pts = np.zeros((478, 2), dtype=float)
    cx, cy = center
    half_w = width / 2.0
    left_x = cx - half_w
    right_x = cx + half_w
    upper_y = cy - height
    lower_y = cy + height

    if eye == "left":
        pts[263] = (left_x, cy)
        pts[362] = (right_x, cy)
        pts[387] = (cx, upper_y)
        pts[386] = (cx, cy - height * 0.5)
        pts[385] = (cx, cy - height * 0.2)
        pts[374] = (cx, lower_y)
        pts[373] = (cx, cy + height * 0.5)
        pts[380] = (cx, cy + height * 0.2)
        pts[473] = (cx, cy)
        pts[474] = (cx - 0.04, cy)
        pts[475] = (cx + 0.04, cy)
        pts[476] = (cx, cy - 0.04)
        pts[477] = (cx, cy + 0.04)
    else:
        pts[133] = (left_x, cy)
        pts[33] = (right_x, cy)
        pts[158] = (cx, upper_y)
        pts[159] = (cx, cy - height * 0.5)
        pts[160] = (cx, cy - height * 0.2)
        pts[144] = (cx, lower_y)
        pts[145] = (cx, cy + height * 0.5)
        pts[153] = (cx, cy + height * 0.2)
        pts[468] = (cx, cy)
        pts[469] = (cx - 0.04, cy)
        pts[470] = (cx + 0.04, cy)
        pts[471] = (cx, cy - 0.04)
        pts[472] = (cx, cy + 0.04)
    return pts


class EyeExtractorTest(unittest.TestCase):
    # tests when one eye is invalid, the valid eye should be kept
    def test_extract_eyes_keeps_valid_eye_when_other_is_invalid(self):
        landmarks = np.zeros((478, 2), dtype=float)
        landmarks[:] = 0.5

        right = build_eye("right", center=(0.6, 0.5), width=0.22, height=0.06)
        left = build_eye("left", center=(0.3, 0.5), width=0.22, height=0.06)

        # Make the left eye invalid by collapsing its corners together.
        left[263] = (0.3, 0.5)
        left[362] = (0.3, 0.5)

        landmarks = right + left
        landmarks[landmarks == 0.0] = 0.5

        eyes = extract_eyes(landmarks, 1000, 1000)

        self.assertIsNotNone(eyes)
        self.assertIn("right", eyes)
        self.assertNotIn("left", eyes)
        self.assertIn("gaze", eyes)
        self.assertIsNotNone(eyes["gaze"]["h"])

    # test for normal case (both eyes valid)
    def test_extract_eyes_keeps_both_detectable_eyes_consistent(self):
        right = build_eye("right", center=(0.65, 0.5), width=0.24, height=0.08)
        left = build_eye("left", center=(0.35, 0.5), width=0.24, height=0.08)
        landmarks = right + left
        eyes = extract_eyes(landmarks, 1000, 1000)

        self.assertIsNotNone(eyes)
        self.assertIn("left", eyes)
        self.assertIn("right", eyes)
        self.assertIn("gaze", eyes)
        self.assertIsNotNone(eyes["left"]["iris"]["center"])
        self.assertIsNotNone(eyes["right"]["iris"]["center"])


IRIS_POINTS = {"left": (473, 474, 475, 476, 477), "right": (468, 469, 470, 471, 472)}
OPEN = {"eyeBlinkLeft": 0.05, "eyeBlinkRight": 0.05}
CLOSED = {"eyeBlinkLeft": 0.9, "eyeBlinkRight": 0.9}


# both eyes, with optional changes to one eye to fake a glitch
def build_face(width=0.24, left_width=None, right_width=None, left_height=0.08, right_height=0.08,
               left_iris_dx=0.0, left_iris_dy=0.0, left_iris_radius=None):
    right = build_eye("right", center=(0.65, 0.5), width=right_width or width, height=right_height)
    left = build_eye("left", center=(0.35, 0.5), width=left_width or width, height=left_height)
    left[list(IRIS_POINTS["left"])] += (left_iris_dx, left_iris_dy)
    if left_iris_radius is not None:
        cx, cy = left[473]
        left[474] = (cx - left_iris_radius, cy)
        left[475] = (cx + left_iris_radius, cy)
        left[476] = (cx, cy - left_iris_radius)
        left[477] = (cx, cy + left_iris_radius)
    return right + left


class EyeValidatorTest(unittest.TestCase):
    def setUp(self):
        self.validator = {"left": EyeValidator(), "right": EyeValidator()}

    def run_frame(self, landmarks, blendshapes=OPEN):
        return extract_eyes(landmarks, 1000, 1000, blendshapes, eye_validator=self.validator)

    # a rejected eye must not end up in the output
    def test_rejected_eye_is_removed_from_output(self):
        self.run_frame(build_face())
        eyes = self.run_frame(build_face(left_width=0.40))  # left eye suddenly 67% wider

        self.assertIsNotNone(eyes)
        self.assertNotIn("left", eyes)
        self.assertIn("right", eyes)

    def test_gaze_jump_is_rejected(self):
        self.run_frame(build_face())
        eyes = self.run_frame(build_face(left_iris_dx=0.11))  # iris jumps to the corner in one frame

        self.assertNotIn("left", eyes)
        self.assertIn("right", eyes)

    # one bad frame shouldn't affect the frames after it
    def test_normal_frame_after_glitch_is_accepted(self):
        self.run_frame(build_face())
        self.run_frame(build_face(left_width=0.40))
        eyes = self.run_frame(build_face())

        self.assertIn("left", eyes)
        self.assertIn("right", eyes)

    # e.g. leaning closer: the new size has to be accepted eventually, not rejected forever
    def test_lasting_change_is_accepted_after_max_rejections(self):
        self.run_frame(build_face())
        for _ in range(MAX_REJECTIONS):
            self.assertIsNone(self.run_frame(build_face(width=0.40)))

        eyes = self.run_frame(build_face(width=0.40))
        self.assertIn("left", eyes)
        self.assertIn("right", eyes)

    def test_tiny_iris_is_rejected_even_on_first_frame(self):
        eyes = self.run_frame(build_face(left_iris_radius=0.001))  # ~2 px wide iris

        self.assertNotIn("left", eyes)
        self.assertIn("right", eyes)

    # the iris jumps around while the lid covers it, a blink must still be reported as closed
    def test_closed_eye_is_kept_during_blink(self):
        self.run_frame(build_face())
        eyes = self.run_frame(build_face(left_iris_dx=0.11), blendshapes=CLOSED)

        self.assertIn("left", eyes)
        self.assertTrue(eyes["left"]["closed"])

    # after a blink the eye is compared to how it looked before the blink
    def test_closed_eye_does_not_become_reference(self):
        self.run_frame(build_face())
        self.run_frame(build_face(left_iris_dx=0.11), blendshapes=CLOSED)
        eyes = self.run_frame(build_face())

        self.assertIn("left", eyes)
        self.assertFalse(eyes["left"]["closed"])

    def test_reset_forgets_previous_eye(self):
        self.run_frame(build_face())
        self.validator["left"].reset()
        self.validator["right"].reset()
        eyes = self.run_frame(build_face(width=0.40))

        self.assertIn("left", eyes)
        self.assertIn("right", eyes)

    # MAX_EYE_WIDTH_JUMP is 35%: just under passes, just over doesn't
    def test_width_jump_threshold(self):
        self.run_frame(build_face())
        eyes = self.run_frame(build_face(left_width=0.24 * 1.34))
        self.assertIn("left", eyes)

        self.setUp()
        self.run_frame(build_face())
        eyes = self.run_frame(build_face(left_width=0.24 * 1.36))
        self.assertNotIn("left", eyes)

    def test_vertical_gaze_jump_is_rejected(self):
        self.run_frame(build_face())
        eyes = self.run_frame(build_face(left_iris_dy=0.11))

        self.assertNotIn("left", eyes)
        self.assertIn("right", eyes)

    # the other tests glitch the left eye, make sure the right one is checked too
    def test_right_eye_glitch_is_rejected(self):
        self.run_frame(build_face())
        eyes = self.run_frame(build_face(right_width=0.40))

        self.assertNotIn("right", eyes)
        self.assertIn("left", eyes)

    # good frames in between reset the count, so scattered glitches never get accepted
    def test_scattered_glitches_are_all_rejected(self):
        self.run_frame(build_face())
        # runs of glitches just short of the limit, so they'd add up past it if the count never reset
        for _ in range(3):
            for _ in range(MAX_REJECTIONS - 1):
                self.assertNotIn("left", self.run_frame(build_face(left_width=0.40)))
            self.assertIn("left", self.run_frame(build_face()))

    # only the gaze check is skipped for closed eyes, the size check still applies
    def test_closed_eye_with_width_jump_is_rejected(self):
        self.run_frame(build_face())
        eyes = self.run_frame(build_face(left_width=0.40), blendshapes=CLOSED)

        self.assertNotIn("left", eyes)
        self.assertIn("right", eyes)

    # starting with closed eyes leaves no reference, so the first open eye becomes it
    def test_first_open_eye_after_closed_start_becomes_reference(self):
        eyes = self.run_frame(build_face(), blendshapes=CLOSED)
        self.assertTrue(eyes["left"]["closed"])

        eyes = self.run_frame(build_face(left_iris_dx=0.11))
        self.assertIn("left", eyes)

        # the iris jumping back from there is a glitch compared to that reference
        eyes = self.run_frame(build_face())
        self.assertNotIn("left", eyes)

    # the validator is optional, without one nothing gets filtered
    def test_without_validator_nothing_is_filtered(self):
        extract_eyes(build_face(), 1000, 1000, OPEN)
        eyes = extract_eyes(build_face(left_width=0.40), 1000, 1000, OPEN)

        self.assertIn("left", eyes)
        self.assertIn("right", eyes)


# numbers based on a real recording: a winked eye only gets ~2/3 as open as the other one
# (height 0.05 vs 0.08 here) and its blink score only reaches ~0.2-0.35
class WinkTest(unittest.TestCase):
    def test_left_wink_is_closed(self):
        eyes = extract_eyes(build_face(left_height=0.05), 1000, 1000,
                            {"eyeBlinkLeft": 0.25, "eyeBlinkRight": 0.05})

        self.assertTrue(eyes["left"]["closed"])
        self.assertFalse(eyes["right"]["closed"])
        self.assertEqual(eyes["gaze"]["eyes_used"], 1)  # gaze comes from the open eye only

    def test_right_wink_is_closed(self):
        eyes = extract_eyes(build_face(right_height=0.05), 1000, 1000,
                            {"eyeBlinkLeft": 0.05, "eyeBlinkRight": 0.3})

        self.assertFalse(eyes["left"]["closed"])
        self.assertTrue(eyes["right"]["closed"])

    # eyes are never exactly the same size, a small difference isn't a wink
    def test_slightly_smaller_eye_is_not_closed(self):
        eyes = extract_eyes(build_face(left_height=0.072), 1000, 1000,  # 90% as open
                            {"eyeBlinkLeft": 0.25, "eyeBlinkRight": 0.05})

        self.assertFalse(eyes["left"]["closed"])

    # e.g. head turned: one eye looks smaller, but the blink scores don't agree it's closing
    def test_smaller_eye_without_higher_blink_is_not_closed(self):
        eyes = extract_eyes(build_face(left_height=0.05), 1000, 1000,
                            {"eyeBlinkLeft": 0.05, "eyeBlinkRight": 0.05})

        self.assertFalse(eyes["left"]["closed"])

    def test_wink_without_blendshapes_uses_openness_only(self):
        eyes = extract_eyes(build_face(left_height=0.05), 1000, 1000, None)

        self.assertTrue(eyes["left"]["closed"])
        self.assertFalse(eyes["right"]["closed"])

    def test_both_eyes_closed_still_works(self):
        eyes = extract_eyes(build_face(), 1000, 1000, CLOSED)

        self.assertTrue(eyes["left"]["closed"])
        self.assertTrue(eyes["right"]["closed"])
        self.assertEqual(eyes["gaze"]["direction"], "closed")

    # the validator runs after the wink check, so a winked eye gets the closed-eye treatment
    def test_winked_eye_is_kept_by_validator(self):
        validator = {"left": EyeValidator(), "right": EyeValidator()}
        extract_eyes(build_face(), 1000, 1000, OPEN, eye_validator=validator)
        eyes = extract_eyes(build_face(left_height=0.05, left_iris_dx=0.11), 1000, 1000,
                            {"eyeBlinkLeft": 0.25, "eyeBlinkRight": 0.05}, eye_validator=validator)

        self.assertIn("left", eyes)
        self.assertTrue(eyes["left"]["closed"])


class FeatureVectorTest(unittest.TestCase):
    def test_feature_vector_centered_iris(self):
        """Test feature vector with centered irises (no offset)."""
        right = build_eye("right", center=(0.65, 0.5), width=0.24, height=0.08)
        left = build_eye("left", center=(0.35, 0.5), width=0.24, height=0.08)
        landmarks = right + left
        eyes = extract_eyes(landmarks, 1000, 1000)

        result = extract_feature_vector(eyes, timestamp=12345)

        self.assertTrue(result["valid"])
        self.assertEqual(result["timestamp"], 12345)
        self.assertIsNotNone(result["features"])
        self.assertEqual(len(result["features"]), 6)

        # Centered iris should have approximately zero offsets
        left_iris_x, left_iris_y, left_eye_open = result["features"][0:3]
        right_iris_x, right_iris_y, right_eye_open = result["features"][3:6]

        self.assertAlmostEqual(left_iris_x, 0.0, places=2)
        self.assertAlmostEqual(left_iris_y, 0.0, places=2)
        self.assertAlmostEqual(right_iris_x, 0.0, places=2)
        self.assertAlmostEqual(right_iris_y, 0.0, places=2)

        # Eye openness should be positive
        self.assertGreater(left_eye_open, 0)
        self.assertGreater(right_eye_open, 0)

    def test_feature_vector_shifted_iris_right(self):
        """Test feature vector with iris shifted to the right within the eye."""
        right = build_eye("right", center=(0.65, 0.5), width=0.24, height=0.08)
        left = build_eye("left", center=(0.35, 0.5), width=0.24, height=0.08)

        # Shift iris centers to the right within each eye
        left[473] = (0.35 + 0.05, 0.5)  # Shift left eye iris right
        left[474] = (0.35 + 0.05 - 0.04, 0.5)
        left[475] = (0.35 + 0.05 + 0.04, 0.5)
        left[476] = (0.35 + 0.05, 0.5 - 0.04)
        left[477] = (0.35 + 0.05, 0.5 + 0.04)

        right[468] = (0.65 + 0.05, 0.5)  # Shift right eye iris right
        right[469] = (0.65 + 0.05 - 0.04, 0.5)
        right[470] = (0.65 + 0.05 + 0.04, 0.5)
        right[471] = (0.65 + 0.05, 0.5 - 0.04)
        right[472] = (0.65 + 0.05, 0.5 + 0.04)

        landmarks = right + left
        eyes = extract_eyes(landmarks, 1000, 1000)
        result = extract_feature_vector(eyes)

        self.assertTrue(result["valid"])
        left_iris_x = result["features"][0]
        right_iris_x = result["features"][3]

        # Positive offset indicates iris shifted toward the right corner
        self.assertGreater(left_iris_x, 0)
        self.assertGreater(right_iris_x, 0)

    def test_feature_vector_shifted_iris_up(self):
        """Test feature vector with iris shifted upward within the eye."""
        right = build_eye("right", center=(0.65, 0.5), width=0.24, height=0.08)
        left = build_eye("left", center=(0.35, 0.5), width=0.24, height=0.08)

        # Shift iris centers upward within each eye
        left[473] = (0.35, 0.5 - 0.03)
        left[474] = (0.35 - 0.04, 0.5 - 0.03)
        left[475] = (0.35 + 0.04, 0.5 - 0.03)
        left[476] = (0.35, 0.5 - 0.03 - 0.04)
        left[477] = (0.35, 0.5 - 0.03 + 0.04)

        right[468] = (0.65, 0.5 - 0.03)
        right[469] = (0.65 - 0.04, 0.5 - 0.03)
        right[470] = (0.65 + 0.04, 0.5 - 0.03)
        right[471] = (0.65, 0.5 - 0.03 - 0.04)
        right[472] = (0.65, 0.5 - 0.03 + 0.04)

        landmarks = right + left
        eyes = extract_eyes(landmarks, 1000, 1000)
        result = extract_feature_vector(eyes)

        self.assertTrue(result["valid"])
        left_iris_y = result["features"][1]
        right_iris_y = result["features"][4]

        # Negative offset indicates iris shifted toward upper eyelid
        self.assertLess(left_iris_y, 0)
        self.assertLess(right_iris_y, 0)

    def test_feature_vector_translation_invariance(self):
        """Test that feature vector is approximately invariant to translation."""
        # Original position
        right1 = build_eye("right", center=(0.65, 0.5), width=0.24, height=0.08)
        left1 = build_eye("left", center=(0.35, 0.5), width=0.24, height=0.08)
        landmarks1 = right1 + left1
        eyes1 = extract_eyes(landmarks1, 1000, 1000)
        result1 = extract_feature_vector(eyes1)

        # Translated position (shift entire face right)
        right2 = build_eye("right", center=(0.75, 0.5), width=0.24, height=0.08)
        left2 = build_eye("left", center=(0.45, 0.5), width=0.24, height=0.08)
        landmarks2 = right2 + left2
        eyes2 = extract_eyes(landmarks2, 1000, 1000)
        result2 = extract_feature_vector(eyes2)

        self.assertTrue(result1["valid"])
        self.assertTrue(result2["valid"])

        # Features should be nearly identical after translation
        for i in range(6):
            self.assertAlmostEqual(result1["features"][i], result2["features"][i], places=2)

    def test_feature_vector_scale_invariance(self):
        """Test that feature vector is approximately invariant to uniform scaling."""
        # Original size
        right1 = build_eye("right", center=(0.65, 0.5), width=0.24, height=0.08)
        left1 = build_eye("left", center=(0.35, 0.5), width=0.24, height=0.08)
        landmarks1 = right1 + left1
        eyes1 = extract_eyes(landmarks1, 1000, 1000)
        result1 = extract_feature_vector(eyes1)

        # Larger size (wider eyes)
        right2 = build_eye("right", center=(0.65, 0.5), width=0.30, height=0.10)
        left2 = build_eye("left", center=(0.35, 0.5), width=0.30, height=0.10)
        landmarks2 = right2 + left2
        eyes2 = extract_eyes(landmarks2, 1000, 1000)
        result2 = extract_feature_vector(eyes2)

        self.assertTrue(result1["valid"])
        self.assertTrue(result2["valid"])

        # Features should be nearly identical despite different eye sizes
        for i in range(6):
            self.assertAlmostEqual(result1["features"][i], result2["features"][i], places=2)

    def test_feature_vector_missing_eye(self):
        """Test that feature vector is invalid when one eye is missing."""
        right = build_eye("right", center=(0.65, 0.5), width=0.24, height=0.08)
        left = build_eye("left", center=(0.35, 0.5), width=0.24, height=0.08)

        # Collapse left eye corners to make it invalid
        left[263] = (0.35, 0.5)
        left[362] = (0.35, 0.5)

        landmarks = right + left
        eyes = extract_eyes(landmarks, 1000, 1000)

        result = extract_feature_vector(eyes)

        self.assertFalse(result["valid"])
        self.assertEqual(result["reason"], "Both eyes required")
        self.assertIsNone(result["features"])

    def test_feature_vector_closed_eye(self):
        """Test that feature vector is invalid when an eye is closed."""
        right = build_eye("right", center=(0.65, 0.5), width=0.24, height=0.08)
        left = build_eye("left", center=(0.35, 0.5), width=0.24, height=0.08)

        # Collapse eyelids to simulate closed eye
        left[387] = (0.35, 0.5)
        left[386] = (0.35, 0.5)
        left[385] = (0.35, 0.5)
        left[374] = (0.35, 0.5)
        left[373] = (0.35, 0.5)
        left[380] = (0.35, 0.5)

        landmarks = right + left
        eyes = extract_eyes(landmarks, 1000, 1000)

        result = extract_feature_vector(eyes)

        self.assertFalse(result["valid"])
        self.assertIn(result["reason"], ["Eye closed", "Eye nearly closed"])
        self.assertIsNone(result["features"])

    def test_feature_vector_no_eye_data(self):
        """Test that feature vector is invalid when no eye data is provided."""
        result = extract_feature_vector(None)

        self.assertFalse(result["valid"])
        self.assertEqual(result["reason"], "No eye data")
        self.assertIsNone(result["features"])

    def test_feature_vector_nearly_closed_eye(self):
        """Test that feature vector is invalid when eye openness is below threshold."""
        right = build_eye("right", center=(0.65, 0.5), width=0.24, height=0.03)  # Very small height
        left = build_eye("left", center=(0.35, 0.5), width=0.24, height=0.03)

        landmarks = right + left
        eyes = extract_eyes(landmarks, 1000, 1000)

        result = extract_feature_vector(eyes)

        # Eye openness should be very small (< 0.15 threshold)
        self.assertFalse(result["valid"])
        # Either "Eye closed" (if extract_eyes marks it closed) or "Eye nearly closed" (if our threshold catches it)
        self.assertIn(result["reason"], ["Eye closed", "Eye nearly closed"])
        self.assertIsNone(result["features"])

    def test_feature_vector_per_eye_breakdown(self):
        """Test that per-eye breakdown is included in result."""
        right = build_eye("right", center=(0.65, 0.5), width=0.24, height=0.08)
        left = build_eye("left", center=(0.35, 0.5), width=0.24, height=0.08)
        landmarks = right + left
        eyes = extract_eyes(landmarks, 1000, 1000)

        result = extract_feature_vector(eyes)

        self.assertTrue(result["valid"])
        self.assertIn("left", result)
        self.assertIn("right", result)

        # Check that per-eye fields match the vector
        self.assertEqual(result["left"]["iris_x"], result["features"][0])
        self.assertEqual(result["left"]["iris_y"], result["features"][1])
        self.assertEqual(result["left"]["eye_open"], result["features"][2])
        self.assertEqual(result["right"]["iris_x"], result["features"][3])
        self.assertEqual(result["right"]["iris_y"], result["features"][4])
        self.assertEqual(result["right"]["eye_open"], result["features"][5])


if __name__ == "__main__":
    unittest.main()
