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
# builds.
#
# Signed ad hoc unless SIGN_IDENTITY names a certificate, as the Release workflow does with its
# Developer ID. A real identity also gets the hardened runtime and a secure timestamp, both of
# which notarisation requires. Notarising is the workflow's job; see docs/macos-port.md.
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

# The pets' sounds, from shared/sounds, which the Windows build embeds too. See WaitingSound.swift.
mkdir -p "$app/Contents/Resources/Sounds"
cp "$root"/shared/sounds/*.wav "$app/Contents/Resources/Sounds/"

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

identity=${SIGN_IDENTITY:--}
if [ "$identity" = "-" ]; then
    options=""
else
    # No entitlements: the app is not sandboxed (it must write ~/.claude/settings.json) and uses
    # nothing the hardened runtime withholds.
    options="--options runtime --timestamp"
fi

# Inside out: the helper first, then the bundle that seals it.
# shellcheck disable=SC2086 # $options is deliberately split into separate arguments.
codesign --force --sign "$identity" $options --identifier au.getboris.BorisCodeStatusHooks "$app/Contents/Helpers/BorisCodeStatusHooks"
# shellcheck disable=SC2086
codesign --force --sign "$identity" $options "$app"

echo "Built $app ($version)"
