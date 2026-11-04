#!/usr/bin/env bash
set -euo pipefail

# Builds AutomatedTradeStrategies.Management.Web in Release mode and deploys it to DEPLOY_DIR, then restarts
# the systemd service that runs it. The service runs:  dotnet <DEPLOY_DIR>/AutomatedTradeStrategies.Management.Web.dll
#
# Usage: deploy.sh            (run from anywhere; set the variables below or in the environment to override)

# ==============================
# Configuration
# ==============================
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT_FILE="$PROJECT_DIR/AutomatedTradeStrategies.Management.Web.csproj"
PUBLISH_DIR="${PUBLISH_DIR:-$PROJECT_DIR/bin/publish}"
DEPLOY_DIR="${DEPLOY_DIR:-/var/www/automated-trade-strategies-web}"
SERVICE_NAME="${SERVICE_NAME:-automated-trade-strategies-web}"
DOTNET="${DOTNET:-dotnet}"
# Address printed when the deploy finishes. Defaults to the unit's ASPNETCORE_URLS; set WEB_URL if a proxy serves it publicly.
WEB_URL="${WEB_URL:-http://127.0.0.1:5000}"

# Files that exist only on the server and must survive a redeploy.
PRESERVE=("appsettings.Production.json")

# Privilege command for the deploy folder and the service. Empty when already root, or set SUDO= to run without it.
SUDO="${SUDO-sudo}"
if [[ $EUID -eq 0 ]]; then SUDO=""; fi

run_root() {
    $SUDO "$@"
}

# ==============================
# Step 1: Build (publish) in Release mode
# ==============================
echo ""
echo "======================================================"
echo "  Step 1: Publish (Release) → $PUBLISH_DIR"
echo "======================================================"

rm -rf "$PUBLISH_DIR"
cd "$PROJECT_DIR"
"$DOTNET" publish "$(basename "$PROJECT_FILE")" --configuration Release --output "$PUBLISH_DIR"

# ==============================
# Step 2: Copy to the deploy folder
# ==============================
echo ""
echo "======================================================"
echo "  Step 2: Deploy → $DEPLOY_DIR"
echo "======================================================"

run_root mkdir -p "$DEPLOY_DIR"

exclude_args=()
for file in "${PRESERVE[@]}"; do
    exclude_args+=(--exclude "$file")
done

run_root rsync -a --delete "${exclude_args[@]}" "$PUBLISH_DIR/" "$DEPLOY_DIR/"
echo "  Copied $PUBLISH_DIR → $DEPLOY_DIR"

# ==============================
# Step 3: Restart the service
# ==============================
echo ""
echo "======================================================"
echo "  Step 3: Restart $SERVICE_NAME"
echo "======================================================"

if systemctl list-unit-files "$SERVICE_NAME.service" --no-legend | grep -q "^$SERVICE_NAME.service"; then
    # Picks up unit file changes; a no-op when the unit hasn't changed.
    run_root systemctl daemon-reload
    run_root systemctl restart "$SERVICE_NAME"
    run_root systemctl --no-pager status "$SERVICE_NAME" --lines=0
    if ! systemctl is-active --quiet "$SERVICE_NAME"; then
        echo "  $SERVICE_NAME is not running. Check: journalctl -u $SERVICE_NAME -n 50" >&2
        exit 1
    fi
else
    # systemd needs an absolute ExecStart path.
    echo "  Service '$SERVICE_NAME' is not installed. Install Scripts/$SERVICE_NAME.service:"
    echo "    sudo cp $SCRIPT_DIR/$SERVICE_NAME.service /etc/systemd/system/$SERVICE_NAME.service"
    echo "    sudo systemctl daemon-reload && sudo systemctl enable --now $SERVICE_NAME"
    echo "  Then run this script again. Its ExecStart runs: $(command -v "$DOTNET" || echo "$DOTNET") $DEPLOY_DIR/AutomatedTradeStrategies.Management.Web.dll"
    exit 1
fi

echo ""
echo "Deploy complete."
echo "Website running at: $WEB_URL"
