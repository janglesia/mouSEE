"""
Face landmark tracker.

Reads the webcam, runs MediaPipe Face Landmarker, and writes one JSON line per
frame to stdout. The C# app starts this process and reads those lines.
Log/debug text goes to stderr so it never corrupts the JSON stream.

Messages:
  {"type":"ready","width":1920,"height":1080}
  {"type":"frame","t":1234,
   "tracking":{"state":"ok","reason":null},  # ok / eyes_closed / lost, see tracking_status.py
   "faces":[{
      "box":[x,y,w,h],                       # normalized 0-1, around all landmarks
      "landmarks":[[x,y,z], ...478],         # normalized 0-1 (z: depth, roughly same scale as x)
      "irises":{"left":[x,y],"right":[x,y]}, # iris centers (landmarks 473 and 468)
      "pose":{"yaw":0,"pitch":0,"roll":0},   # head rotation in degrees, 0 = facing camera
      "blendshapes":{"eyeBlinkLeft":0.02, ...51}}]}   # expression scores 0-1
  {"type":"error","message":"..."}
  {"type":"cameras","cameras":[{"index":0,"width":640,"height":480}]}   # only with --list-cameras, then it exits
  {"type":"key","key":"q"}                   # q, p, c or r pressed in the preview window

Coordinates are mirrored (like a selfie view), so moving your head right moves
points right. Left/right in names (irises, blendshapes) mean the person's own
left/right, not the side of the image.

Landmark index map:
https://storage.googleapis.com/mediapipe-assets/documentation/mediapipe_face_landmark_fullsize.png

Run standalone to test:  python face_tracker.py --preview
"""
from eye_extractor import extract_eyes, draw_eyes, GazeSmoother, EyeValidator
from gaze_model import estimate_gaze, params_from_dict, DEFAULT_PARAMS
from tracking_status import tracking_status
# the modules themselves, to set the eye check's values in them (see apply_eye_thresholds)
import eye_extractor as eye_extractor_module
import tracking_status as tracking_status_module

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

#logging shi
import csv
import os
import time

DEFAULT_MODEL = Path(__file__).with_name("face_landmarker.task")

# Iris center landmarks (only present in the 478-point model)
RIGHT_IRIS = 468
LEFT_IRIS = 473

# MSMF opens a lot faster than DSHOW and can do 1080p60. MJPG is needed for high res
CAMERA_BACKEND = cv2.CAP_MSMF if sys.platform == "win32" else cv2.CAP_ANY

# --list-cameras tries camera 0 up to MAX_CAMERAS - 1, but gives up after 2 in a row don't open
MAX_CAMERAS = 10
MAX_MISSES_IN_A_ROW = 2

# the per-person values the eye check saves (the "eyes" part of a calibration file), and where each one lives.
# a value outside lowest-highest is a broken file, not a person, so it's ignored. see docs/calibration/EYE-CALIBRATION.md
EYE_THRESHOLDS = {
    "IRIS_HIDDEN_RATIO": (eye_extractor_module, 0.3, 1.2),
    "COVERED_CONTRAST": (eye_extractor_module, 0.01, 0.5),
    "LIDS_OPEN_OPENNESS": (eye_extractor_module, 0.1, 0.6),
    "LIDS_SHUT_OPENNESS": (eye_extractor_module, 0.03, 0.15),
    "REST_PITCH": (tracking_status_module, -60.0, 60.0),
    "REST_YAW": (tracking_status_module, -60.0, 60.0),
    "MAX_PITCH": (tracking_status_module, 10.0, 80.0),
    "MAX_YAW": (tracking_status_module, 10.0, 80.0),
}


_emit_lock = threading.Lock()  # the preview thread emits key presses, lines must not mix


def emit(obj):
    line = json.dumps(obj, separators=(",", ":")) + "\n"
    with _emit_lock:
        sys.stdout.write(line)
        sys.stdout.flush()


def log(msg):
    print(msg, file=sys.stderr, flush=True)

def log_gaze_diagnostics(gaze, eyes=None, filepath="gaze_diagnostics.csv"):
    """Append gaze diagnostics to a CSV file."""
    if not gaze:
        return

    eyes = eyes or {}

    left_offset = (eyes.get("left") or {}).get("offset", {})
    right_offset = (eyes.get("right") or {}).get("offset", {})
    left = gaze.get("left") or {}
    right = gaze.get("right") or {}

    def get_value(eye, key):
        value = eye.get(key)
        return value if isinstance(value, (int, float)) else None

    file_exists = os.path.exists(filepath)

    with open(filepath, "a", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(
            f,
            fieldnames=[
                "timestamp",
                "valid",
                "reason",
                "eyes_used",
                "left_h_deg",
                "left_v_deg",
                "right_h_deg",
                "right_v_deg",
                "disagree_h_deg",
                "disagree_v_deg",
                "h_deg",
                "v_deg",
                "left_h_offset",
                "left_v_offset",
                "right_h_offset",
                "right_v_offset",
            ],
        )

        if not file_exists:
            writer.writeheader()

        writer.writerow({
            "timestamp": time.time(),
            "valid": gaze.get("valid"),
            "reason": gaze.get("reason"),
            "eyes_used": gaze.get("eyes_used"),
            "left_h_deg": get_value(left, "h_deg"),
            "left_v_deg": get_value(left, "v_deg"),
            "right_h_deg": get_value(right, "h_deg"),
            "right_v_deg": get_value(right, "v_deg"),
            "disagree_h_deg": gaze.get("disagree_h_deg", gaze.get("disagree_h")),
            "disagree_v_deg": gaze.get("disagree_v_deg", gaze.get("disagree_v")),
            "h_deg": gaze.get("h_deg"),
            "v_deg": gaze.get("v_deg"),
            "left_h_offset": left_offset.get("h"),
            "left_v_offset": left_offset.get("v"),
            "right_h_offset": right_offset.get("h"),
            "right_v_offset": right_offset.get("v"),
        })

def apply_eye_thresholds(values):
    """Sets the eye check's values (see EYE_THRESHOLDS) in eye_extractor / tracking_status. They read their
    constants every frame, so this is enough. Unknown names, and values that aren't numbers or are out of range,
    are skipped and keep the default. Returns {name: value} for what was set."""
    applied = {}
    if not isinstance(values, dict):
        return applied
    for name, value in values.items():
        if name not in EYE_THRESHOLDS or isinstance(value, bool) or not isinstance(value, (int, float)):
            continue
        module, lowest, highest = EYE_THRESHOLDS[name]
        if not (math.isfinite(value) and lowest <= value <= highest):
            continue
        setattr(module, name, float(value))
        applied[name] = float(value)
    return applied


def load_calibration(path):
    """Reads a saved calibration file (app/Calibration/Saved/*.json): applies its eye check values and
    returns (gaze params, the eye values that were set). Raises OSError / ValueError if it can't be read."""
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    if not isinstance(data, dict):
        raise ValueError("not a calibration file")
    return params_from_dict(data.get("gaze")), apply_eye_thresholds(data.get("eyes"))


# --record-eyes columns: the frame, then the same measurements for each eye
EYE_RECORD_MEASUREMENTS = ("iris_ratio", "contrast", "blink", "openness", "width_px", "closed", "covered")
EYE_RECORD_FIELDS = (["t_ms", "face", "state", "reason", "yaw", "pitch"]
                     + [f"{side}_{m}" for side in ("left", "right") for m in EYE_RECORD_MEASUREMENTS])


def eye_record_row(t_ms, face, tracking):
    """One --record-eyes row. face: the face dict as sent to the app, or None. Missing eyes give empty cells."""
    row = {"t_ms": t_ms, "face": face is not None, "state": tracking["state"], "reason": tracking["reason"]}
    if face is not None:
        row["yaw"], row["pitch"] = face["pose"]["yaw"], face["pose"]["pitch"]
        eyes = face.get("eyes") or {}
        for side in ("left", "right"):
            eye = eyes.get(side)
            if eye:
                for m in EYE_RECORD_MEASUREMENTS:
                    row[f"{side}_{m}"] = eye.get(m)
    return row


def head_inputs(box, pose, cancel_head=True):
    """Where the face is and how the head is tilted, for the gaze model's head corrections (leaning, tilting), as
    estimate_gaze() keywords. Empty with --no-head-compensation: then head movement isn't cancelled out of the gaze,
    for the app's "Eyes + head nudge, no head correction" mode (docs/bubble/HEAD-MODES.md)."""
    if not cancel_head:
        return {}
    return {"face_x": box[0] + box[2] / 2, "face_pitch": pose["pitch"]}


def list_cameras(max_cameras=MAX_CAMERAS, max_misses=MAX_MISSES_IN_A_ROW, open_camera=None):
    """[{"index", "width", "height"}] for every camera that opens and gives a frame.
    width/height is the camera's default mode, not the biggest it can do.
    a camera another program is using might not open, so it won't be listed.
    open_camera is for the tests, normally it's cv2.VideoCapture"""
    open_camera = open_camera or (lambda i: cv2.VideoCapture(i, CAMERA_BACKEND))
    found = []
    misses = 0
    for index in range(max_cameras):
        cap = open_camera(index)
        try:
            # some drivers "open" an index that has no camera behind it, so ask for a frame too
            ok = cap.isOpened() and cap.read()[0]
            if ok:
                found.append({"index": index,
                              "width": int(cap.get(cv2.CAP_PROP_FRAME_WIDTH)),
                              "height": int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))})
        finally:
            cap.release()
        misses = 0 if ok else misses + 1
        if misses >= max_misses:
            break
    return found


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

    def show(self, frame, faces, tracking):
        with self.lock:
            self.latest = (frame, faces, tracking)
        self.new_frame.set()

    def run(self):
        # all OpenCV window calls have to stay on this thread
        while not self.closed.is_set():
            if not self.new_frame.wait(0.1):
                continue
            shown_at = time.monotonic()
            self.new_frame.clear()
            with self.lock:
                frame, faces, tracking = self.latest
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
            # green when ok, orange for closed eyes, red when lost
            state = tracking["state"]
            label = state.upper() + (f": {tracking['reason']}" if tracking["reason"] else "")
            color = {"ok": (0, 200, 0), "eyes_closed": (0, 165, 255)}.get(state, (0, 0, 255))
            cv2.putText(view, label, (10, fh - 15), cv2.FONT_HERSHEY_SIMPLEX, 0.8, color, 2)
            cv2.imshow("Tracker preview (q quit, p pause, c calibrations, r re-center, f fine-tune)", view)
            # the app quits on q, pauses on p, opens the calibrations on c, re-centers on r and fine-tunes on f.
            # on its own (make tracker) q closes this and the others do nothing
            key = cv2.waitKey(1) & 0xFF
            if key == ord("q"):
                emit({"type": "key", "key": "q"})
                self.closed.set()
            elif key in (ord("p"), ord("c"), ord("r"), ord("f")):
                emit({"type": "key", "key": chr(key)})
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
    parser.add_argument("--calibration", metavar="FILE",
                        help="saved calibration to use (app/Calibration/Saved/*.json), default values without one")
    parser.add_argument("--record-eyes", metavar="FILE",
                        help="also write every frame's eye measurements to this CSV file, for checking detection problems")
    parser.add_argument("--no-head-compensation", action="store_true",
                        help="turn the head correction off, so head movement stays in the gaze (the app's 'no head correction' mode, docs/bubble/HEAD-MODES.md)")
    parser.add_argument("--list-cameras", action="store_true",
                        help="print the cameras that open as one JSON line and exit")
    args = parser.parse_args()

    if args.list_cameras:
        emit({"type": "cameras", "cameras": list_cameras()})
        return 0

    gaze_params = DEFAULT_PARAMS
    if args.calibration:
        try:
            gaze_params, eye_values = load_calibration(args.calibration)
            log(f"using calibration {args.calibration}" + (f", eye check values {eye_values}" if eye_values else ""))
        except (OSError, ValueError) as e:
            # carry on with the defaults, a broken calibration file shouldn't stop tracking
            log(f"can't read calibration {args.calibration}, using default values: {e}")
    if args.no_head_compensation:
        log("not cancelling head movement out of the gaze (--no-head-compensation)")

    if not Path(args.model).exists():
        emit({"type": "error", "message": f"Model not found: {args.model}"})
        return 1

    cap = cv2.VideoCapture(args.camera, CAMERA_BACKEND)
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

    # used for measuring FPS (different from current FPS which counts incoming frame messages)
    capture_window_start = time.monotonic()
    capture_frame_count = 0
    capture_fps = 0.0

    preview = None
    if args.preview:
        preview = Preview()
        preview.start()

    gaze_smoother = GazeSmoother()
    # one per eye, they remember the last good eye to catch one-frame glitches
    eye_validator = {"left": EyeValidator(), "right": EyeValidator()}
    last_log_time = 0.0

    recorder = record_file = None
    if args.record_eyes:
        record_file = open(args.record_eyes, "w", newline="", encoding="utf-8")
        recorder = csv.DictWriter(record_file, fieldnames=EYE_RECORD_FIELDS)
        recorder.writeheader()
        log(f"recording eye measurements to {args.record_eyes}")
    try:
        while preview is None or not preview.closed.is_set():
            ok, frame = cap.read()
            if not ok:
                emit({"type": "error", "message": "Camera stopped returning frames"})
                break

            # calculates capture rate over ~1 sec windows
            capture_frame_count += 1
            capture_elapsed = time.monotonic() - capture_window_start

            if capture_elapsed >= 1.0:
                capture_fps = capture_frame_count / capture_elapsed
                capture_frame_count = 0
                capture_window_start = time.monotonic()


            # VIDEO mode requires strictly increasing timestamps
            ts = int((time.monotonic() - start) * 1000)
            wall = int(time.time() * 1000)  # Unix ms, for latency measurement in the C# app
            if ts <= last_ts:
                ts = last_ts + 1
            last_ts = ts

            # timed detection on the unmirrored frame so left/right eye labels are correct, flip x after
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            landmark_start = time.perf_counter()
            result = landmarker.detect_for_video(
                mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), ts)
            landmark_ms = (time.perf_counter() - landmark_start) * 1000.0

            if not result.face_landmarks:
                gaze_smoother.reset()
                eye_validator["left"].reset()
                eye_validator["right"].reset()
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
                face["eyes"] = extract_eyes(pts, w, h, face["blendshapes"],
                                            gaze_smoother=gaze_smoother, eye_validator=eye_validator,
                                            image=frame)
                if face["eyes"] is None:
                    # Eye extraction failed for this frame. Keep the face and frame; do not crash.
                    face["eye_extraction"] = {
                        "ok": False,
                        "reason": "extract_eyes_returned_none"
                    }
                    face["gaze_estimate"] = estimate_gaze(None, gaze_params)
                else:
                    # Eye data exists, so estimate gaze normally.
                    # with where the face is and how the head is tilted, for the head corrections (leaning, tilting)
                    face["eyes"]["estimate"] = estimate_gaze(
                        face["eyes"], gaze_params,
                        **head_inputs(face["box"], face["pose"], cancel_head=not args.no_head_compensation)
                    )
                    face["eye_extraction"] = {
                        "ok": True,
                        "reason": None
                    }
                now = time.monotonic()

                #if now - last_log_time >= 1.0:
                #    log_gaze_diagnostics(face["eyes"]["estimate"], eyes=face["eyes"]) #for debugging gaze estimation
                #    last_log_time = now
                faces.append(face)
            tracking = tracking_status(faces[0] if faces else None)
            if recorder:
                recorder.writerow(eye_record_row(ts, faces[0] if faces else None, tracking))
            emit({"type": "frame", "t": ts, "wall": wall, "capture_fps": capture_fps, "landmark_ms": landmark_ms,
                  "tracking": tracking, "faces": faces})

            if preview:
                preview.show(frame, faces, tracking)
    except (BrokenPipeError, KeyboardInterrupt):
        pass  # C# app closed or Ctrl+C
    finally:
        if preview:
            preview.closed.set()
            preview.join(timeout=1)
        cap.release()
        landmarker.close()
        if record_file:
            record_file.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
