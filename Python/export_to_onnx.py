import os
import json
import xgboost as xgb
import onnx
import joblib
from datetime import datetime
from onnxmltools import convert_xgboost
from onnxmltools.convert.common.data_types import FloatTensorType

# ==============================
# Configuration
# ==============================
TODAY = datetime.today().strftime("%Y%m%d")

MODELS_CONFIG_PATH = "/mnt/c/dev/AutomatedTradeStrategies/Models/Config/models.json"
with open(MODELS_CONFIG_PATH) as f:
    _config = json.load(f)

EXECUTION_CONFIGS = [
    {
        "name": m["modelName"],
        "dir":  os.path.join(m["featureLocation"], TODAY),
    }
    for m in _config["models"]
    if m.get("active") and m.get("export")
]

# ==============================
# Export one execution type
# ==============================
def export_model(name, model_input, scaler_input, onnx_output, scaler_output):
    print(f"\n{'='*50}")
    print(f"  Exporting: {name}")
    print(f"{'='*50}")

    print(f"Loading model from {model_input}...")
    model = xgb.XGBRegressor()
    model.load_model(model_input)
    print(f"  Features: {model.n_features_in_}")
    print(f"  Best iteration: {getattr(model, 'best_iteration', 'n/a')}")

    print(f"\nExporting to ONNX → {onnx_output}...")
    initial_type = [("float_input", FloatTensorType([None, model.n_features_in_]))]
    onnx_proto = convert_xgboost(model, initial_types=initial_type)
    with open(onnx_output, "wb") as f:
        f.write(onnx_proto.SerializeToString())

    # Verify the exported model is well-formed
    print("Verifying ONNX model...")
    onnx_model = onnx.load(onnx_output)
    onnx.checker.check_model(onnx_model)
    print(f"  Input:  {onnx_model.graph.input[0].name}  "
          f"{[d.dim_value for d in onnx_model.graph.input[0].type.tensor_type.shape.dim]}")
    print(f"  Output: {onnx_model.graph.output[0].name}")
    print("  Model is valid.")

    # Export Scaler Parameters → JSON
    # The C# inference layer applies scaling manually, so we export
    # mean and scale as plain JSON rather than the pkl binary.
    print(f"\nExporting scaler params → {scaler_output}...")
    scaler = joblib.load(scaler_input)

    if len(scaler.mean_) != model.n_features_in_:
        raise ValueError(
            f"Scaler has {len(scaler.mean_)} features but model expects "
            f"{model.n_features_in_}. Ensure model and scaler were trained together."
        )

    with open(scaler_output, "w") as f:
        json.dump({"mean": scaler.mean_.tolist(), "scale": scaler.scale_.tolist()}, f)

    print(f"  Features exported: {len(scaler.mean_)}")


# ==============================
# Main
# ==============================
for config in EXECUTION_CONFIGS:
    d = config["dir"]
    model_input = os.path.join(d, "xgb_model.json")
    scaler_input = os.path.join(d, "scaler.pkl")

    if not os.path.isfile(model_input) or not os.path.isfile(scaler_input):
        missing = [p for p in (model_input, scaler_input) if not os.path.isfile(p)]
        print(f"\nSkipping '{config['name']}': missing {', '.join(missing)}")
        continue

    onnx_output = os.path.join(d, "xgb_model.onnx")
    scaler_output = os.path.join(d, "scaler_params.json")

    export_model(config["name"], model_input, scaler_input, onnx_output, scaler_output)

print("\nDone.")
