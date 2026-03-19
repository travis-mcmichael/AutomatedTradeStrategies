import pandas as pd
import numpy as np
import xgboost as xgb
import joblib

from sklearn.preprocessing import StandardScaler
from sklearn.metrics import (
    mean_absolute_error,
    mean_squared_error,
    r2_score
)

# ==============================
# Configuration
# ==============================
CSV_PATH = "/mnt/c/dev/data/features/barFeatures_20260309.csv"
MODEL_OUTPUT = "xgb_model.json"
SCALER_OUTPUT = "scaler.pkl"

TRAIN_SPLIT = 0.7
VAL_SPLIT = 0.15  # remaining 15% = test


# ==============================
# Load Data
# ==============================
print("Loading CSV...")
df = pd.read_csv(CSV_PATH)

print(df.head())
print(df.dtypes)

# Replace NaNs with column means
df = df.fillna(df.mean())

print(f"Dataset shape: {df.shape}")

# Last column is the target
X = df.iloc[:, :-1].values
y = df.iloc[:, -1].values

print("\nTarget distribution:")
print(f"Min: {np.min(y):.6f}")
print(f"Max: {np.max(y):.6f}")
print(f"Mean: {np.mean(y):.6f}")
print(f"Std: {np.std(y):.6f}")

# ==============================
# Chronological Split
# ==============================
n = len(X)

train_end = int(n * TRAIN_SPLIT)
val_end = int(n * (TRAIN_SPLIT + VAL_SPLIT))

X_train = X[:train_end]
y_train = y[:train_end]

X_val = X[train_end:val_end]
y_val = y[train_end:val_end]

X_test = X[val_end:]
y_test = y[val_end:]

print("\nDataset splits:")
print(f"Train: {len(X_train)}")
print(f"Validation: {len(X_val)}")
print(f"Test: {len(X_test)}")

print(f"\nX_train.shape: {X_train.shape}")
print(f"X_val.shape: {X_val.shape}")
print(f"X_test.shape: {X_test.shape}")

# ==============================
# Normalize (fit on train only)
# ==============================
scaler = StandardScaler()

X_train_scaled = scaler.fit_transform(X_train)
X_val_scaled = scaler.transform(X_val)
X_test_scaled = scaler.transform(X_test)

# ==============================
# Train XGBoost Regression
# ==============================
print("\nTraining XGBoost Regressor...")

model = xgb.XGBRegressor(
    n_estimators=1000,
    max_depth=4,
    learning_rate=0.03,
    subsample=0.7,
    colsample_bytree=0.5,
    reg_alpha=1,
    reg_lambda=10,
    min_child_weight=10,
    tree_method="hist",
    objective="reg:squarederror",
    random_state=42,
    early_stopping_rounds=50
)

model.fit(
    X_train_scaled,
    y_train,
    eval_set=[(X_val_scaled, y_val)],
    verbose=True
)

# ==============================
# Evaluate
# ==============================
def evaluate(split_name, X_data, y_data):
    preds = model.predict(X_data)

    mae = mean_absolute_error(y_data, preds)
    rmse = np.sqrt(mean_squared_error(y_data, preds))
    r2 = r2_score(y_data, preds)

    print(f"\n=== {split_name} Results ===")
    print(f"MAE:  {mae:.6f}")
    print(f"RMSE: {rmse:.6f}")
    print(f"R²:   {r2:.4f}")


evaluate("Train", X_train_scaled, y_train)
evaluate("Validation", X_val_scaled, y_val)
evaluate("Test", X_test_scaled, y_test)

# ==============================
# Feature Importance
# ==============================
print("\nTop Feature Importances:")

importance = model.feature_importances_
indices = np.argsort(importance)[::-1]

for i in range(min(15, len(indices))):
    print(f"{i+1}. Feature {indices[i]}: {importance[indices[i]]:.6f}")

# ==============================
# Save Model + Scaler
# ==============================
print("\nSaving model and scaler...")

model.save_model(MODEL_OUTPUT)
joblib.dump(scaler, SCALER_OUTPUT)

print("Done.")