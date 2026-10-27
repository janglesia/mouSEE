"""
Main script to train and compare gaze estimation models.

Usage:
    python tracker/ml_models/train_and_compare.py <recording_path> [--output <output_dir>]
"""

import argparse
import json
import os
import sys
from pathlib import Path

import numpy as np
from sklearn.model_selection import train_test_split

# Add parent directory to path for imports
sys.path.insert(0, str(Path(__file__).parent.parent.parent))

from tracker.ml_models.random_forest_gaze import RandomForestGazeModel
from tracker.ml_models.gradient_boosting_gaze import GradientBoostingGazeModel
from tracker.ml_models.ridge_gaze import RidgeGazeModel
from tracker.ml_models.data_loader import load_training_data
from tracker.ml_models.evaluator import compare_models, print_comparison_report


def main():
    parser = argparse.ArgumentParser(
        description='Train and compare gaze estimation models on accuracy test data'
    )
    parser.add_argument(
        'recording_path',
        type=str,
        help='Path to accuracy test JSON file (e.g., app/Calibration/Saved/tests/*.json)'
    )
    parser.add_argument(
        '--output',
        type=str,
        default='tracker/ml_models/output',
        help='Output directory for models and results (default: tracker/ml_models/output)'
    )
    parser.add_argument(
        '--test-size',
        type=float,
        default=0.2,
        help='Fraction of data to use for testing (default: 0.2)'
    )
    parser.add_argument(
        '--random-state',
        type=int,
        default=42,
        help='Random seed for reproducibility (default: 42)'
    )

    args = parser.parse_args()

    print("=" * 80)
    print("GAZE ESTIMATION MODEL TRAINING AND COMPARISON")
    print("=" * 80)

    # Create output directory
    output_dir = Path(args.output)
    models_dir = output_dir / 'models'
    models_dir.mkdir(parents=True, exist_ok=True)

    print(f"\nOutput directory: {output_dir}")
    print(f"Recording path: {args.recording_path}")

    # Load data
    print("\n" + "-" * 80)
    print("LOADING DATA")
    print("-" * 80)
    try:
        X, y, metadata = load_training_data(args.recording_path)
    except Exception as e:
        print(f"Error loading data: {e}")
        sys.exit(1)

    if len(X) == 0:
        print("Error: No valid samples found in recording")
        sys.exit(1)

    # Split data
    print("\n" + "-" * 80)
    print("SPLITTING DATA")
    print("-" * 80)
    print(f"Total samples: {len(X)}")
    print(f"Train/test split: {1 - args.test_size:.0%} / {args.test_size:.0%}")

    # Stratify by phase if available
    phases = [m.get('phase', 'unknown') for m in metadata] if metadata else None
    if phases and len(set(phases)) > 1:
        X_train, X_test, y_train, y_test = train_test_split(
            X, y, test_size=args.test_size, random_state=args.random_state, stratify=phases
        )
        print(f"Stratified by phase: {set(phases)}")
    else:
        X_train, X_test, y_train, y_test = train_test_split(
            X, y, test_size=args.test_size, random_state=args.random_state
        )

    print(f"Training samples: {len(X_train)}")
    print(f"Test samples: {len(X_test)}")

    # Initialize models
    print("\n" + "-" * 80)
    print("INITIALIZING MODELS")
    print("-" * 80)

    models = {
        'RandomForest': RandomForestGazeModel(
            n_estimators=100,
            max_depth=10,
            random_state=args.random_state
        ),
        'GradientBoosting': GradientBoostingGazeModel(
            n_estimators=200,
            learning_rate=0.1,
            max_depth=5,
            random_state=args.random_state
        ),
        'Ridge': RidgeGazeModel(
            degree=2,
            alpha=1.0,
            random_state=args.random_state
        ),
    }

    for name, model in models.items():
        print(f"  {name}")

    # Train models
    print("\n" + "-" * 80)
    print("TRAINING MODELS")
    print("-" * 80)

    for name, model in models.items():
        print(f"\nTraining {name}...")
        try:
            model.train(X_train, y_train)
        except Exception as e:
            print(f"Error training {name}: {e}")
            sys.exit(1)

    # Evaluate models
    print("\n" + "-" * 80)
    print("EVALUATING MODELS")
    print("-" * 80)

    results = compare_models(models, X_test, y_test)

    # Print comparison
    print_comparison_report(results)

    # Save models
    print("\n" + "-" * 80)
    print("SAVING MODELS")
    print("-" * 80)

    for name, model in models.items():
        model_path = models_dir / f"{name.lower()}_gaze_model.pkl"
        try:
            model.save(str(model_path))
        except Exception as e:
            print(f"Error saving {name}: {e}")

    # Save results
    print("\n" + "-" * 80)
    print("SAVING RESULTS")
    print("-" * 80)

    results_path = output_dir / 'results.json'
    with open(results_path, 'w') as f:
        json.dump(results, f, indent=2)
    print(f"Results saved to {results_path}")

    # Save data split info
    split_info = {
        'total_samples': int(len(X)),
        'train_samples': int(len(X_train)),
        'test_samples': int(len(X_test)),
        'test_size': args.test_size,
        'random_state': args.random_state,
        'recording_path': args.recording_path,
    }
    split_info_path = output_dir / 'split_info.json'
    with open(split_info_path, 'w') as f:
        json.dump(split_info, f, indent=2)
    print(f"Split info saved to {split_info_path}")

    print("\n" + "=" * 80)
    print("TRAINING AND COMPARISON COMPLETE")
    print("=" * 80)
    print(f"\nAll outputs saved to: {output_dir}")
    print(f"  Models: {models_dir}")
    print(f"  Results: {results_path}")
    print(f"  Split info: {split_info_path}")


if __name__ == '__main__':
    main()
