import json
import sys
import tempfile
import unittest
from pathlib import Path

# face_tracker.py imports its neighbours as top-level modules (it's run from tracker/), so do the same here
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "tracker"))
import cv2  # noqa: E402
import tracker.face_tracker as face_tracker  # noqa: E402
from tracker.face_tracker import (list_cameras, apply_eye_thresholds, load_calibration, EYE_THRESHOLDS,  # noqa: E402
                                  eye_record_row, EYE_RECORD_FIELDS)


class FakeCamera:
    """Stands in for cv2.VideoCapture. opens: isOpened(), gives_frame: read() succeeds."""
    def __init__(self, opens=True, gives_frame=True, width=1280, height=720):
        self.opens, self.gives_frame = opens, gives_frame
        self.size = {cv2.CAP_PROP_FRAME_WIDTH: width, cv2.CAP_PROP_FRAME_HEIGHT: height}
        self.released = False

    def isOpened(self):
        return self.opens

    def read(self):
        return self.gives_frame, None

    def get(self, prop):
        return float(self.size[prop])

    def release(self):
        self.released = True


def opener(cameras):
    """open_camera for list_cameras: index -> FakeCamera, anything not in the dict doesn't open."""
    opened = []

    def open_camera(index):
        cam = cameras.get(index, FakeCamera(opens=False))
        opened.append((index, cam))
        return cam

    return open_camera, opened


class ListCamerasTest(unittest.TestCase):
    def test_lists_each_camera_with_its_size(self):
        open_camera, _ = opener({0: FakeCamera(width=1920, height=1080), 1: FakeCamera(width=640, height=480)})
        self.assertEqual(list_cameras(open_camera=open_camera), [
            {"index": 0, "width": 1920, "height": 1080},
            {"index": 1, "width": 640, "height": 480},
        ])

    def test_no_cameras(self):
        open_camera, _ = opener({})
        self.assertEqual(list_cameras(open_camera=open_camera), [])

    def test_stops_after_misses_in_a_row(self):
        open_camera, opened = opener({0: FakeCamera()})
        list_cameras(max_misses=2, open_camera=open_camera)
        self.assertEqual([i for i, _ in opened], [0, 1, 2])

    def test_one_gap_is_skipped(self):
        # a camera unplugged from the middle leaves a gap, the next one should still be found
        open_camera, _ = opener({0: FakeCamera(), 2: FakeCamera()})
        self.assertEqual([c["index"] for c in list_cameras(max_misses=2, open_camera=open_camera)], [0, 2])

    def test_opens_but_no_frame_is_not_a_camera(self):
        open_camera, _ = opener({0: FakeCamera(gives_frame=False)})
        self.assertEqual(list_cameras(open_camera=open_camera), [])

    def test_stops_at_max_cameras(self):
        open_camera, opened = opener({i: FakeCamera() for i in range(20)})
        self.assertEqual(len(list_cameras(max_cameras=3, open_camera=open_camera)), 3)
        self.assertEqual(len(opened), 3)

    def test_every_camera_is_released(self):
        open_camera, opened = opener({0: FakeCamera(), 1: FakeCamera(gives_frame=False)})
        list_cameras(open_camera=open_camera)
        self.assertTrue(all(cam.released for _, cam in opened))


class EyeThresholdsTest(unittest.TestCase):
    """apply_eye_thresholds / load_calibration: the eye check's values from a saved calibration"""

    def setUp(self):
        # these change module constants, put them back after each test
        for name, (module, _, _) in EYE_THRESHOLDS.items():
            self.addCleanup(setattr, module, name, getattr(module, name))

    def test_sets_the_values_in_their_modules(self):
        applied = apply_eye_thresholds({"IRIS_HIDDEN_RATIO": 0.7, "COVERED_CONTRAST": 0.1, "REST_PITCH": -9.5,
                                        "REST_YAW": 2, "MAX_PITCH": 32, "MAX_YAW": 40})
        self.assertEqual(len(applied), 6)
        self.assertEqual(face_tracker.eye_extractor_module.IRIS_HIDDEN_RATIO, 0.7)
        self.assertEqual(face_tracker.eye_extractor_module.COVERED_CONTRAST, 0.1)
        self.assertEqual(face_tracker.tracking_status_module.REST_PITCH, -9.5)
        self.assertEqual(face_tracker.tracking_status_module.MAX_YAW, 40.0)

    def test_unknown_names_and_bad_values_are_skipped(self):
        before = face_tracker.eye_extractor_module.IRIS_HIDDEN_RATIO
        applied = apply_eye_thresholds({"IRIS_HIDDEN_RATIO": "high", "MAX_EYE_WIDTH_JUMP": 9, "COVERED_CONTRAST": True,
                                        "MAX_PITCH": float("nan")})
        self.assertEqual(applied, {})
        self.assertEqual(face_tracker.eye_extractor_module.IRIS_HIDDEN_RATIO, before)

    def test_out_of_range_is_skipped(self):
        self.assertEqual(apply_eye_thresholds({"IRIS_HIDDEN_RATIO": 5.0, "MAX_YAW": 2, "REST_PITCH": -90}), {})

    def test_not_a_dict(self):
        self.assertEqual(apply_eye_thresholds(None), {})
        self.assertEqual(apply_eye_thresholds([1, 2]), {})

    def write(self, folder, data):
        path = Path(folder) / "cal.json"
        path.write_text(json.dumps(data) if not isinstance(data, str) else data, encoding="utf-8")
        return path

    def test_load_calibration_reads_gaze_and_eyes(self):
        with tempfile.TemporaryDirectory() as folder:
            path = self.write(folder, {"version": 1, "gaze": {"k_h": 0.6}, "eyes": {"REST_PITCH": -8}})
            params, eyes = load_calibration(path)
        self.assertEqual(params.k_h, 0.6)
        self.assertEqual(eyes, {"REST_PITCH": -8.0})
        self.assertEqual(face_tracker.tracking_status_module.REST_PITCH, -8.0)

    def test_load_calibration_without_eyes(self):
        with tempfile.TemporaryDirectory() as folder:
            _, eyes = load_calibration(self.write(folder, {"version": 1, "gaze": {}}))
        self.assertEqual(eyes, {})

    def test_load_calibration_broken_file_raises(self):
        # main() catches these and keeps the defaults
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(ValueError):
                load_calibration(self.write(folder, "{ not json"))
            with self.assertRaises(ValueError):
                load_calibration(self.write(folder, "[1, 2]"))

    def test_applied_values_change_what_the_tracker_decides(self):
        # REST_PITCH really is read by tracking_status() every frame
        status = face_tracker.tracking_status_module.tracking_status
        face = {"box": [0.3, 0.2, 0.4, 0.6], "pose": {"yaw": 0, "pitch": -35, "roll": 0}, "eyes": None}
        self.assertEqual(status(face)["reason"], "head_turned")
        apply_eye_thresholds({"REST_PITCH": -10})
        self.assertEqual(status(face)["reason"], "eyes_not_found")  # past the head check now


class EyeRecordTest(unittest.TestCase):
    """eye_record_row, the --record-eyes CSV"""

    def test_row_has_every_column_for_both_eyes(self):
        eye = {"iris_ratio": 0.7, "contrast": 0.4, "blink": 0.05, "openness": 0.33, "width_px": 61.0,
               "closed": False, "covered": False, "offset": {"h": 0, "v": 0}}
        face = {"pose": {"yaw": 2.0, "pitch": -12.0}, "eyes": {"left": eye, "right": dict(eye, closed=True)}}

        row = eye_record_row(1500, face, {"state": "ok", "reason": None})

        self.assertEqual(set(row), set(EYE_RECORD_FIELDS))
        self.assertEqual(row["left_iris_ratio"], 0.7)
        self.assertTrue(row["right_closed"])
        self.assertEqual(row["pitch"], -12.0)

    def test_no_face_and_missing_eye_leave_cells_empty(self):
        row = eye_record_row(10, None, {"state": "lost", "reason": "no_face"})
        self.assertFalse(row["face"])
        self.assertNotIn("left_openness", row)  # DictWriter writes "" for missing keys

        face = {"pose": {"yaw": 0, "pitch": 0}, "eyes": {"left": {"openness": 0.3}}}
        row = eye_record_row(10, face, {"state": "ok", "reason": None})
        self.assertEqual(row["left_openness"], 0.3)
        self.assertNotIn("right_openness", row)


class HeadInputsTest(unittest.TestCase):
    def test_middle_of_the_face_and_pitch(self):
        self.assertEqual(face_tracker.head_inputs([0.4, 0.3, 0.2, 0.25], {"pitch": -11.5, "yaw": 2.0}),
                         {"face_x": 0.5, "face_pitch": -11.5})

    def test_nothing_without_head_compensation(self):
        self.assertEqual(face_tracker.head_inputs([0.4, 0.3, 0.2, 0.25], {"pitch": -11.5}, cancel_head=False), {})


if __name__ == "__main__":
    unittest.main()
