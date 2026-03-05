import pandas as pd
import numpy as np
import xgboost as xgb
import joblib
from sklearn.preprocessing import StandardScaler
from sklearn.metrics import (
    accuracy_score,
    precision_score,
    recall_score,
    roc_auc_score,
    confusion_matrix
)

# ==============================
# Configuration
# ==============================
CSV_PATH = "/mnt/c/dev/data/features/feature_20260226.csv"
MODEL_OUTPUT = "xgb_model.json"
SCALER_OUTPUT = "scaler.pkl"

TRAIN_SPLIT = 0.7
VAL_SPLIT = 0.15  # remaining 15% will be test


# ==============================
# Load Data
# ==============================
print("Loading CSV...")
df = pd.read_csv(CSV_PATH)

print(f"Dataset shape: {df.shape}")

# Assume last column is label
X = df.iloc[:, :-1].values
y = df.iloc[:, -1].values

print("Label distribution:")
unique, counts = np.unique(y, return_counts=True)
for u, c in zip(unique, counts):
    print(f"Label {u}: {c} ({c / len(y):.2%})")

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

print("\nSplit sizes:")
print(f"Train: {len(X_train)}")
print(f"Validation: {len(X_val)}")
print(f"Test: {len(X_test)}")

# ==============================
# Normalize (fit on train only!)
# ==============================
scaler = StandardScaler()
X_train_scaled = scaler.fit_transform(X_train)
X_val_scaled = scaler.transform(X_val)
X_test_scaled = scaler.transform(X_test)

# ==============================
# Train XGBoost
# ==============================
print("\nTraining XGBoost...")

model = xgb.XGBClassifier(
    n_estimators=300,
    max_depth=6,
    learning_rate=0.05,
    subsample=0.8,
    colsample_bytree=0.8,
    objective="binary:logistic",
    eval_metric="logloss",
    tree_method="hist",
    random_state=42
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
    probs = model.predict_proba(X_data)[:, 1]

    print(f"\n=== {split_name} Results ===")
    print(f"Accuracy:  {accuracy_score(y_data, preds):.4f}")
    print(f"Precision: {precision_score(y_data, preds):.4f}")
    print(f"Recall:    {recall_score(y_data, preds):.4f}")
    print(f"ROC AUC:   {roc_auc_score(y_data, probs):.4f}")
    print("Confusion Matrix:")
    print(confusion_matrix(y_data, preds))


evaluate("Train", X_train_scaled, y_train)
evaluate("Validation", X_val_scaled, y_val)
evaluate("Test", X_test_scaled, y_test)

# ==============================
# Save Model + Scaler
# ==============================
print("\nSaving model and scaler...")
model.save_model(MODEL_OUTPUT)
joblib.dump(scaler, SCALER_OUTPUT)

print("Done.")