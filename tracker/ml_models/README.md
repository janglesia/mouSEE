# ML Gaze Estimation Models

This directory contains machine learning models for gaze estimation using eye tracking data from the mouSEE project.

## Overview

Three scikit-learn models are implemented for estimating gaze coordinates from eye measurements:

1. **Random Forest Regressor** (`random_forest_gaze.py`)
   - Ensemble of decision trees
   - Hyperparameters: `n_estimators=100`, `max_depth=10`
   - Good for capturing non-linear relationships without overfitting

2. **Gradient Boosting Regressor** (`gradient_boosting_gaze.py`)
   - Sequential ensemble that corrects previous errors
   - Hyperparameters: `n_estimators=200`, `learning_rate=0.1`, `max_depth=5`
   - Often achieves high accuracy with careful tuning

3. **Ridge Regression with Polynomial Features** (`ridge_gaze.py`)
   - Linear model with polynomial feature expansion and L2 regularization
   - Pipeline: `PolynomialFeatures(degree=2)` → `Ridge(alpha=1.0)`
   - Simple, interpretable, and fast to train

All models:
- Train separate regressors for X and Y coordinates
- Accept 6 features: `[left_offset_h, left_offset_v, left_openness, right_offset_h, right_offset_v, right_openness]`
- Output 2 coordinates: `[x, y]` (normalized screen coordinates 0-1)
- Support save/load via pickle
- Use `random_state=42` for reproducibility

## How to Train Models

### Using the Main Script

The easiest way to train and compare all models is using the provided script:

```bash
python tracker/ml_models/train_and_compare.py <recording_path> [--output <output_dir>]
```

**Arguments:**
- `recording_path`: Path to an accuracy test JSON file (e.g., `app/Calibration/Saved/tests/*.json`)
- `--output`: Output directory for models and results (default: `tracker/ml_models/output`)
- `--test-size`: Fraction of data for testing (default: 0.2)
- `--random-state`: Random seed (default: 42)

**Example:**
```bash
python tracker/ml_models/train_and_compare.py app/Calibration/Saved/tests/2026-10-19_22-57-28.json --output tracker/ml_models/output
```

The script will:
1. Load the accuracy test recording
2. Extract features and targets
3. Split data into train/test sets (80/20 by default)
4. Train all three models
5. Evaluate on the test set
6. Print a comparison report
7. Save trained models to `<output_dir>/models/`
8. Save evaluation results to `<output_dir>/results.json`

### Training a Single Model Programmatically

```python
import numpy as np
from tracker.ml_models.random_forest_gaze import RandomForestGazeModel
from tracker.ml_models.data_loader import load_training_data

# Load data
X, y, metadata = load_training_data('path/to/recording.json')

# Initialize model
model = RandomForestGazeModel(n_estimators=100, max_depth=10, random_state=42)

# Train
model.train(X, y)

# Save
model.save('path/to/model.pkl')

# Load later
loaded_model = RandomForestGazeModel()
loaded_model.load('path/to/model.pkl')

# Predict
predictions = loaded_model.predict(X)
```

## How to Evaluate Models

### Using the Evaluator Module

```python
from tracker.ml_models.evaluator import evaluate_model, compare_models, print_comparison_report

# Evaluate a single model
metrics = evaluate_model(model, X_test, y_test)
print(f"MAE X: {metrics['mae_x']:.4f}")
print(f"MAE Y: {metrics['mae_y']:.4f}")
print(f"Euclidean MAE: {metrics['euclidean_mae']:.4f}")

# Compare multiple models
models_dict = {
    'RandomForest': rf_model,
    'GradientBoosting': gb_model,
    'Ridge': ridge_model,
}
results = compare_models(models_dict, X_test, y_test)

# Print comparison table
print_comparison_report(results)
```

### Metrics

The evaluator computes the following metrics:

- **MAE X/Y**: Mean Absolute Error for X and Y coordinates separately
- **RMSE X/Y**: Root Mean Squared Error for X and Y coordinates separately
- **Euclidean MAE**: Mean Euclidean distance error (combines X and Y)
- **Bias X/Y**: Mean signed error (indicates systematic over/under-prediction)

These metrics match the format used by the C# app's `AccuracyTest.Report()` for consistency.

## How to Use Trained Models for Prediction

Once a model is trained and saved, you can use it for real-time gaze prediction:

```python
import numpy as np
from tracker.ml_models.random_forest_gaze import RandomForestGazeModel

# Load the trained model
model = RandomForestGazeModel()
model.load('path/to/model.pkl')

# Prepare features from eye tracking data
# Features: [left_offset_h, left_offset_v, left_openness,
#            right_offset_h, right_offset_v, right_openness]
features = np.array([[
    0.1,   # left_offset_h
    -0.05, # left_offset_v
    0.7,   # left_openness
    0.08,  # right_offset_h
    -0.03, # right_offset_v
    0.75,  # right_openness
]])

# Predict gaze coordinates
prediction = model.predict(features)
x, y = prediction[0]

print(f"Predicted gaze: ({x:.3f}, {y:.3f})")
```

## Data Format

### Input Features

All models expect feature vectors of shape `(n_samples, 6)` with columns:

| Column | Name | Description | Range |
|--------|------|-------------|-------|
| 0 | left_offset_h | Left eye horizontal iris offset | -1 to 1 |
| 1 | left_offset_v | Left eye vertical iris offset | -1 to 1 |
| 2 | left_openness | Left eye lid openness | 0 to 1 |
| 3 | right_offset_h | Right eye horizontal iris offset | -1 to 1 |
| 4 | right_offset_v | Right eye vertical iris offset | -1 to 1 |
| 5 | right_openness | Right eye lid openness | 0 to 1 |

### Output Coordinates

Models predict gaze coordinates of shape `(n_samples, 2)` with columns:

| Column | Name | Description | Range |
|--------|------|-------------|-------|
| 0 | x | Horizontal screen position | 0 to 1 |
| 1 | y | Vertical screen position | 0 to 1 |

Coordinates are normalized to screen dimensions (0 = left/top, 1 = right/bottom).

## Recording Data Format

The models are trained on accuracy test recordings saved by the C# app. These JSON files contain:

```json
{
  "report": {
    "madeAt": "2024-01-01T00:00:00Z",
    "calibrationId": "...",
    "screenWidth": 1920,
    "screenHeight": 1080,
    ...
  },
  "frames": [
    {
      "index": 0,
      "x": 0.5,
      "y": 0.5,
      "phase": "Sit normally",
      "frames": [
        {
          "ms": 0,
          "faceFound": true,
          "yaw": -3.2,
          "pitch": -5.1,
          "left": {
            "offsetH": 0.1,
            "offsetV": -0.05,
            "openness": 0.7,
            "closed": false,
            "covered": false
          },
          "right": {
            "offsetH": 0.08,
            "offsetV": -0.03,
            "openness": 0.75,
            "closed": false,
            "covered": false
          }
        },
        ...
      ]
    },
    ...
  ]
}
```

**Key notes:**
- All keys are lowercase (`report`, `frames`, `left`, `right`, `offsetH`, etc.)
- Each target point has a nested `frames` array containing the actual frame samples
- The `phase` field indicates how you were sitting (e.g., "Sit normally", "Lean left", "Head tilted up")

The data loader (`data_loader.py`) extracts features and targets from this format, automatically skipping frames where eyes are closed, covered, or missing.

## Testing

Run the unit tests to verify the implementation:

```bash
python -m unittest tests.test_ml_models -v
```

All tests use synthetic data, so they don't require actual recordings.

Test coverage includes:
- Data loading and feature extraction
- Model training and prediction for all three models
- Model save/load functionality
- Evaluation metrics
- End-to-end pipeline testing

## Notes

- **Feature normalization**: The Ridge model benefits from normalized features, while Random Forest and Gradient Boosting are scale-invariant. The current implementation does not normalize features, but this could be added if needed.
- **Missing data**: Frames with closed, covered, or missing eyes are automatically skipped during data loading.
- **Reproducibility**: All models use `random_state=42` by default for reproducible results.
- **Hyperparameters**: The default hyperparameters are reasonable starting points but may need tuning for specific datasets or use cases.

## Comparison with Geometric Model

The existing geometric model (`tracker/gaze_model.py`) uses hand-tuned parameters and geometric calculations to estimate gaze. These ML models:

- **Pros**: Can learn complex, non-linear mappings from data; potentially higher accuracy with sufficient training data
- **Cons**: Require calibration recordings; may overfit if data is limited; less interpretable than geometric approach

The ML models are intended as an alternative/complement to the geometric model, allowing comparison and selection of the best approach for each user.
