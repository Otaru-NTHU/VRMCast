#!/usr/bin/env bash
# Builds the macOS (Apple Silicon) standalone player in batch mode.
#
# Usage: Scripts/build-macos.sh [output.app]
# Env:   UNITY_PATH  path to the Unity executable (defaults to the Hub install matching ProjectVersion.txt)
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUTPUT="${1:-$REPO_ROOT/Builds/macOS/VRMCast.app}"
VERSION="$(sed -n 's/^m_EditorVersion: //p' "$REPO_ROOT/ProjectSettings/ProjectVersion.txt")"
UNITY="${UNITY_PATH:-/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity}"

if [[ ! -x "$UNITY" ]]; then
  echo "Unity $VERSION not found at $UNITY. Install it with Unity Hub or set UNITY_PATH." >&2
  exit 1
fi

mkdir -p "$REPO_ROOT/Logs"
echo "Building $OUTPUT with Unity $VERSION ..."
"$UNITY" -batchmode -quit \
  -projectPath "$REPO_ROOT" \
  -buildTarget OSXUniversal \
  -executeMethod VRMCast.Editor.BuildScript.BuildMacOS \
  -buildPath "$OUTPUT" \
  -logFile "$REPO_ROOT/Logs/build-macos.log"
echo "Build finished: $OUTPUT (log: Logs/build-macos.log)"
