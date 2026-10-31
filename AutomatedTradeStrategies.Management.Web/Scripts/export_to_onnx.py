import argparse
import os
import json
import xgboost as xgb
import onnx
import joblib
from onnxmltools import convert_xgboost
from onnxmltools.convert.common.data_types import FloatTensorType


# ==============================
# Export one model
# ==============================
def export_model(name, model_input, scaler_input, onnx_output, scaler_output):
    print(f"\n{'='*50}")
    print(f"  Exporting: {name}")
    print(f"{'='*50}")

    print(f"Loading model from {model_input}...")
    model = xgb.XGBRegressor()
    model.load_model(model_input)
    print(f"  Features: {model.n_features_in_}")
    print(f"  Objective: {model.objective}")
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
def parse_args():
    parser = argparse.ArgumentParser(description="Export a trained XGBoost model and its scaler to ONNX and JSON.")
    parser.add_argument("--name", required=True, help="Model name, shown in the output.")
    parser.add_argument("--dir", required=True, help="Dated model folder holding xgb_model.json and scaler.pkl. Outputs are written here.")
    return parser.parse_args()


if __name__ == "__main__":
    args = parse_args()
    model_input = os.path.join(args.dir, "xgb_model.json")
    scaler_input = os.path.join(args.dir, "scaler.pkl")

    missing = [p for p in (model_input, scaler_input) if not os.path.isfile(p)]
    if missing:
        raise SystemExit(f"Cannot export '{args.name}': missing {', '.join(missing)}")

    export_model(
        args.name,
        model_input,
        scaler_input,
        os.path.join(args.dir, "xgb_model.onnx"),
        os.path.join(args.dir, "scaler_params.json"),
    )

    print("\nDone.")
