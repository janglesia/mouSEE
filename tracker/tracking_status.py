"""
Per-frame tracking status: is this frame good enough to move the cursor with?

Only looks at one frame. Deciding how long a problem has to last before it
counts as "lost" (so a blink or a one-frame glitch doesn't) is up to the app.

Returns {"state": ..., "reason": ...}:
  ok           face and at least one open eye are usable (a wink is still ok)
  eyes_closed  face is fine but both eyes are closed. not lost, could be a blink
  lost         reason says why:
    no_face          no face found
    face_partial     face is cut off at the edge of the image
    head_turned      head turned or tilted too far for the iris to be reliable
    eyes_not_found   face found but no usable eyes (both dropped as glitches)
    too_far          eyes too small in the image to read the iris
    eyes_covered     no open eye left and at least one is covered (hand, hair...)
"""

# starting guesses, adjust once we have more recordings
EDGE_MARGIN = 0.01          # face box this close to the image edge counts as cut off (0-1)
MAX_YAW = 35.0              # degrees, turning left/right
MAX_PITCH = 30.0            # degrees, looking up/down. resting pitch is often -10 with the camera on top of the screen
MIN_EYE_WIDTH_PX = 15.0     # below this the iris is only a few pixels


def _lost(reason):
    return {"state": "lost", "reason": reason}


def tracking_status(face):
    """face: one face dict as sent to the app (box, pose, eyes), or None if no face was found."""
    if face is None:
        return _lost("no_face")

    x, y, w, h = face["box"]
    if x < EDGE_MARGIN or y < EDGE_MARGIN or x + w > 1 - EDGE_MARGIN or y + h > 1 - EDGE_MARGIN:
        return _lost("face_partial")

    pose = face["pose"]
    if abs(pose["yaw"]) > MAX_YAW or abs(pose["pitch"]) > MAX_PITCH:
        return _lost("head_turned")

    eyes = face.get("eyes")
    found = [eyes[name] for name in ("left", "right") if eyes and name in eyes]
    if not found:
        return _lost("eyes_not_found")

    if max(eye["width_px"] for eye in found) < MIN_EYE_WIDTH_PX:
        return _lost("too_far")

    if eyes["gaze"]["eyes_used"] == 0:
        # one covered eye is fine (gaze uses the other one), but not if nothing usable is left
        if any(eye.get("covered") for eye in found):
            return _lost("eyes_covered")
        return {"state": "eyes_closed", "reason": None}

    return {"state": "ok", "reason": None}
