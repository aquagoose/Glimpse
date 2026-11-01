#!/bin/bash

SCRIPT_PATH=$(readlink -f "$0")
BASE_DIR=$(dirname "$SCRIPT_PATH")

echo "Installing Glimpse."

sudo rm -vrf "/opt/Glimpse" || exit 1

sudo mkdir -vp "/opt/Glimpse" || exit 1

sudo cp -vr "$BASE_DIR/bin"/* "/opt/Glimpse" || exit 1
sudo cp -vr "$BASE_DIR/icons"/* "/usr/share/icons" || exit 1
sudo cp -vr "$BASE_DIR/glimpse" "/usr/bin" || exit 1
sudo cp -vr "$BASE_DIR/Glimpse.desktop" "/usr/share/applications" || exit 1

echo "Glimpse has been installed!"
