import os
import pandas as pd
import numpy as np
import xgboost as xgb
import joblib

from sklearn.preprocessing import StandardScaler
from sklearn.metrics import mean_absolute_error, mean_squared_error, r2_score

# ==============================
# Configuration
# ==============================
FEATURES_BASE = "/mnt/c/dev/data/features"

EXECUTION_CONFIGS = [
    {"name": "tick",     "csv": os.path.join(FEATURES_BASE, "tick",     "features.csv")},
    {"name": "bar-high", "csv": os.path.join(FEATURES_BASE, "bar-high", "features.csv")},
    {"name": "bar-low",  "csv": os.path.join(FEATURES_BASE, "bar-low",  "features.csv")},
]

TRAIN_SPLIT = 0.70
VAL_SPLIT   = 0.15   # remaining 15% = test


# ==============================
# Evaluate helper
# ==============================
def evaluate(model, split_name, X_data, y_data):
    preds = model.predict(X_data)

    mae  = mean_absolute_error(y_data, preds)
    rmse = np.sqrt(mean_squared_error(y_data, preds))
    r2   = r2_score(y_data, preds)

    within_half_atr = np.mean(np.abs(preds - y_data) < 0.5)
    within_one_atr  = np.mean(np.abs(preds - y_data) < 1.0)

    print(f"\n=== {split_name} ===")
    print(f"  MAE:               {mae:.4f}")
    print(f"  RMSE:              {rmse:.4f}")
    print(f"  R²:                {r2:.4f}")
    print(f"  Within 0.5x ATR:  {within_half_atr:.2%}")
    print(f"  Within 1.0x ATR:  {within_one_atr:.2%}")


# ==============================
# Train for one execution type
# ==============================
def train_model(name, csv_path):
    output_dir    = os.path.dirname(csv_path)
    model_output  = os.path.join(output_dir, "xgb_model.json")
    scaler_output = os.path.join(output_dir, "scaler.pkl")

    print(f"\n{'='*50}")
    print(f"  Training: {name}")
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
    n         = len(X)
    train_end = int(n * TRAIN_SPLIT)
    val_end   = int(n * (TRAIN_SPLIT + VAL_SPLIT))

    X_train, y_train = X[:train_end],        y[:train_end]
    X_val,   y_val   = X[train_end:val_end],  y[train_end:val_end]
    X_test,  y_test  = X[val_end:],           y[val_end:]

    print("\nDataset splits:")
    print(f"  Train:      {len(X_train)}")
    print(f"  Validation: {len(X_val)}")
    print(f"  Test:       {len(X_test)}")

    # Normalize (fit on train only)
    scaler         = StandardScaler()
    X_train_scaled = scaler.fit_transform(X_train)
    X_val_scaled   = scaler.transform(X_val)
    X_test_scaled  = scaler.transform(X_test)

    # Train XGBoost Regression
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
        n_jobs=-1,
        random_state=42,
    )

    model.fit(
        X_train_scaled,
        y_train,
        eval_set=[(X_val_scaled, y_val)],
        early_stopping_rounds=50,
        verbose=50,
    )

    print(f"\nBest iteration: {model.best_iteration}  |  Best val score: {model.best_score:.6f}")

    # Evaluate
    evaluate(model, "Train",      X_train_scaled, y_train)
    evaluate(model, "Validation", X_val_scaled,   y_val)
    evaluate(model, "Test",       X_test_scaled,  y_test)

    # Feature Importance
    feature_names = df.columns[:-1]
    importance    = model.feature_importances_
    indices       = np.argsort(importance)[::-1]

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
for config in EXECUTION_CONFIGS:
    if not os.path.isfile(config["csv"]):
        print(f"\nSkipping '{config['name']}': {config['csv']} not found.")
        continue
    train_model(config["name"], config["csv"])

print("\nDone.")
