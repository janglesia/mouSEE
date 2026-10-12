import unittest

from tracker.tracking_status import (tracking_status, EDGE_MARGIN, MAX_YAW, MAX_PITCH,
                                     MIN_EYE_WIDTH_PX)


# a face dict shaped like the one face_tracker.py builds, with only the fields tracking_status reads
def make_face(box=(0.3, 0.2, 0.4, 0.6), yaw=0.0, pitch=0.0, width_px=60.0,
              left=True, right=True, left_closed=False, right_closed=False,
              left_covered=False, right_covered=False):
    eyes = {}
    if left:
        eyes["left"] = {"width_px": width_px, "closed": left_closed, "covered": left_covered}
    if right:
        eyes["right"] = {"width_px": width_px, "closed": right_closed, "covered": right_covered}
    if eyes:
        eyes["gaze"] = {"eyes_used": sum(not eyes[n]["closed"] and not eyes[n]["covered"]
                                         for n in ("left", "right") if n in eyes)}
    return {"box": list(box), "pose": {"yaw": yaw, "pitch": pitch, "roll": 0.0}, "eyes": eyes or None}


class TrackingStatusTest(unittest.TestCase):
    def assertLost(self, status, reason):
        self.assertEqual(status, {"state": "lost", "reason": reason})

    def test_normal_face_is_ok(self):
        self.assertEqual(tracking_status(make_face()), {"state": "ok", "reason": None})

    def test_no_face(self):
        self.assertLost(tracking_status(None), "no_face")

    def test_face_cut_off_at_each_edge(self):
        for box in [(0.0, 0.2, 0.4, 0.6),     # left
                    (0.6, 0.2, 0.4, 0.6),     # right (x + w = 1.0)
                    (0.3, 0.0, 0.4, 0.6),     # top
                    (0.3, 0.4, 0.4, 0.6)]:    # bottom (y + h = 1.0)
            with self.subTest(box=box):
                self.assertLost(tracking_status(make_face(box=box)), "face_partial")

    def test_face_just_inside_the_margin_is_ok(self):
        m = EDGE_MARGIN + 0.001
        box = (m, m, 1 - 2 * m, 1 - 2 * m)
        self.assertEqual(tracking_status(make_face(box=box))["state"], "ok")

    def test_head_turned_either_way(self):
        for yaw, pitch in [(MAX_YAW + 1, 0), (-MAX_YAW - 1, 0), (0, MAX_PITCH + 1), (0, -MAX_PITCH - 1)]:
            with self.subTest(yaw=yaw, pitch=pitch):
                self.assertLost(tracking_status(make_face(yaw=yaw, pitch=pitch)), "head_turned")

    # resting pitch is often around -10 with the camera on top of the screen, that has to be ok
    def test_normal_head_angles_are_ok(self):
        for yaw, pitch in [(MAX_YAW - 1, 0), (-20, -10), (0, -MAX_PITCH + 1)]:
            with self.subTest(yaw=yaw, pitch=pitch):
                self.assertEqual(tracking_status(make_face(yaw=yaw, pitch=pitch))["state"], "ok")

    # extract_eyes returned None, or the validator dropped both eyes
    def test_face_without_usable_eyes(self):
        self.assertLost(tracking_status(make_face(left=False, right=False)), "eyes_not_found")

    def test_too_far_away(self):
        self.assertLost(tracking_status(make_face(width_px=MIN_EYE_WIDTH_PX - 1)), "too_far")

    def test_both_eyes_closed_is_not_lost(self):
        status = tracking_status(make_face(left_closed=True, right_closed=True))
        self.assertEqual(status, {"state": "eyes_closed", "reason": None})

    # gaze still comes from the open eye
    def test_wink_is_ok(self):
        self.assertEqual(tracking_status(make_face(left_closed=True))["state"], "ok")

    # the validator dropped one eye as a glitch, the other one is enough
    def test_one_eye_dropped_is_ok(self):
        self.assertEqual(tracking_status(make_face(right=False))["state"], "ok")

    # gaze comes from the other eye
    def test_one_eye_covered_is_ok(self):
        self.assertEqual(tracking_status(make_face(left_covered=True))["state"], "ok")

    def test_both_eyes_covered_is_lost(self):
        self.assertLost(tracking_status(make_face(left_covered=True, right_covered=True)), "eyes_covered")

    # nothing usable left, and it isn't just a blink
    def test_one_covered_one_closed_is_lost(self):
        self.assertLost(tracking_status(make_face(left_covered=True, right_closed=True)), "eyes_covered")

    # only the first problem is reported, in the order the docstring lists them
    def test_first_problem_wins(self):
        face = make_face(box=(0.0, 0.2, 0.4, 0.6), yaw=60, width_px=5, left_closed=True, right_closed=True)
        self.assertLost(tracking_status(face), "face_partial")

        face = make_face(yaw=60, width_px=5, left_closed=True, right_closed=True)
        self.assertLost(tracking_status(face), "head_turned")

        face = make_face(width_px=5, left_closed=True, right_closed=True)
        self.assertLost(tracking_status(face), "too_far")


if __name__ == "__main__":
    unittest.main()
