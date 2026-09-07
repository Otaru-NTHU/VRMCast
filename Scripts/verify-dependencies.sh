#!/usr/bin/env bash
# Checks that third-party dependencies are pinned to the versions recorded in THIRD_PARTY_NOTICES.md (PRD 39).
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MANIFEST="$REPO_ROOT/Packages/manifest.json"
EXPECTED_UNIVRM="v0.131.2"
EXPECTED_UNITY_MAJOR="6000.3"
status=0

for pkg in com.vrmc.gltf com.vrmc.univrm com.vrmc.vrm; do
  line="$(grep "\"$pkg\"" "$MANIFEST" || true)"
  if [[ -z "$line" ]]; then
    echo "MISSING  $pkg is not in Packages/manifest.json"; status=1; continue
  fi
  if [[ "$line" != *"#$EXPECTED_UNIVRM"* ]]; then
    echo "MISMATCH $pkg is not pinned to $EXPECTED_UNIVRM: $line"; status=1
  else
    echo "ok       $pkg @ $EXPECTED_UNIVRM"
  fi
done

if grep -q '"com.vrmc.vrmshaders"' "$MANIFEST"; then
  echo "STALE    com.vrmc.vrmshaders is obsolete since UniVRM v0.125; remove it"; status=1
fi

EXPECTED_MEDIAPIPE="0.16.3"
mp_line="$(grep '"com.github.homuler.mediapipe"' "$MANIFEST" || true)"
if [[ "$mp_line" != *"com.github.homuler.mediapipe-$EXPECTED_MEDIAPIPE.tgz"* ]]; then
  echo "MISMATCH com.github.homuler.mediapipe is not pinned to $EXPECTED_MEDIAPIPE: $mp_line"; status=1
else
  echo "ok       com.github.homuler.mediapipe @ $EXPECTED_MEDIAPIPE (tarball)"
fi
if [[ ! -f "$REPO_ROOT/Packages/com.github.homuler.mediapipe-$EXPECTED_MEDIAPIPE.tgz" ]]; then
  echo "MISSING  Packages/com.github.homuler.mediapipe-$EXPECTED_MEDIAPIPE.tgz — run Scripts/setup-mediapipe.sh"; status=1
fi

version="$(sed -n 's/^m_EditorVersion: //p' "$REPO_ROOT/ProjectSettings/ProjectVersion.txt")"
if [[ "$version" == "$EXPECTED_UNITY_MAJOR".* ]]; then
  echo "ok       Unity $version"
else
  echo "MISMATCH Unity $version is not a $EXPECTED_UNITY_MAJOR LTS release"; status=1
fi

for forbidden in electron tauri puppeteer node-; do
  if grep -qi "\"$forbidden" "$MANIFEST"; then
    echo "FORBIDDEN dependency '$forbidden' found in manifest (PRD 41)"; status=1
  fi
done

exit $status
