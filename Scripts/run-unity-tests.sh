#!/usr/bin/env bash
# Runs the EditMode tests inside the Unity Test Runner in batch mode (requires Unity).
# For a Unity-free run of the same Core tests use Scripts/run-core-tests.sh.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="$(sed -n 's/^m_EditorVersion: //p' "$REPO_ROOT/ProjectSettings/ProjectVersion.txt")"
UNITY="${UNITY_PATH:-/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity}"

if [[ ! -x "$UNITY" ]]; then
  echo "Unity $VERSION not found at $UNITY. Install it with Unity Hub or set UNITY_PATH." >&2
  exit 1
fi

mkdir -p "$REPO_ROOT/Logs"
"$UNITY" -batchmode \
  -projectPath "$REPO_ROOT" \
  -runTests -testPlatform EditMode \
  -testResults "$REPO_ROOT/Logs/editmode-results.xml" \
  -logFile "$REPO_ROOT/Logs/editmode-tests.log"
echo "Results: Logs/editmode-results.xml"
