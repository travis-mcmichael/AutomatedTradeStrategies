import xgboost as xgb
import onnx
import joblib
import json

# ==============================
# Configuration
# ==============================
MODEL_INPUT   = "xgb_model.json"
SCALER_INPUT  = "scaler.pkl"
ONNX_OUTPUT   = "xgb_model.onnx"
SCALER_OUTPUT = "scaler_params.json"

# ==============================
# Export XGBoost → ONNX
# Requires XGBoost 2.0+. Native export preserves full model fidelity
# and removes the onnxmltools/skl2onnx dependency chain.
# ==============================
print(f"Loading model from {MODEL_INPUT}...")
model = xgb.XGBRegressor()
model.load_model(MODEL_INPUT)
print(f"  Features: {model.n_features_in_}")
print(f"  Best iteration: {getattr(model, 'best_iteration', 'n/a')}")

print(f"\nExporting to ONNX → {ONNX_OUTPUT}...")
model.save_model(ONNX_OUTPUT)

# Verify the exported model is well-formed
print("Verifying ONNX model...")
onnx_model = onnx.load(ONNX_OUTPUT)

# check_model throws. Perhaps we wrap in try-except and handle gracefully?
onnx.checker.check_model(onnx_model)
print(f"  Input:  {onnx_model.graph.input[0].name}  "
      f"{[d.dim_value for d in onnx_model.graph.input[0].type.tensor_type.shape.dim]}")
print(f"  Output: {onnx_model.graph.output[0].name}")
print("  Model is valid.")

# ==============================
# Export Scaler Parameters → JSON
# The C# inference layer applies scaling manually, so we export
# mean and scale as plain JSON rather than the pkl binary.
# ==============================
print(f"\nExporting scaler params → {SCALER_OUTPUT}...")
scaler = joblib.load(SCALER_INPUT)

if len(scaler.mean_) != model.n_features_in_:
    raise ValueError(
        f"Scaler has {len(scaler.mean_)} features but model expects "
        f"{model.n_features_in_}. Ensure model and scaler were trained together."
    )

with open(SCALER_OUTPUT, "w") as f:
    json.dump({"mean": scaler.mean_.tolist(), "scale": scaler.scale_.tolist()}, f)

print(f"  Features exported: {len(scaler.mean_)}")
print("Done.")
