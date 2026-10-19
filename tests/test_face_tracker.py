import sys
import unittest
from pathlib import Path

# face_tracker.py imports its neighbours as top-level modules (it's run from tracker/), so do the same here
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "tracker"))
import cv2  # noqa: E402
from tracker.face_tracker import list_cameras  # noqa: E402


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


if __name__ == "__main__":
    unittest.main()
