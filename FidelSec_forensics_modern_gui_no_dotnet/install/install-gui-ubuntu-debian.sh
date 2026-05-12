#!/usr/bin/env bash
set -euo pipefail

APP_DIR="/opt/fidelsec"

sudo apt update
sudo apt install -y python3 python3-tk bash coreutils util-linux xdg-utils policykit-1

sudo mkdir -p "$APP_DIR"
sudo cp -r fidelsec "$APP_DIR/"
sudo mkdir -p "$APP_DIR/output" "$APP_DIR/logs" "$APP_DIR/config"
sudo cp fidelsec-gui "$APP_DIR/fidelsec-gui"
sudo chmod +x "$APP_DIR/fidelsec-gui"
sudo ln -sf "$APP_DIR/fidelsec-gui" /usr/local/bin/fidelsec-gui

if [ -d /usr/share/applications ]; then
    sudo cp desktop/fidelsec.desktop /usr/share/applications/fidelsec.desktop
fi

echo
echo "Installatie voltooid."
echo "Start met:"
echo "sudo fidelsec-gui"
echo
echo "Of lokaal:"
echo "sudo ./run-gui.sh"
