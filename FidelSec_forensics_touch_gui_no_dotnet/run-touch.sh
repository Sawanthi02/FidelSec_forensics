#!/usr/bin/env bash
set -euo pipefail
BASE_DIR="$(cd "$(dirname "$0")" && pwd)"
export PYTHONPATH="$BASE_DIR"
python3 "$BASE_DIR/fidelsec/gui/touch_app.py"
