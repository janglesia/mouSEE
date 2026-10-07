import unittest

import numpy as np

from tracker.eye_extractor import extract_eyes


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


if __name__ == "__main__":
    unittest.main()
