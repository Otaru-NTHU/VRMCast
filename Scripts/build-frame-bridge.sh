#!/usr/bin/env bash
# Compiles the Unity native plugin that feeds frames to the VRM Live Camera extension and drives its
# activation. Output: Assets/Plugins/macOS/VRMCastFrameBridge.bundle (Apple Silicon). Needs Xcode command
# line tools (clang). Re-run after editing Native/macOS/FrameBridge/VRMCastFrameBridge.mm.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="$REPO_ROOT/Native/macOS/FrameBridge/VRMCastFrameBridge.mm"
BUNDLE="$REPO_ROOT/Assets/Plugins/macOS/VRMCastFrameBridge.bundle"
BIN="$BUNDLE/Contents/MacOS/VRMCastFrameBridge"

if ! command -v clang++ >/dev/null; then
  echo "clang++ not found. Install the Xcode command line tools: xcode-select --install" >&2
  exit 1
fi

mkdir -p "$BUNDLE/Contents/MacOS"
cat > "$BUNDLE/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleExecutable</key><string>VRMCastFrameBridge</string>
	<key>CFBundleIdentifier</key><string>tech.hilight.vrmcast.framebridge</string>
	<key>CFBundleName</key><string>VRMCastFrameBridge</string>
	<key>CFBundlePackageType</key><string>BNDL</string>
	<key>CFBundleShortVersionString</key><string>0.1.0</string>
	<key>CFBundleVersion</key><string>1</string>
	<key>LSMinimumSystemVersion</key><string>13.0</string>
</dict>
</plist>
PLIST

echo "Compiling $SRC ..."
clang++ -std=c++17 -fobjc-arc -O2 -arch arm64 -mmacosx-version-min=13.0 \
  -bundle -o "$BIN" "$SRC" \
  -framework Foundation -framework CoreMedia -framework CoreMediaIO -framework CoreVideo -framework SystemExtensions
codesign --force --sign - "$BUNDLE" >/dev/null 2>&1 || true
echo "Built: $BUNDLE"
echo "Unity picks it up on the next asset refresh (Plugins/macOS is macOS-only by default)."
