#!/usr/bin/env bash
# Packages VRMCast.app for the virtual camera: builds the Camera Extension for the Unity app's bundle
# identifier, embeds it, signs everything with a Developer ID and (optionally) installs to /Applications.
#
# Usage: Scripts/package-macos.sh [--install] DEVELOPMENT_TEAM=ABCDE12345 [APP=Builds/macOS/VRMCast.app]
#        [SIGN_IDENTITY="Developer ID Application: Name (TEAM)"]
#
# Steps: 1) Scripts/build-frame-bridge.sh (plugin must exist before the Unity build so it is inside the app)
#        2) Unity build (Scripts/build-macos.sh) if APP does not exist
#        3) xcodebuild the VRMCastCameraExtension target with VRMCAST_HOST_BUNDLE_ID=<unity bundle id>
#        4) copy it to VRMCast.app/Contents/Library/SystemExtensions/
#        5) codesign extension then app with the host entitlements (system-extension install, app group)
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SPIKE="$REPO_ROOT/Native/macOS/CameraExtensionSpike"
INSTALL=0
APP="$REPO_ROOT/Builds/macOS/VRMCast.app"
DEVELOPMENT_TEAM="${DEVELOPMENT_TEAM:-}"
SIGN_IDENTITY="${SIGN_IDENTITY:-}"
for arg in "$@"; do
  case "$arg" in
    --install) INSTALL=1 ;;
    DEVELOPMENT_TEAM=*) DEVELOPMENT_TEAM="${arg#*=}" ;;
    APP=*) APP="${arg#*=}" ;;
    SIGN_IDENTITY=*) SIGN_IDENTITY="${arg#*=}" ;;
    *) echo "Unknown argument: $arg" >&2; exit 2 ;;
  esac
done
if [[ -z "$DEVELOPMENT_TEAM" ]]; then
  echo "DEVELOPMENT_TEAM=<10-character Team ID> is required (system extensions must be team-signed)." >&2
  exit 2
fi
if [[ -z "$SIGN_IDENTITY" ]]; then
  SIGN_IDENTITY="$(security find-identity -v -p codesigning | grep "Developer ID Application" | grep "$DEVELOPMENT_TEAM" | head -1 | sed -E 's/.*"(.*)"/\1/' || true)"
  if [[ -z "$SIGN_IDENTITY" ]]; then
    SIGN_IDENTITY="$(security find-identity -v -p codesigning | grep "Apple Development" | grep "$DEVELOPMENT_TEAM" | head -1 | sed -E 's/.*"(.*)"/\1/' || true)"
  fi
  if [[ -z "$SIGN_IDENTITY" ]]; then
    echo "No signing identity for team $DEVELOPMENT_TEAM found in the keychain; pass SIGN_IDENTITY=..." >&2
    exit 2
  fi
fi
echo "Signing identity: $SIGN_IDENTITY"

"$REPO_ROOT/Scripts/build-frame-bridge.sh"
if [[ ! -d "$APP" ]]; then
  "$REPO_ROOT/Scripts/build-macos.sh" "$APP"
fi

BUNDLE_ID="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$APP/Contents/Info.plist")"
PREFIX="$(sed -n 's/^VRMCAST_BUNDLE_PREFIX *= *//p' "$SPIKE/Config/Signing.xcconfig" | tr -d ' ')"
echo "App: $APP ($BUNDLE_ID), app group prefix: $PREFIX"

DERIVED="$SPIKE/build-embed"
xcodebuild \
  -project "$SPIKE/VRMCastCameraSpike.xcodeproj" \
  -target VRMCastCameraExtension \
  -configuration Release \
  -derivedDataPath "$DERIVED" \
  DEVELOPMENT_TEAM="$DEVELOPMENT_TEAM" \
  VRMCAST_HOST_BUNDLE_ID="$BUNDLE_ID" \
  build | tail -n 5
EXT="$DERIVED/Build/Products/Release/VRMCastCameraExtension.systemextension"
[[ -d "$EXT" ]] || { echo "extension not built at $EXT" >&2; exit 1; }

DEST="$APP/Contents/Library/SystemExtensions"
rm -rf "$DEST"
mkdir -p "$DEST"
cp -R "$EXT" "$DEST/"

ENTITLEMENTS="$(mktemp -t vrmcast-entitlements).plist"
cat > "$ENTITLEMENTS" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>com.apple.developer.system-extension.install</key>
	<true/>
	<key>com.apple.security.application-groups</key>
	<array>
		<string>$DEVELOPMENT_TEAM.$PREFIX</string>
	</array>
	<key>com.apple.security.device.camera</key>
	<true/>
	<key>com.apple.security.device.audio-input</key>
	<true/>
	<key>com.apple.security.cs.disable-library-validation</key>
	<true/>
</dict>
</plist>
PLIST

echo "Signing ..."
codesign --force --options runtime --timestamp --sign "$SIGN_IDENTITY" "$DEST/VRMCastCameraExtension.systemextension"
# Unity apps ship many dylibs/bundles; sign them first, then the app with its entitlements.
find "$APP/Contents" \( -name "*.dylib" -o -name "*.bundle" \) -not -path "*/SystemExtensions/*" -print0 | while IFS= read -r -d '' item; do
  codesign --force --options runtime --timestamp --sign "$SIGN_IDENTITY" "$item"
done
codesign --force --options runtime --timestamp --entitlements "$ENTITLEMENTS" --sign "$SIGN_IDENTITY" "$APP"
codesign --verify --deep --strict "$APP"
rm -f "$ENTITLEMENTS"
echo "Packaged: $APP with $(basename "$EXT") ($BUNDLE_ID.Camera)"

if [[ "$INSTALL" == 1 ]]; then
  rm -rf "/Applications/$(basename "$APP")"
  cp -R "$APP" /Applications/
  echo "Installed to /Applications. Launch it and press Install / Enable Virtual Camera."
fi
