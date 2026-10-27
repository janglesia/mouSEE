"""
Unit tests for ML gaze estimation models.

All tests use synthetic data to avoid requiring actual recordings.
"""

import unittest
import tempfile
import json
from pathlib import Path

import numpy as np

from tracker.ml_models.random_forest_gaze import RandomForestGazeModel
from tracker.ml_models.gradient_boosting_gaze import GradientBoostingGazeModel
from tracker.ml_models.ridge_gaze import RidgeGazeModel
from tracker.ml_models.data_loader import (
    load_accuracy_test_recording,
    extract_features,
    extract_targets,
    load_training_data
)
from tracker.ml_models.evaluator import evaluate_model, compare_models, print_comparison_report


def generate_synthetic_features(n_samples=100):
    """Generate synthetic feature data for testing."""
    np.random.seed(42)
    X = np.random.randn(n_samples, 6) * 0.3
    # Ensure openness values are in reasonable range [0, 1]
    X[:, 2] = np.clip(X[:, 2] + 0.5, 0, 1)  # left_openness
    X[:, 5] = np.clip(X[:, 5] + 0.5, 0, 1)  # right_openness
    return X


def generate_synthetic_targets(n_samples=100):
    """Generate synthetic target data for testing."""
    np.random.seed(42)
    y = np.random.rand(n_samples, 2)
    return y


def generate_synthetic_recording(n_targets=10, n_frames_per_target=20):
    """Generate a synthetic accuracy test recording for testing."""
    np.random.seed(42)

    targets = []
    for i in range(n_targets):
        frames = []
        for j in range(n_frames_per_target):
            frame = {
                'ms': j * 16,
                'faceFound': True,
                'yaw': np.random.randn() * 0.1,
                'pitch': np.random.randn() * 0.1,
                'left': {
                    'offsetH': np.random.randn() * 0.3,
                    'offsetV': np.random.randn() * 0.3,
                    'openness': np.clip(np.random.randn() * 0.2 + 0.5, 0, 1),
                    'closed': False,
                    'covered': False,
                },
                'right': {
                    'offsetH': np.random.randn() * 0.3,
                    'offsetV': np.random.randn() * 0.3,
                    'openness': np.clip(np.random.randn() * 0.2 + 0.5, 0, 1),
                    'closed': False,
                    'covered': False,
                },
            }
            frames.append(frame)

        target = {
            'index': i,
            'x': np.random.rand(),
            'y': np.random.rand(),
            'phase': 'Sit normally' if i < 5 else 'Lean left',
            'frames': frames,
        }
        targets.append(target)

    report = {
        'madeAt': '2024-01-01T00:00:00Z',
        'calibrationId': 'test-calibration',
        'calibrationName': 'Test Calibration',
        'screenWidth': 1920,
        'screenHeight': 1080,
        'offsetX': 0.0,
        'offsetY': 0.0,
        'headMode': 'EyesOnly',
        'points': [],
        'meanErrorX': {},
        'biasX': {},
        'meanErrorY': {},
        'summary': [],
    }

    return {
        'report': report,
        'frames': targets,
    }


class DataLoaderTest(unittest.TestCase):
    def test_load_accuracy_test_recording(self):
        """Test loading a synthetic accuracy test recording."""
        recording = generate_synthetic_recording()

        with tempfile.NamedTemporaryFile(mode='w', suffix='.json', delete=False) as f:
            json.dump(recording, f)
            temp_path = f.name

        try:
            data = load_accuracy_test_recording(temp_path)
            self.assertIn('report', data)
            self.assertIn('frames', data)
            self.assertEqual(len(data['frames']), 10)
        finally:
            Path(temp_path).unlink()

    def test_load_accuracy_test_recording_not_found(self):
        """Test error handling when file doesn't exist."""
        with self.assertRaises(FileNotFoundError):
            load_accuracy_test_recording('nonexistent.json')

    def test_extract_features(self):
        """Test feature extraction from frames."""
        recording = generate_synthetic_recording()
        frames = recording['frames'][0]['frames']

        X, metadata = extract_features(frames)

        self.assertEqual(X.shape[1], 6)
        self.assertEqual(len(X), len(frames))
        self.assertEqual(len(metadata), len(frames))
        self.assertTrue(all('ms' in m for m in metadata))

    def test_extract_features_skips_closed_eyes(self):
        """Test that frames with closed eyes are skipped."""
        frames = [
            {
                'ms': 0,
                'left': {'offsetH': 0.1, 'offsetV': 0.2, 'openness': 0.5, 'closed': False, 'covered': False},
                'right': {'offsetH': 0.1, 'offsetV': 0.2, 'openness': 0.5, 'closed': False, 'covered': False},
            },
            {
                'ms': 16,
                'left': {'offsetH': 0.1, 'offsetV': 0.2, 'openness': 0.5, 'closed': True, 'covered': False},
                'right': {'offsetH': 0.1, 'offsetV': 0.2, 'openness': 0.5, 'closed': False, 'covered': False},
            },
        ]

        X, metadata = extract_features(frames)

        self.assertEqual(len(X), 1)  # Only one valid frame

    def test_extract_targets(self):
        """Test target extraction from target recordings."""
        recording = generate_synthetic_recording()
        targets = recording['frames']

        y, phases = extract_targets(targets)

        self.assertEqual(y.shape[1], 2)
        self.assertEqual(len(y), len(targets) * 20)  # 10 targets * 20 frames
        self.assertEqual(len(phases), len(y))
        self.assertIn('Sit normally', phases)
        self.assertIn('Lean left', phases)

    def test_load_training_data(self):
        """Test end-to-end data loading."""
        recording = generate_synthetic_recording()

        with tempfile.NamedTemporaryFile(mode='w', suffix='.json', delete=False) as f:
            json.dump(recording, f)
            temp_path = f.name

        try:
            X, y, metadata = load_training_data(temp_path)

            self.assertEqual(X.shape[1], 6)
            self.assertEqual(y.shape[1], 2)
            self.assertEqual(len(X), len(y))
            self.assertEqual(len(metadata), len(X))
        finally:
            Path(temp_path).unlink()


class RandomForestModelTest(unittest.TestCase):
    def test_training_and_prediction(self):
        """Test Random Forest model training and prediction."""
        model = RandomForestGazeModel()
        X = generate_synthetic_features(n_samples=100)
        y = generate_synthetic_targets(n_samples=100)

        model.train(X, y)
        self.assertTrue(model.is_trained)

        predictions = model.predict(X)
        self.assertEqual(predictions.shape, y.shape)

    def test_save_and_load(self):
        """Test saving and loading Random Forest model."""
        model = RandomForestGazeModel()
        X = generate_synthetic_features(n_samples=100)
        y = generate_synthetic_targets(n_samples=100)

        model.train(X, y)

        with tempfile.NamedTemporaryFile(suffix='.pkl', delete=False) as f:
            temp_path = f.name

        try:
            model.save(temp_path)

            loaded_model = RandomForestGazeModel()
            loaded_model.load(temp_path)

            self.assertTrue(loaded_model.is_trained)

            original_pred = model.predict(X)
            loaded_pred = loaded_model.predict(X)

            np.testing.assert_array_almost_equal(original_pred, loaded_pred)
        finally:
            Path(temp_path).unlink()

    def test_predict_before_training_raises_error(self):
        """Test that prediction before training raises an error."""
        model = RandomForestGazeModel()
        X = generate_synthetic_features(n_samples=10)

        with self.assertRaises(RuntimeError):
            model.predict(X)

    def test_save_before_training_raises_error(self):
        """Test that saving before training raises an error."""
        model = RandomForestGazeModel()

        with tempfile.NamedTemporaryFile(suffix='.pkl', delete=False) as f:
            temp_path = f.name

        try:
            with self.assertRaises(RuntimeError):
                model.save(temp_path)
        finally:
            Path(temp_path).unlink()


class GradientBoostingModelTest(unittest.TestCase):
    def test_training_and_prediction(self):
        """Test Gradient Boosting model training and prediction."""
        model = GradientBoostingGazeModel()
        X = generate_synthetic_features(n_samples=100)
        y = generate_synthetic_targets(n_samples=100)

        model.train(X, y)
        self.assertTrue(model.is_trained)

        predictions = model.predict(X)
        self.assertEqual(predictions.shape, y.shape)

    def test_save_and_load(self):
        """Test saving and loading Gradient Boosting model."""
        model = GradientBoostingGazeModel()
        X = generate_synthetic_features(n_samples=100)
        y = generate_synthetic_targets(n_samples=100)

        model.train(X, y)

        with tempfile.NamedTemporaryFile(suffix='.pkl', delete=False) as f:
            temp_path = f.name

        try:
            model.save(temp_path)

            loaded_model = GradientBoostingGazeModel()
            loaded_model.load(temp_path)

            self.assertTrue(loaded_model.is_trained)

            original_pred = model.predict(X)
            loaded_pred = loaded_model.predict(X)

            np.testing.assert_array_almost_equal(original_pred, loaded_pred)
        finally:
            Path(temp_path).unlink()

    def test_predict_before_training_raises_error(self):
        """Test that prediction before training raises an error."""
        model = GradientBoostingGazeModel()
        X = generate_synthetic_features(n_samples=10)

        with self.assertRaises(RuntimeError):
            model.predict(X)


class RidgeModelTest(unittest.TestCase):
    def test_training_and_prediction(self):
        """Test Ridge model training and prediction."""
        model = RidgeGazeModel()
        X = generate_synthetic_features(n_samples=100)
        y = generate_synthetic_targets(n_samples=100)

        model.train(X, y)
        self.assertTrue(model.is_trained)

        predictions = model.predict(X)
        self.assertEqual(predictions.shape, y.shape)

    def test_save_and_load(self):
        """Test saving and loading Ridge model."""
        model = RidgeGazeModel()
        X = generate_synthetic_features(n_samples=100)
        y = generate_synthetic_targets(n_samples=100)

        model.train(X, y)

        with tempfile.NamedTemporaryFile(suffix='.pkl', delete=False) as f:
            temp_path = f.name

        try:
            model.save(temp_path)

            loaded_model = RidgeGazeModel()
            loaded_model.load(temp_path)

            self.assertTrue(loaded_model.is_trained)

            original_pred = model.predict(X)
            loaded_pred = loaded_model.predict(X)

            np.testing.assert_array_almost_equal(original_pred, loaded_pred)
        finally:
            Path(temp_path).unlink()

    def test_predict_before_training_raises_error(self):
        """Test that prediction before training raises an error."""
        model = RidgeGazeModel()
        X = generate_synthetic_features(n_samples=10)

        with self.assertRaises(RuntimeError):
            model.predict(X)


class EvaluatorTest(unittest.TestCase):
    def test_evaluate_model(self):
        """Test model evaluation metrics."""
        model = RandomForestGazeModel()
        X = generate_synthetic_features(n_samples=100)
        y = generate_synthetic_targets(n_samples=100)

        model.train(X, y)

        metrics = evaluate_model(model, X, y)

        self.assertIn('mae_x', metrics)
        self.assertIn('mae_y', metrics)
        self.assertIn('rmse_x', metrics)
        self.assertIn('rmse_y', metrics)
        self.assertIn('euclidean_mae', metrics)
        self.assertIn('bias_x', metrics)
        self.assertIn('bias_y', metrics)

        self.assertGreater(metrics['mae_x'], 0)
        self.assertGreater(metrics['mae_y'], 0)
        self.assertGreater(metrics['euclidean_mae'], 0)

    def test_compare_models(self):
        """Test comparing multiple models."""
        X = generate_synthetic_features(n_samples=100)
        y = generate_synthetic_targets(n_samples=100)

        model1 = RandomForestGazeModel()
        model2 = GradientBoostingGazeModel()

        model1.train(X, y)
        model2.train(X, y)

        models_dict = {
            'RandomForest': model1,
            'GradientBoosting': model2,
        }

        results = compare_models(models_dict, X, y)

        self.assertEqual(len(results), 2)
        self.assertIn('RandomForest', results)
        self.assertIn('GradientBoosting', results)

        for metrics in results.values():
            self.assertIn('mae_x', metrics)
            self.assertIn('mae_y', metrics)


class EndToEndTest(unittest.TestCase):
    def test_end_to_end_pipeline(self):
        """Test the complete pipeline with synthetic data."""
        # Generate synthetic recording
        recording = generate_synthetic_recording(n_targets=5, n_frames_per_target=30)

        with tempfile.NamedTemporaryFile(mode='w', suffix='.json', delete=False) as f:
            json.dump(recording, f)
            temp_path = f.name

        try:
            # Load data
            X, y, metadata = load_training_data(temp_path)
            self.assertGreater(len(X), 0)

            # Split data
            split_idx = int(0.8 * len(X))
            X_train, X_test = X[:split_idx], X[split_idx:]
            y_train, y_test = y[:split_idx], y[split_idx:]

            # Train models
            models = {
                'RandomForest': RandomForestGazeModel(),
                'GradientBoosting': GradientBoostingGazeModel(),
                'Ridge': RidgeGazeModel(),
            }

            for name, model in models.items():
                model.train(X_train, y_train)
                self.assertTrue(model.is_trained)

            # Evaluate models
            results = compare_models(models, X_test, y_test)
            self.assertEqual(len(results), 3)

            # Verify all metrics are present
            for metrics in results.values():
                self.assertIn('mae_x', metrics)
                self.assertIn('mae_y', metrics)
                self.assertIn('euclidean_mae', metrics)

        finally:
            Path(temp_path).unlink()


if __name__ == '__main__':
    unittest.main()
