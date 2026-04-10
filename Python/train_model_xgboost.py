import os
import json
import pandas as pd
import numpy as np
import xgboost as xgb
import joblib

from datetime import datetime
from sklearn.preprocessing import StandardScaler
from sklearn.metrics import (
    mean_absolute_error, mean_squared_error, r2_score,
    accuracy_score, roc_auc_score, precision_score, recall_score,
)

# ==============================
# Configuration
# ==============================
TODAY = datetime.today().strftime("%Y%m%d")

MODELS_CONFIG_PATH = "/mnt/c/dev/AutomatedTradeStrategies/Models/Config/models.json"
with open(MODELS_CONFIG_PATH) as f:
    _config = json.load(f)

EXECUTION_CONFIGS = [
    {
        "name":              m["modelName"],
        "csv":               os.path.join(m["featureLocation"], "features.csv"),
        "is_classification": m.get("target") == "CLASS",
    }
    for m in _config["models"]
    if m.get("active") and m.get("export")
]

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

    within_tenth_atr   = np.mean(np.abs(preds - y_data) < 0.1)
    within_quarter_atr = np.mean(np.abs(preds - y_data) < 0.25)
    within_half_atr    = np.mean(np.abs(preds - y_data) < 0.5)
    within_one_atr     = np.mean(np.abs(preds - y_data) < 1.0)

    print(f"\n=== {split_name} ===")
    print(f"  MAE:              {mae:.4f}")
    print(f"  RMSE:             {rmse:.4f}")
    print(f"  R²:               {r2:.4f}")
    print(f"  Within .10x ATR:  {within_tenth_atr:.2%}")
    print(f"  Within .25x ATR:  {within_quarter_atr:.2%}")
    print(f"  Within 0.5x ATR:  {within_half_atr:.2%}")
    print(f"  Within 1.0x ATR:  {within_one_atr:.2%}")


def evaluate_classification(model, split_name, X_data, y_data):
    preds = model.predict(X_data)
    probs = model.predict_proba(X_data)[:, 1]

    acc  = accuracy_score(y_data, preds)
    auc  = roc_auc_score(y_data, probs)
    prec = precision_score(y_data, preds, zero_division=0)
    rec  = recall_score(y_data, preds, zero_division=0)

    print(f"\n=== {split_name} ===")
    print(f"  Accuracy:   {acc:.4f}")
    print(f"  AUC:        {auc:.4f}")
    print(f"  Precision:  {prec:.4f}")
    print(f"  Recall:     {rec:.4f}")


# ==============================
# Train for one execution type
# ==============================
def train_model(name, csv_path, is_classification=False):
    output_dir    = os.path.join(os.path.dirname(csv_path), TODAY)
    os.makedirs(output_dir, exist_ok=True)
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

    if is_classification:
        print("\nTraining XGBoost Classifier (binary:logistic)...")
        model = xgb.XGBClassifier(
            n_estimators=400,
            max_depth=4,
            learning_rate=0.05,
            subsample=0.8,
            colsample_bytree=0.7,
            gamma=0,
            reg_alpha=1,
            reg_lambda=5,
            tree_method="hist",
            objective="binary:logistic",
            eval_metric="logloss",
            early_stopping_rounds=50,
            n_jobs=-1,
            random_state=42,
        )
    else:
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
    _evaluate = evaluate_classification if is_classification else evaluate_regression
    _evaluate(model, "Train",      X_train_scaled, y_train)
    _evaluate(model, "Validation", X_val_scaled,   y_val)
    _evaluate(model, "Test",       X_test_scaled,  y_test)

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
for config in EXECUTION_CONFIGS:
    if not os.path.isfile(config["csv"]):
        print(f"\nSkipping '{config['name']}': {config['csv']} not found.")
        continue
    train_model(config["name"], config["csv"], config["is_classification"])

print("\nDone.")
