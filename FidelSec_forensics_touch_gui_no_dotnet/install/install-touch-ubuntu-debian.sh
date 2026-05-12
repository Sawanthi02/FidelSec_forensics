#!/usr/bin/env bash
set -euo pipefail

APP_DIR="/opt/fidelsec-touch"

sudo apt update
sudo apt install -y python3 python3-tk bash coreutils util-linux xdg-utils policykit-1

sudo mkdir -p "$APP_DIR"
sudo cp -r fidelsec "$APP_DIR/"
sudo mkdir -p "$APP_DIR/output" "$APP_DIR/logs" "$APP_DIR/config"
sudo cp fidelsec-touch "$APP_DIR/fidelsec-touch"
sudo chmod +x "$APP_DIR/fidelsec-touch"
sudo ln -sf "$APP_DIR/fidelsec-touch" /usr/local/bin/fidelsec-touch

if [ -d /usr/share/applications ]; then
    sudo cp desktop/fidelsec-touch.desktop /usr/share/applications/fidelsec-touch.desktop
fi

echo
echo "Installatie voltooid."
echo "Start met:"
echo "sudo fidelsec-touch"
echo
echo "Of lokaal:"
echo "sudo ./run-touch.sh"
