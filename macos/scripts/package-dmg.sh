#!/bin/sh
# Packages macos/.build/BorisCodeStatus.app into the release download,
# macos/.build/BorisCodeStatus-<version>-macOS.dmg: the app beside an Applications link, so
# installing is a drag. The name is the one UpdateChecker looks for (`macAssetSuffix`).
#
#   macos/scripts/build-app.sh && macos/scripts/package-dmg.sh
#
# It packages whatever signature the app has. build-app.sh signs ad hoc, which is enough to try a
# build locally; a public release needs the app signed with a Developer ID first and the .dmg
# signed, notarised and stapled after, all done by the Release workflow (see docs/macos-port.md).
set -eu

cd "$(dirname "$0")/.."

app=.build/BorisCodeStatus.app
if [ ! -d "$app" ]; then
    echo "No $app; run scripts/build-app.sh first." >&2
    exit 1
fi

# Read from the bundle, not Directory.Build.props, so the name always matches what is inside.
version=$(/usr/libexec/PlistBuddy -c 'Print CFBundleShortVersionString' "$app/Contents/Info.plist")
dmg=.build/BorisCodeStatus-$version-macOS.dmg

staging=$(mktemp -d)
trap 'rm -rf "$staging"' EXIT
ditto "$app" "$staging/BorisCodeStatus.app"
ln -s /Applications "$staging/Applications"

rm -f "$dmg"
hdiutil create -quiet -volname "BorisCodeStatus $version" -srcfolder "$staging" -fs HFS+ -format UDZO "$dmg"
hdiutil verify -quiet "$dmg"

echo "Packaged $dmg"
