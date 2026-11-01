#!/bin/sh
# Renders the app icon (AppIcon.swift) into macos/Resources/AppIcon.icns, which build-app.sh
# copies into the bundle. The .icns is committed, so only rerun this after changing the icon.
#
#   macos/scripts/make-icon.sh
set -eu

cd "$(dirname "$0")/.."

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
iconset="$work/AppIcon.iconset"

APP_ICON_DIR="$iconset" swift test --filter AppIconTests
iconutil --convert icns --output Resources/AppIcon.icns "$iconset"

echo "Wrote Resources/AppIcon.icns"
