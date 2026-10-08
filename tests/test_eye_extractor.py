import unittest

import numpy as np

from tracker.eye_extractor import extract_eyes, extract_feature_vector


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
