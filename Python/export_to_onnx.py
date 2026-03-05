import xgboost as xgb
import onnxmltools
#import joblib
#import json
from onnxmltools.convert.common.data_types import FloatTensorType

# Load trained model
model = xgb.XGBClassifier()
model.load_model("xgb_model.json")

# Define input shape
feature_count = model.n_features_in_
initial_type = [("input", FloatTensorType([None, feature_count]))]

# Convert
onnx_model = onnxmltools.convert_xgboost(
    model,
    initial_types=initial_type
)

# Save
with open("xgb_model.onnx", "wb") as f:
    f.write(onnx_model.SerializeToString())

print("ONNX export complete.")

#scaler = joblib.load("scaler.pkl")

#params = {
#    "mean": scaler.mean_.tolist(),
#    "scale": scaler.scale_.tolist()
#}

#with open("scaler_params.json", "w") as f:
#    json.dump(params, f)