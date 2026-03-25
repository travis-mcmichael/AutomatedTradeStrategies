#!/usr/bin/env bash
set -euo pipefail

# ==============================
# Configuration
# ==============================
REPO_ROOT="/mnt/c/dev/AutomatedTradeStrategies"
FEATURES_BASE="/mnt/c/dev/data/features"
MODEL_DATE="$(date +%Y%m%d)"
PYTHON="python3"
DOT_NET="/mnt/c/Program Files/dotnet/dotnet.exe"
dotnet() { "$DOT_NET" "$@"; }

EXECUTION_TYPES=("mes.day-bar.high" "mes.day-bar.low")

source /home/superuser/onnx-env/bin/activate

MODELS_DIR="$REPO_ROOT/Models/$MODEL_DATE"

# ==============================
# Step 1: Export features
# ==============================
echo ""
echo "======================================================"
echo "  Step 1: Export features"
echo "======================================================"

for exec_type in "${EXECUTION_TYPES[@]}"; do
    echo ""
    echo "  Exporting: $exec_type"
    dotnet run --project "$(wslpath -w "$REPO_ROOT/AutomatedTrading.Export.Feature")" \
        --configuration Release \
        -- "$(wslpath -w "$FEATURES_BASE")" "$exec_type"
done

# ==============================
# Step 2: Train XGBoost models
# ==============================
echo ""
echo "======================================================"
echo "  Step 2: Train XGBoost models"
echo "======================================================"
$PYTHON "$REPO_ROOT/Python/train_model_xgboost.py"

# ==============================
# Step 3: Export models to ONNX
# ==============================
echo ""
echo "======================================================"
echo "  Step 3: Export models to ONNX"
echo "======================================================"
$PYTHON "$REPO_ROOT/Python/export_to_onnx.py"

# ==============================
# Step 4: Copy models to project
# ==============================
echo ""
echo "======================================================"
echo "  Step 4: Copy models → project ($MODEL_DATE)"
echo "======================================================"

for exec_type in "${EXECUTION_TYPES[@]}"; do
    src="$FEATURES_BASE/$exec_type"
    onnx_file="$src/xgb_model.onnx"
    scaler_file="$src/scaler_params.json"

    if [[ ! -f "$onnx_file" || ! -f "$scaler_file" ]]; then
        echo "  Skipping '$exec_type': model files not found in $src"
        continue
    fi

    for dest_base in "$MODELS_DIR"; do
        dest="$dest_base/$exec_type"
        mkdir -p "$dest"
        cp "$onnx_file"   "$dest/xgb_model.onnx"
        cp "$scaler_file" "$dest/scaler_params.json"
        echo "  Copied $exec_type → $dest"
    done
done

echo ""
echo "Pipeline complete."
