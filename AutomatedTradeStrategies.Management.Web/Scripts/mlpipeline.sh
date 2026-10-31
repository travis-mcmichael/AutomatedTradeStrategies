#!/usr/bin/env bash
set -euo pipefail

# Trains and deploys one model. Features must already be exported to <feature-dir>/features.csv
# (the Management web app's feature export writes it there).
#
# Usage: mlpipeline.sh --name <model name> --feature-dir <features folder> --target <HIGH|LOW>

# ==============================
# Configuration
# ==============================
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"   # the Python scripts sit alongside this script
OUTPUT_BASE="/mnt/c/dev/AutomatedTradeStrategies/Models"   # outputLocation: the API loads <OUTPUT_BASE>/<model name>/<version>/
MODEL_DATE="$(date +%Y%m%d)"
PYTHON="python3"

usage() {
    echo "Usage: $0 --name <model name> --feature-dir <features folder> --target <HIGH|LOW>" >&2
    exit 1
}

# ==============================
# Arguments
# ==============================
NAME=""
FEATURE_DIR=""
TARGET=""
while [[ $# -gt 0 ]]; do
    [[ $# -ge 2 ]] || usage
    case "$1" in
        --name)        NAME="$2";        shift 2 ;;
        --feature-dir) FEATURE_DIR="$2"; shift 2 ;;
        --target)      TARGET="$2";      shift 2 ;;
        *)             usage ;;
    esac
done

[[ -n "$NAME" && -n "$FEATURE_DIR" && -n "$TARGET" ]] || usage
[[ "$TARGET" == "HIGH" || "$TARGET" == "LOW" ]] || usage

CSV="$FEATURE_DIR/features.csv"
MODEL_OUT="$FEATURE_DIR/$MODEL_DATE"   # dated folder that training and export write to
API_DEST="$OUTPUT_BASE/$NAME/$MODEL_DATE"

source "${ONNX_VENV:-/home/superuser/onnx-env}/bin/activate"

# ==============================
# Step 1: Check features
# ==============================
echo ""
echo "======================================================"
echo "  Step 1: Check features for $NAME"
echo "======================================================"

if [[ ! -f "$CSV" ]]; then
    echo "  $CSV not found. Export the features for $NAME from the Management web app first." >&2
    exit 1
fi

# ==============================
# Step 2: Train XGBoost model
# ==============================
echo ""
echo "======================================================"
echo "  Step 2: Train XGBoost model ($NAME, $TARGET)"
echo "======================================================"
$PYTHON "$SCRIPT_DIR/train_model_xgboost.py" --name "$NAME" --csv "$CSV" --target "$TARGET"

# ==============================
# Step 3: Export model to ONNX
# ==============================
echo ""
echo "======================================================"
echo "  Step 3: Export model to ONNX"
echo "======================================================"
$PYTHON "$SCRIPT_DIR/export_to_onnx.py" --name "$NAME" --dir "$MODEL_OUT"

# ==============================
# Step 4: Copy model to project
# ==============================
echo ""
echo "======================================================"
echo "  Step 4: Copy model → $API_DEST"
echo "======================================================"

for file in xgb_model.onnx scaler_params.json; do
    if [[ ! -f "$MODEL_OUT/$file" ]]; then
        echo "  $MODEL_OUT/$file not found. Nothing copied." >&2
        exit 1
    fi
done

mkdir -p "$API_DEST"
cp "$MODEL_OUT/xgb_model.onnx"     "$API_DEST/xgb_model.onnx"
cp "$MODEL_OUT/scaler_params.json" "$API_DEST/scaler_params.json"
echo "  Copied $NAME → $API_DEST"

echo ""
echo "Pipeline complete."
