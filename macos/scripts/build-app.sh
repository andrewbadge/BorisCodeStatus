#!/bin/sh
# Assembles BorisCodeStatus.app from the SwiftPM products, ad-hoc signed.
#
#   macos/scripts/build-app.sh            # universal (arm64 + x86_64) release build
#   macos/scripts/build-app.sh --debug    # this machine's architecture, debug build
#
# Writes macos/.build/BorisCodeStatus.app. A bundle there will not register hooks: the app refuses
# any path under .build/ (HookPathGuard), so copy it to /Applications to use it for real.
#
# The version comes from <Version> in Directory.Build.props, the single source of truth for both
# builds. Developer ID signing and notarisation are not done here; see docs/macos-port.md.
set -eu

cd "$(dirname "$0")/.."
root=$(cd .. && pwd)

version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props" | head -n 1)
if [ -z "$version" ]; then
    echo "Could not read <Version> from Directory.Build.props." >&2
    exit 1
fi

if [ "${1:-}" = "--debug" ]; then
    swift build -c debug --product BorisCodeStatus
    swift build -c debug --product BorisCodeStatusHooks
    products=$(swift build -c debug --show-bin-path)
else
    swift build -c release --arch arm64 --arch x86_64 --product BorisCodeStatus
    swift build -c release --arch arm64 --arch x86_64 --product BorisCodeStatusHooks
    products=$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)
fi

app=.build/BorisCodeStatus.app
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Helpers" "$app/Contents/Resources"

cp "$products/BorisCodeStatus" "$app/Contents/MacOS/BorisCodeStatus"
cp "$products/BorisCodeStatusHooks" "$app/Contents/Helpers/BorisCodeStatusHooks"

cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleIdentifier</key>
    <string>au.getboris.BorisCodeStatus</string>
    <key>CFBundleName</key>
    <string>BorisCodeStatus</string>
    <key>CFBundleDisplayName</key>
    <string>BorisCodeStatus</string>
    <key>CFBundleExecutable</key>
    <string>BorisCodeStatus</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>$version</string>
    <key>CFBundleVersion</key>
    <string>$version</string>
    <key>LSMinimumSystemVersion</key>
    <string>14.0</string>
    <key>LSUIElement</key>
    <true/>
    <key>NSHumanReadableCopyright</key>
    <string>GPL-3.0-or-later</string>
</dict>
</plist>
PLIST

# Inside out: the helper first, then the bundle that seals it.
codesign --force --sign - --identifier au.getboris.BorisCodeStatusHooks "$app/Contents/Helpers/BorisCodeStatusHooks"
codesign --force --sign - "$app"

echo "Built $app ($version)"
