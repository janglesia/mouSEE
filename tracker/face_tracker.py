"""
Face landmark tracker.

Reads the webcam, runs MediaPipe Face Landmarker, and writes one JSON line per
frame to stdout. The C# app starts this process and reads those lines.
Log/debug text goes to stderr so it never corrupts the JSON stream.

Messages:
  {"type":"ready","width":1920,"height":1080}
  {"type":"frame","t":1234,"faces":[{
      "box":[x,y,w,h],                       # normalized 0-1, around all landmarks
      "landmarks":[[x,y,z], ...478],         # normalized 0-1 (z: depth, roughly same scale as x)
      "irises":{"left":[x,y],"right":[x,y]}, # iris centers (landmarks 473 and 468)
      "pose":{"yaw":0,"pitch":0,"roll":0},   # head rotation in degrees, 0 = facing camera
      "blendshapes":{"eyeBlinkLeft":0.02, ...51}}]}   # expression scores 0-1
  {"type":"error","message":"..."}

Coordinates are mirrored (like a selfie view), so moving your head right moves
points right. Left/right in names (irises, blendshapes) mean the person's own
left/right, not the side of the image.

Landmark index map:
https://storage.googleapis.com/mediapipe-assets/documentation/mediapipe_face_landmark_fullsize.png

Run standalone to test:  python face_tracker.py --preview
"""
from eye_extractor import extract_eyes, draw_eyes, GazeSmoother

import argparse
import json
import math
import sys
import threading
import time
from pathlib import Path

import cv2
import mediapipe as mp
import numpy as np
from mediapipe.tasks import python as mp_python
from mediapipe.tasks.python import vision

DEFAULT_MODEL = Path(__file__).with_name("face_landmarker.task")

# Iris center landmarks (only present in the 478-point model)
RIGHT_IRIS = 468
LEFT_IRIS = 473


def emit(obj):
    sys.stdout.write(json.dumps(obj, separators=(",", ":")) + "\n")
    sys.stdout.flush()


def log(msg):
    print(msg, file=sys.stderr, flush=True)


def head_pose(matrix):
    """Yaw/pitch/roll in degrees from MediaPipe's 4x4 face transformation matrix."""
    r = matrix[:3, :3]
    pitch = math.degrees(math.atan2(r[2, 1], r[2, 2]))
    yaw = math.degrees(math.asin(max(-1.0, min(1.0, -r[2, 0]))))
    roll = math.degrees(math.atan2(r[1, 0], r[0, 0]))
    # detection runs on the unmirrored image, so flip yaw/roll to match the mirrored output
    return {"yaw": round(-yaw, 1), "pitch": round(pitch, 1), "roll": round(-roll, 1)}


class Preview(threading.Thread):
    """Debug window, drawn on its own thread.

    Drawing the window (especially over Remote Desktop) is slow enough to drop
    tracking to ~30 fps if done in the main loop. Here it just shows the newest
    frame whenever it's ready and skips the rest.
    """

    def __init__(self):
        super().__init__(daemon=True)
        self.lock = threading.Lock()
        self.latest = None
        self.new_frame = threading.Event()
        self.closed = threading.Event()  # set when the user presses q

    def show(self, frame, faces):
        with self.lock:
            self.latest = (frame, faces)
        self.new_frame.set()

    def run(self):
        # all OpenCV window calls have to stay on this thread
        while not self.closed.is_set():
            if not self.new_frame.wait(0.1):
                continue
            shown_at = time.monotonic()
            self.new_frame.clear()
            with self.lock:
                frame, faces = self.latest
            # half size so a 1080p preview fits on screen. shrink first, drawing on the small image is cheaper
            fh, fw = frame.shape[0] // 2, frame.shape[1] // 2
            view = cv2.resize(cv2.flip(frame, 1), (fw, fh))  # mirror to match the output coordinates
            for f in faces:
                # all 478 dots in one numpy op. a python loop of cv2.circle calls hogs the GIL
                # and slows down the tracking thread
                pts = (np.array(f["landmarks"])[:, :2] * (fw, fh)).astype(int)
                pts = pts.clip((0, 0), (fw - 2, fh - 2))
                for dx, dy in ((0, 0), (1, 0), (0, 1), (1, 1)):  # 2x2 px dots
                    view[pts[:, 1] + dy, pts[:, 0] + dx] = (0, 255, 0)
                for x, y in f["irises"].values():
                    cv2.circle(view, (int(x * fw), int(y * fh)), 4, (0, 0, 255), -1)
                if f.get("eyes"):
                    draw_eyes(view, f["eyes"], frame.shape[1]) 
                p = f["pose"]
                cv2.putText(view, f"yaw {p['yaw']:+.0f}  pitch {p['pitch']:+.0f}  roll {p['roll']:+.0f}",
                            (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 255, 255), 2)
            cv2.imshow("Tracker preview (q to quit)", view)
            if cv2.waitKey(1) & 0xFF == ord("q"):
                self.closed.set()
            # 30 fps is plenty for a preview, leave the rest of the CPU for tracking
            time.sleep(max(0.0, 1 / 30 - (time.monotonic() - shown_at)))
        cv2.destroyAllWindows()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--camera", type=int, default=0,
                        help="which camera to use (default 0)")
    parser.add_argument("--model", default=str(DEFAULT_MODEL),
                        help="path to the model file (default face_landmarker.task next to this script)")
    parser.add_argument("--min-confidence", type=float, default=0.5,
                        help="detection threshold 0-1 (default 0.5)")
    parser.add_argument("--num-faces", type=int, default=1,
                        help="max faces to track (default 1)")
    # if the camera can't do this exact mode it picks the closest one
    parser.add_argument("--width", type=int, default=1920, help="camera width (default 1920)")
    parser.add_argument("--height", type=int, default=1080, help="camera height (default 1080)")
    parser.add_argument("--fps", type=int, default=60, help="camera frame rate (default 60)")
    parser.add_argument("--preview", action="store_true",
                        help="show a debug window with landmarks drawn")
    args = parser.parse_args()

    if not Path(args.model).exists():
        emit({"type": "error", "message": f"Model not found: {args.model}"})
        return 1

    # MSMF opens a lot faster than DSHOW and can do 1080p60. MJPG is needed for high res
    backend = cv2.CAP_MSMF if sys.platform == "win32" else cv2.CAP_ANY
    cap = cv2.VideoCapture(args.camera, backend)
    cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*"MJPG"))
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, args.width)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, args.height)
    cap.set(cv2.CAP_PROP_FPS, args.fps)
    if not cap.isOpened():
        emit({"type": "error", "message": f"Could not open camera {args.camera}"})
        return 1

    landmarker = vision.FaceLandmarker.create_from_options(vision.FaceLandmarkerOptions(
        base_options=mp_python.BaseOptions(model_asset_path=args.model),
        running_mode=vision.RunningMode.VIDEO,
        num_faces=args.num_faces,
        min_face_detection_confidence=args.min_confidence,
        min_face_presence_confidence=args.min_confidence,
        output_face_blendshapes=True,
        output_facial_transformation_matrixes=True,
    ))

    w = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    h = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    emit({"type": "ready", "width": w, "height": h})
    log(f"camera {args.camera} open at {w}x{h}, requested {args.fps} fps")

    start = time.monotonic()
    last_ts = -1

    preview = None
    if args.preview:
        preview = Preview()
        preview.start()

    gaze_smoother = GazeSmoother()
    try:
        while preview is None or not preview.closed.is_set():
            ok, frame = cap.read()
            if not ok:
                emit({"type": "error", "message": "Camera stopped returning frames"})
                break

            # VIDEO mode requires strictly increasing timestamps
            ts = int((time.monotonic() - start) * 1000)
            if ts <= last_ts:
                ts = last_ts + 1
            last_ts = ts

            # detect on the unmirrored frame so left/right eye labels are correct, flip x after
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            result = landmarker.detect_for_video(
                mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), ts)

            if not result.face_landmarks:
                gaze_smoother.reset()
            faces = []
            for i, lms in enumerate(result.face_landmarks):
                pts = [[round(1 - p.x, 4), round(p.y, 4), round(p.z, 4)] for p in lms]
                xs = [p[0] for p in pts]
                ys = [p[1] for p in pts]
                face = {
                    "box": [round(min(xs), 4), round(min(ys), 4),
                            round(max(xs) - min(xs), 4), round(max(ys) - min(ys), 4)],
                    "landmarks": pts,
                    "irises": {"left": pts[LEFT_IRIS][:2], "right": pts[RIGHT_IRIS][:2]},
                    "pose": head_pose(result.facial_transformation_matrixes[i]),
                    "blendshapes": {c.category_name: round(c.score, 3)
                                    for c in result.face_blendshapes[i]
                                    if c.category_name != "_neutral"},
                }
                face["eyes"] = extract_eyes(pts, w, h, face["blendshapes"], gaze_smoother=gaze_smoother) 
                faces.append(face)
            emit({"type": "frame", "t": ts, "faces": faces})

            if preview:
                preview.show(frame, faces)
    except (BrokenPipeError, KeyboardInterrupt):
        pass  # C# app closed or Ctrl+C
    finally:
        if preview:
            preview.closed.set()
            preview.join(timeout=1)
        cap.release()
        landmarker.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
