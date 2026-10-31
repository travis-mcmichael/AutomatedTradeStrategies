import argparse
import os
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


# ==============================
# Train for one execution type
# ==============================
def train_model(name, csv_path, target):
    output_dir    = os.path.join(os.path.dirname(csv_path), TODAY)
    os.makedirs(output_dir, exist_ok=True)
    model_output  = os.path.join(output_dir, "xgb_model.json")
    scaler_output = os.path.join(output_dir, "scaler.pkl")

    print(f"\n{'='*50}")
    print(f"  Training: {name}")
    print(f"  Target:   {target}")
    print(f"  CSV:      {csv_path}")
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

    print("\nTarget distribution:")
    print(f"  Min:  {np.min(y):.4f}")
    print(f"  Max:  {np.max(y):.4f}")
    print(f"  Mean: {np.mean(y):.4f}")
    print(f"  Std:  {np.std(y):.4f}")

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
    evaluate_regression(model, "Train",      X_train_scaled, y_train)
    evaluate_regression(model, "Validation", X_val_scaled,   y_val)
    evaluate_regression(model, "Test",       X_test_scaled,  y_test)

    # Feature Importance
    feature_names = df.columns[:-1]
    importance = model.feature_importances_
    indices = np.argsort(importance)[::-1]

    print("\nTop Feature Importances:")
    for i in range(min(15, len(indices))):
        idx = indices[i]
        print(f"  {i+1:2d}. {feature_names[idx]:<30} {importance[idx]:.6f}")

    # Save Model + Scaler
    print("\nSaving model and scaler...")
    model.save_model(model_output)
    joblib.dump(scaler, scaler_output)
    print(f"  Model  → {model_output}")
    print(f"  Scaler → {scaler_output}")


# ==============================
# Main
# ==============================
def parse_args():
    parser = argparse.ArgumentParser(description="Train an XGBoost model from a features CSV and save the model and scaler.")
    parser.add_argument("--name", required=True, help="Model name, shown in the output.")
    parser.add_argument("--csv", required=True, help="Path to features.csv. The last column is the target.")
    parser.add_argument("--target", required=True, choices=["HIGH", "LOW"], help="Model target.")
    return parser.parse_args()


if __name__ == "__main__":
    args = parse_args()
    if not os.path.isfile(args.csv):
        raise SystemExit(f"Cannot train '{args.name}': {args.csv} not found.")

    train_model(args.name, args.csv, args.target)

    print("\nDone.")
