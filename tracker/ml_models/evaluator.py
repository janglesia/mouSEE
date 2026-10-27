"""
Evaluation metrics for gaze estimation models.

Computes MAE, RMSE, bias, and Euclidean distance error for model predictions.
"""

import numpy as np
from typing import Dict, Any
from sklearn.metrics import mean_absolute_error, mean_squared_error


def evaluate_model(model, X: np.ndarray, y: np.ndarray) -> Dict[str, float]:
    """
    Evaluate a model on test data.

    Args:
        model: Trained gaze model with predict() method
        X: Test features of shape (n_samples, 6)
        y: True targets of shape (n_samples, 2)

    Returns:
        Dictionary of metrics:
        - mae_x: Mean Absolute Error for X coordinate
        - mae_y: Mean Absolute Error for Y coordinate
        - rmse_x: Root Mean Squared Error for X coordinate
        - rmse_y: Root Mean Squared Error for Y coordinate
        - euclidean_mae: Mean Euclidean distance error
        - bias_x: Mean signed error for X coordinate
        - bias_y: Mean signed error for Y coordinate
    """
    # Predict
    y_pred = model.predict(X)

    # Split into X and Y components
    y_true_x = y[:, 0]
    y_true_y = y[:, 1]
    y_pred_x = y_pred[:, 0]
    y_pred_y = y_pred[:, 1]

    # MAE
    mae_x = mean_absolute_error(y_true_x, y_pred_x)
    mae_y = mean_absolute_error(y_true_y, y_pred_y)

    # RMSE
    rmse_x = np.sqrt(mean_squared_error(y_true_x, y_pred_x))
    rmse_y = np.sqrt(mean_squared_error(y_true_y, y_pred_y))

    # Euclidean distance error
    euclidean_errors = np.sqrt((y_true_x - y_pred_x)**2 + (y_true_y - y_pred_y)**2)
    euclidean_mae = np.mean(euclidean_errors)

    # Bias (mean signed error)
    bias_x = np.mean(y_pred_x - y_true_x)
    bias_y = np.mean(y_pred_y - y_true_y)

    return {
        'mae_x': float(mae_x),
        'mae_y': float(mae_y),
        'rmse_x': float(rmse_x),
        'rmse_y': float(rmse_y),
        'euclidean_mae': float(euclidean_mae),
        'bias_x': float(bias_x),
        'bias_y': float(bias_y),
    }


def compare_models(models_dict: Dict[str, Any], X: np.ndarray, y: np.ndarray) -> Dict[str, Dict[str, float]]:
    """
    Evaluate multiple models and return comparison results.

    Args:
        models_dict: Dictionary mapping model names to trained models
        X: Test features of shape (n_samples, 6)
        y: True targets of shape (n_samples, 2)

    Returns:
        Dictionary mapping model names to their evaluation metrics
    """
    results = {}

    for name, model in models_dict.items():
        print(f"Evaluating {name}...")
        metrics = evaluate_model(model, X, y)
        results[name] = metrics

    return results


def print_comparison_report(results: Dict[str, Dict[str, float]]):
    """
    Pretty-print a comparison of model evaluation results.

    Args:
        results: Dictionary mapping model names to their evaluation metrics
    """
    print("\n" + "=" * 80)
    print("MODEL COMPARISON REPORT")
    print("=" * 80)

    # Find best model for each metric
    best_mae = min(results.items(), key=lambda x: x[1]['euclidean_mae'])
    best_rmse = min(results.items(), key=lambda x: np.sqrt(x[1]['rmse_x']**2 + x[1]['rmse_y']**2))

    print(f"\nBest MAE (Euclidean): {best_mae[0]} ({best_mae[1]['euclidean_mae']:.4f})")
    print(f"Best RMSE (combined): {best_rmse[0]} ({np.sqrt(best_rmse[1]['rmse_x']**2 + best_rmse[1]['rmse_y']**2):.4f})")

    print("\n" + "-" * 80)
    print(f"{'Model':<25} {'MAE X':<10} {'MAE Y':<10} {'RMSE X':<10} {'RMSE Y':<10} {'Euclidean':<10}")
    print("-" * 80)

    for name, metrics in results.items():
        print(f"{name:<25} {metrics['mae_x']:<10.4f} {metrics['mae_y']:<10.4f} "
              f"{metrics['rmse_x']:<10.4f} {metrics['rmse_y']:<10.4f} {metrics['euclidean_mae']:<10.4f}")

    print("\n" + "-" * 80)
    print(f"{'Model':<25} {'Bias X':<10} {'Bias Y':<10}")
    print("-" * 80)

    for name, metrics in results.items():
        print(f"{name:<25} {metrics['bias_x']:<10.4f} {metrics['bias_y']:<10.4f}")

    print("=" * 80 + "\n")


def format_metrics_for_app(metrics: Dict[str, float]) -> Dict[str, float]:
    """
    Format metrics to match the C# app's AccuracyTest.Report() format.

    The app uses:
    - MeanErrorX: average absolute error for X (per phase)
    - BiasX: average signed error for X (per phase)
    - MeanErrorY: average absolute error for Y (per phase)

    Args:
        metrics: Metrics dictionary from evaluate_model()

    Returns:
        Dictionary with keys matching the app's format
    """
    return {
        'MeanErrorX': metrics['mae_x'],
        'BiasX': metrics['bias_x'],
        'MeanErrorY': metrics['mae_y'],
    }
