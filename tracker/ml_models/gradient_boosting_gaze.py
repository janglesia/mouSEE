"""
Gradient Boosting Regressor for gaze estimation.

Trains separate models for X and Y coordinates using eye offset and openness features.
"""

import pickle

import numpy as np
from sklearn.ensemble import GradientBoostingRegressor


class GradientBoostingGazeModel:
    """Gradient Boosting model for gaze estimation."""

    def __init__(self, n_estimators=200, learning_rate=0.1, max_depth=5, random_state=42):
        """
        Initialize the Gradient Boosting gaze model.

        Args:
            n_estimators: Number of boosting stages
            learning_rate: Shrinkage factor for each tree
            max_depth: Maximum depth of each tree
            random_state: Random seed for reproducibility
        """
        self.model_x = GradientBoostingRegressor(
            n_estimators=n_estimators,
            learning_rate=learning_rate,
            max_depth=max_depth,
            random_state=random_state
        )
        self.model_y = GradientBoostingRegressor(
            n_estimators=n_estimators,
            learning_rate=learning_rate,
            max_depth=max_depth,
            random_state=random_state
        )
        self.is_trained = False

    def train(self, X, y):
        """
        Train the model on feature data.

        Args:
            X: Feature array of shape (n_samples, 6) with columns:
               [left_offset_h, left_offset_v, left_openness,
                right_offset_h, right_offset_v, right_openness]
            y: Target array of shape (n_samples, 2) with columns: [x, y]
        """
        if X.shape[1] != 6:
            raise ValueError(f"Expected 6 features, got {X.shape[1]}")

        if y.shape[1] != 2:
            raise ValueError(f"Expected 2 targets (x, y), got {y.shape[1]}")

        print("Training Gradient Boosting model...")
        print(f"  Samples: {X.shape[0]}")
        print(f"  Training X coordinate model...")
        self.model_x.fit(X, y[:, 0])
        print(f"  Training Y coordinate model...")
        self.model_y.fit(X, y[:, 1])
        self.is_trained = True
        print("Training complete.")

    def predict(self, X):
        """
        Predict gaze coordinates from features.

        Args:
            X: Feature array of shape (n_samples, 6)

        Returns:
            Predictions array of shape (n_samples, 2) with columns: [x, y]
        """
        if not self.is_trained:
            raise RuntimeError("Model must be trained before prediction")

        if X.shape[1] != 6:
            raise ValueError(f"Expected 6 features, got {X.shape[1]}")

        x_pred = self.model_x.predict(X)
        y_pred = self.model_y.predict(X)

        return np.column_stack((x_pred, y_pred))

    def save(self, path):
        """
        Save the trained model to a file.

        Args:
            path: Path to save the model (will use pickle)
        """
        if not self.is_trained:
            raise RuntimeError("Cannot save untrained model")

        with open(path, 'wb') as f:
            pickle.dump({
                'model_x': self.model_x,
                'model_y': self.model_y,
                'is_trained': self.is_trained
            }, f)
        print(f"Model saved to {path}")

    def load(self, path):
        """
        Load a trained model from a file.

        Args:
            path: Path to load the model from
        """
        with open(path, 'rb') as f:
            data = pickle.load(f)

        self.model_x = data['model_x']
        self.model_y = data['model_y']
        self.is_trained = data['is_trained']
        print(f"Model loaded from {path}")
