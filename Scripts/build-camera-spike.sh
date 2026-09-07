#!/usr/bin/env bash
# Builds the Camera Extension spike (PRD 43) with xcodebuild and optionally installs it to /Applications.
#
# Usage: Scripts/build-camera-spike.sh [--install] [DEVELOPMENT_TEAM=ABCDE12345]
#
# System extensions only load from an app inside /Applications unless `systemextensionsctl developer on`
# has been run, hence --install. The Team ID can also be set once in
# Native/macOS/CameraExtensionSpike/Config/Signing.xcconfig.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SPIKE="$REPO_ROOT/Native/macOS/CameraExtensionSpike"
DERIVED="$SPIKE/build"
INSTALL=0
EXTRA=()
for arg in "$@"; do
  case "$arg" in
    --install) INSTALL=1 ;;
    *=*) EXTRA+=("$arg") ;;
    *) echo "Unknown argument: $arg" >&2; exit 2 ;;
  esac
done

if ! command -v xcodebuild >/dev/null; then
  echo "xcodebuild not found. Install Xcode 15 or newer and run: sudo xcode-select -s /Applications/Xcode.app" >&2
  exit 1
fi

python3 "$REPO_ROOT/Scripts/check-xcodeproj.py" "$SPIKE/VRMCastCameraSpike.xcodeproj"

echo "Building VRMCastCameraSpike (Release) ..."
xcodebuild \
  -project "$SPIKE/VRMCastCameraSpike.xcodeproj" \
  -scheme VRMCastCameraSpike \
  -configuration Release \
  -derivedDataPath "$DERIVED" \
  "${EXTRA[@]}" \
  build | tail -n 20

APP="$DERIVED/Build/Products/Release/VRMCastCameraSpike.app"
echo "Built: $APP"
codesign -dv --entitlements - "$APP" 2>&1 | sed -n '1,40p' || true

if [[ "$INSTALL" == 1 ]]; then
  echo "Installing to /Applications (replacing any previous copy) ..."
  rm -rf /Applications/VRMCastCameraSpike.app
  cp -R "$APP" /Applications/
  echo "Launch with: open /Applications/VRMCastCameraSpike.app"
fi
