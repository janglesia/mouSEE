"""
Data loader for accuracy test recordings.

Loads accuracy test JSON files and extracts features and targets for ML training.
"""

import json
from pathlib import Path
from typing import List, Tuple, Dict, Any

import numpy as np


def load_accuracy_test_recording(path: str) -> Dict[str, Any]:
    """
    Load an accuracy test JSON file.

    Args:
        path: Path to the accuracy test JSON file

    Returns:
        Dictionary containing the loaded data with keys:
        - 'report': AccuracyTestReport
        - 'frames': List of TargetRecording

    Raises:
        FileNotFoundError: If the file doesn't exist
        ValueError: If the file format is invalid
    """
    path = Path(path)

    if not path.exists():
        raise FileNotFoundError(f"Accuracy test file not found: {path}")

    with open(path, 'r', encoding='utf-8') as f:
        data = json.load(f)

    # Validate structure
    if not isinstance(data, dict):
        raise ValueError("Invalid file format: expected JSON object")

    if 'report' not in data or 'frames' not in data:
        raise ValueError("Invalid file format: missing 'report' or 'frames' keys")

    return data


def extract_features(frames: List[Dict[str, Any]]) -> Tuple[np.ndarray, List[Dict[str, Any]]]:
    """
    Convert FrameSample objects to feature vectors.

    For each frame, extracts [eye.offsetH, eye.offsetV, eye.openness] for left and right eyes.
    Skips frames where either eye is closed, covered, or missing.

    Args:
        frames: List of FrameSample dictionaries (from JSON)

    Returns:
        Tuple of:
        - X: Feature array of shape (n_samples, 6) with columns:
            [left_offset_h, left_offset_v, left_openness,
             right_offset_h, right_offset_v, right_openness]
        - metadata: List of metadata dictionaries for each valid frame
    """
    features = []
    metadata = []

    for frame in frames:
        # Check if both eyes are present and usable (use lowercase keys)
        left = frame.get('left')
        right = frame.get('right')

        # Skip if either eye is missing
        if left is None or right is None:
            continue

        # Skip if either eye is closed or covered (use lowercase keys)
        if left.get('closed', False) or left.get('covered', False):
            continue
        if right.get('closed', False) or right.get('covered', False):
            continue

        # Extract features (use lowercase keys)
        left_offset_h = left.get('offsetH', 0.0)
        left_offset_v = left.get('offsetV', 0.0)
        left_openness = left.get('openness', 0.0)

        right_offset_h = right.get('offsetH', 0.0)
        right_offset_v = right.get('offsetV', 0.0)
        right_openness = right.get('openness', 0.0)

        # Ensure all values are finite
        feature_vector = [
            left_offset_h, left_offset_v, left_openness,
            right_offset_h, right_offset_v, right_openness
        ]

        if not all(isinstance(v, (int, float)) for v in feature_vector):
            continue

        if not all(np.isfinite(v) for v in feature_vector):
            continue

        features.append(feature_vector)
        metadata.append({
            'ms': frame.get('ms', 0),
            'face_found': frame.get('faceFound', False),
            'yaw': frame.get('yaw', 0.0),
            'pitch': frame.get('pitch', 0.0),
        })

    if len(features) == 0:
        return np.array([]).reshape(0, 6), []

    return np.array(features), metadata


def extract_targets(target_recordings: List[Dict[str, Any]]) -> Tuple[np.ndarray, List[str]]:
    """
    Extract ground truth X/Y coordinates from test points.

    Args:
        target_recordings: List of TargetRecording dictionaries (from JSON)
            Each has: index, x, y, phase, frames (list of FrameSample)

    Returns:
        Tuple of:
        - y: Target array of shape (n_samples, 2) with columns: [x, y]
        - phases: List of phase labels for each sample
    """
    targets = []
    phases = []

    for target in target_recordings:
        x = target.get('x')
        y = target.get('y')
        phase = target.get('phase', 'unknown')

        if x is None or y is None:
            continue

        if not isinstance(x, (int, float)) or not isinstance(y, (int, float)):
            continue

        if not np.isfinite(x) or not np.isfinite(y):
            continue

        # Get number of frames for this target (use lowercase 'frames')
        frames = target.get('frames', [])
        n_frames = len(frames)

        # Add the same target for each frame (will be filtered later by features)
        for _ in range(n_frames):
            targets.append([x, y])
            phases.append(phase)

    if len(targets) == 0:
        return np.array([]).reshape(0, 2), []

    return np.array(targets), phases


def load_training_data(recording_path: str) -> Tuple[np.ndarray, np.ndarray, List[Dict[str, Any]]]:
    """
    Load a complete accuracy test recording and extract features and targets.

    Args:
        recording_path: Path to the accuracy test JSON file

    Returns:
        Tuple of:
        - X: Feature array of shape (n_samples, 6)
        - y: Target array of shape (n_samples, 2)
        - metadata: List of metadata dictionaries for each sample
    """
    data = load_accuracy_test_recording(recording_path)

    target_recordings = data.get('frames', [])

    # Extract targets from all target recordings
    y_all, phases_all = extract_targets(target_recordings)

    # Extract features from all frames across all targets
    frames_all = []
    phases_for_frames = []

    for target in target_recordings:
        frames = target.get('frames', [])
        phase = target.get('phase', 'unknown')
        frames_all.extend(frames)
        phases_for_frames.extend([phase] * len(frames))

    X_all, metadata_all = extract_features(frames_all)

    # Ensure X and y have the same number of samples
    min_samples = min(len(X_all), len(y_all))
    X = X_all[:min_samples]
    y = y_all[:min_samples]
    metadata = metadata_all[:min_samples]

    # Add phase to metadata (use the phases_for_frames which aligns with frames_all)
    for i, meta in enumerate(metadata):
        if i < len(phases_for_frames):
            meta['phase'] = phases_for_frames[i]

    print(f"Loaded {len(X)} samples from {recording_path}")
    print(f"  Skipped {len(frames_all) - len(X)} frames due to closed/covered/missing eyes")

    return X, y, metadata
