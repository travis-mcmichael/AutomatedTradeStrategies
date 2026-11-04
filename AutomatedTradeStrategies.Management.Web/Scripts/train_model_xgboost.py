import argparse
import json
import os
import sqlite3
import pandas as pd
import numpy as np
import xgboost as xgb
import joblib

from datetime import datetime
from sklearn.preprocessing import StandardScaler
from sklearn.metrics import mean_absolute_error, mean_squared_error, r2_score

# ==============================
# Configuration
# ==============================
TODAY = datetime.today().strftime("%Y%m%d")

TRAIN_SPLIT = 0.70
VAL_SPLIT   = 0.15   # remaining 15% = test
TOP_FEATURE_COUNT = 15

# ==============================
# Evaluate helpers
# ==============================
def evaluate_regression(model, split_name, X_data, y_data):
    preds = model.predict(X_data)

    mae  = mean_absolute_error(y_data, preds)
    rmse = np.sqrt(mean_squared_error(y_data, preds))
    r2   = r2_score(y_data, preds)

    diff = preds - y_data
    tenth_atr_over     = np.mean((diff > 0) & (diff < 0.1))
    tenth_atr_under    = np.mean((diff < 0) & (diff > -0.1))
    quarter_atr_over   = np.mean((diff > 0) & (diff < 0.25))
    quarter_atr_under  = np.mean((diff < 0) & (diff > -0.25))

    print(f"\n=== {split_name} ===")
    print(f"  MAE:                        {mae:.4f}")
    print(f"  RMSE:                       {rmse:.4f}")
    print(f"  R²:                         {r2:.4f}")
    print(f"  Less than .10x ATR over:    {tenth_atr_over:.2%}")
    print(f"  Less than .10x ATR under:   {tenth_atr_under:.2%}")
    print(f"  Less than .25x ATR over:    {quarter_atr_over:.2%}")
    print(f"  Less than .25x ATR under:   {quarter_atr_under:.2%}")

    return {
        "SplitName": split_name,
        "RowCount": int(len(y_data)),
        "MAE": float(mae),
        "RMSE": float(rmse),
        "RSquared": float(r2) if np.isfinite(r2) else None,
        "ATRLessThanTenPercentOver": float(tenth_atr_over),
        "ATRLessThanTenPercentUnder": float(tenth_atr_under),
        "ATRLessThan25PercentOver": float(quarter_atr_over),
        "ATRLessThan25PercentUnder": float(quarter_atr_under),
    }


# ==============================
# Database
# ==============================
def check_model_config(db_path, model_config_id, name):
    conn = sqlite3.connect(db_path)
    try:
        row = conn.execute("SELECT ModelName FROM ModelConfig WHERE Id = ?", (model_config_id,)).fetchone()
    finally:
        conn.close()

    if row is None:
        raise SystemExit(f"Cannot train '{name}': ModelConfigId {model_config_id} not found in {db_path}.")
    if row[0] != name:
        raise SystemExit(f"Cannot train '{name}': ModelConfigId {model_config_id} is '{row[0]}' in {db_path}.")


def save_performance(db_path, model_config_id, version, target_stats, best_iteration, best_score, top_features, splits):
    conn = sqlite3.connect(db_path)
    try:
        conn.execute("PRAGMA foreign_keys = ON")   # needed for the ON DELETE CASCADE from ModelPerformance to its splits
        with conn:   # one transaction: commits on success, rolls back on error
            # Retraining the same version replaces its earlier row, matching the artifacts that get overwritten.
            conn.execute("DELETE FROM ModelPerformance WHERE ModelConfigId = ? AND Version = ?",
                         (model_config_id, version))
            cursor = conn.execute(
                """INSERT INTO ModelPerformance
                       (ModelConfigId, Version, TrainedAt, BestIteration, BestValidationScore,
                        Min, Max, Mean, Std, TopFeatureImportance)
                   VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
                (model_config_id, version, datetime.now().isoformat(timespec="seconds"),
                 int(best_iteration), float(best_score),
                 target_stats["Min"], target_stats["Max"], target_stats["Mean"], target_stats["Std"],
                 json.dumps(top_features)))
            performance_id = cursor.lastrowid

            conn.executemany(
                """INSERT INTO ModelPerformanceSplit
                       (ModelPerformanceId, SplitName, RowCount, MAE, RMSE, RSquared,
                        ATRLessThanTenPercentOver, ATRLessThanTenPercentUnder,
                        ATRLessThan25PercentOver, ATRLessThan25PercentUnder)
                   VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
                [(performance_id, s["SplitName"], s["RowCount"], s["MAE"], s["RMSE"], s["RSquared"],
                  s["ATRLessThanTenPercentOver"], s["ATRLessThanTenPercentUnder"],
                  s["ATRLessThan25PercentOver"], s["ATRLessThan25PercentUnder"])
                 for s in splits])
    finally:
        conn.close()
    print(f"  Performance → {db_path} (ModelPerformance.Id {performance_id})")


# ==============================
# Train for one execution type
# ==============================
def train_model(name, csv_path, target, version, db_path, model_config_id):
    output_dir    = os.path.join(os.path.dirname(csv_path), version)
    os.makedirs(output_dir, exist_ok=True)
    model_output  = os.path.join(output_dir, "xgb_model.json")
    scaler_output = os.path.join(output_dir, "scaler.pkl")

    print(f"\n{'='*50}")
    print(f"  Training: {name}")
    print(f"  Target:   {target}")
    print(f"  CSV:      {csv_path}")
    print(f"  Version:  {version}")
    print(f"{'='*50}")

    # Load Data
    print("Loading CSV...")
    df = pd.read_csv(csv_path)

    print(df.head())
    print(df.dtypes)

    df = df.fillna(df.mean(numeric_only=True))
    print(f"Dataset shape: {df.shape}")

    X = df.iloc[:, :-1].values
    y = df.iloc[:, -1].values

    target_stats = {
        "Min":  float(np.min(y)),
        "Max":  float(np.max(y)),
        "Mean": float(np.mean(y)),
        "Std":  float(np.std(y)),
    }

    print("\nTarget distribution:")
    print(f"  Min:  {target_stats['Min']:.4f}")
    print(f"  Max:  {target_stats['Max']:.4f}")
    print(f"  Mean: {target_stats['Mean']:.4f}")
    print(f"  Std:  {target_stats['Std']:.4f}")

    # Chronological Split
    n = len(X)
    train_end = int(n * TRAIN_SPLIT)
    val_end = int(n * (TRAIN_SPLIT + VAL_SPLIT))

    X_train, y_train = X[:train_end],        y[:train_end]
    X_val, y_val = X[train_end:val_end],  y[train_end:val_end]
    X_test, y_test = X[val_end:],           y[val_end:]

    print("\nDataset splits:")
    print(f"  Train:      {len(X_train)}")
    print(f"  Validation: {len(X_val)}")
    print(f"  Test:       {len(X_test)}")

    # Normalize (fit on train only)
    scaler = StandardScaler()
    X_train_scaled = scaler.fit_transform(X_train)
    X_val_scaled = scaler.transform(X_val)
    X_test_scaled = scaler.transform(X_test)

    print("\nTraining XGBoost Regressor...")
    model = xgb.XGBRegressor(
        n_estimators=400,
        max_depth=2,
        learning_rate=0.05,
        subsample=0.8,
        colsample_bytree=0.7,
        gamma=0,
        reg_alpha=1,
        reg_lambda=10,
        tree_method="hist",
        objective="reg:squarederror",
        early_stopping_rounds=50,
        n_jobs=-1,
        random_state=42,
    )

    model.fit(
        X_train_scaled,
        y_train,
        eval_set=[(X_val_scaled, y_val)],
        verbose=50,
    )

    print(f"\nBest iteration: {model.best_iteration}  |  Best val score: {model.best_score:.6f}")

    # Evaluate
    splits = [
        evaluate_regression(model, "Train",      X_train_scaled, y_train),
        evaluate_regression(model, "Validation", X_val_scaled,   y_val),
        evaluate_regression(model, "Test",       X_test_scaled,  y_test),
    ]

    # Feature Importance
    feature_names = df.columns[:-1]
    importance = model.feature_importances_
    indices = np.argsort(importance)[::-1]

    print("\nTop Feature Importances:")
    top_features = {}
    for i in range(min(TOP_FEATURE_COUNT, len(indices))):
        idx = indices[i]
        print(f"  {i+1:2d}. {feature_names[idx]:<30} {importance[idx]:.6f}")
        top_features[str(feature_names[idx])] = float(importance[idx])

    # Save Model + Scaler
    print("\nSaving model and scaler...")
    model.save_model(model_output)
    joblib.dump(scaler, scaler_output)
    print(f"  Model  → {model_output}")
    print(f"  Scaler → {scaler_output}")

    # Save Performance
    print("\nSaving performance...")
    save_performance(db_path, model_config_id, version, target_stats,
                     model.best_iteration, model.best_score, top_features, splits)


# ==============================
# Main
# ==============================
def parse_args():
    parser = argparse.ArgumentParser(description="Train an XGBoost model from a features CSV, save the model and scaler, and record its performance.")
    parser.add_argument("--name", required=True, help="Model name, shown in the output.")
    parser.add_argument("--csv", required=True, help="Path to features.csv. The last column is the target.")
    parser.add_argument("--target", required=True, choices=["HIGH", "LOW"], help="Model target.")
    parser.add_argument("--version", default=TODAY, help="Version folder to write the model to. Defaults to today (yyyyMMdd).")
    parser.add_argument("--db", required=True, help="SQLite database holding the ModelConfig and ModelPerformance tables.")
    parser.add_argument("--model-config-id", required=True, type=int, help="ModelConfig.Id the performance is recorded against.")
    return parser.parse_args()


if __name__ == "__main__":
    args = parse_args()
    if not os.path.isfile(args.csv):
        raise SystemExit(f"Cannot train '{args.name}': {args.csv} not found.")
    if not os.path.isfile(args.db):
        raise SystemExit(f"Cannot train '{args.name}': database {args.db} not found.")

    # Checked before training so a bad id doesn't cost a full training run.
    check_model_config(args.db, args.model_config_id, args.name)

    train_model(args.name, args.csv, args.target, args.version, args.db, args.model_config_id)

    print("\nDone.")
