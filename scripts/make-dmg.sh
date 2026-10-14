#!/bin/zsh
# Builds the drag-to-install disk image for a Release build of MockiExams.
# Usage: scripts/make-dmg.sh [path/to/MockiExams.app]
# Output: build/MockiExams-<version>.dmg
set -euo pipefail

ROOT="${0:A:h:h}"
APP="${1:-$ROOT/build/Release/Build/Products/Release/MockiExams.app}"
WORK="$ROOT/build/dmg"
VENV="$ROOT/build/dmgbuild-venv"

[[ -d "$APP" ]] || { echo "No app at $APP. Build the Release configuration first." >&2; exit 1; }
# The background is stored as an alias to /Volumes/MockiExams, so that name must be free.
[[ ! -e /Volumes/MockiExams ]] || { echo "Eject the mounted MockiExams disk first." >&2; exit 1; }
VERSION=$(/usr/libexec/PlistBuddy -c "Print :CFBundleShortVersionString" "$APP/Contents/Info.plist")
OUT="$ROOT/build/MockiExams-$VERSION.dmg"

rm -rf "$WORK"
mkdir -p "$WORK/VolumeIcon.iconset"

swift "$ROOT/scripts/make-dmg-background.swift" "$WORK"
tiffutil -cathidpicheck "$WORK/bg.png" "$WORK/bg@2x.png" -out "$WORK/background.tiff" >/dev/null

cp "$ROOT"/Sources/App/Assets.xcassets/AppIcon.appiconset/icon_*.png "$WORK/VolumeIcon.iconset/"
iconutil -c icns "$WORK/VolumeIcon.iconset" -o "$WORK/VolumeIcon.icns"

if [[ ! -x "$VENV/bin/dmgbuild" ]]; then
  python3 -m venv "$VENV"
  "$VENV/bin/pip" install --quiet dmgbuild
fi

rm -f "$OUT"
"$VENV/bin/dmgbuild" -s "$ROOT/scripts/dmg-settings.py" \
  -D app="$APP" -D background="$WORK/background.tiff" -D volume_icon="$WORK/VolumeIcon.icns" \
  "MockiExams" "$OUT"

echo "$OUT"
shasum -a 256 "$OUT"
