#!/usr/bin/env bash
# Downloads the pinned MediaPipeUnityPlugin tarball into Packages/ (it is 290 MB and not committed).
#
# Packages/manifest.json already references "file:com.github.homuler.mediapipe-0.16.3.tgz"; Unity resolves it
# once this file exists. Run this once after cloning, before opening the project (or press Resolve in the
# Package Manager afterwards). Re-running is safe: an existing, verified file is kept.
set -euo pipefail

VERSION="0.16.3"
SHA256="cc3e77a219e0b99618ae3be64c31a566197deedc69c1e136acf52d65d7cf2e79"
FILE="com.github.homuler.mediapipe-${VERSION}.tgz"
URL="https://github.com/homuler/MediaPipeUnityPlugin/releases/download/v${VERSION}/${FILE}"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST="$REPO_ROOT/Packages/$FILE"

checksum() {
  if command -v shasum >/dev/null 2>&1; then shasum -a 256 "$1" | awk '{print $1}'
  else sha256sum "$1" | awk '{print $1}'; fi
}

if [[ -f "$DEST" ]] && [[ "$(checksum "$DEST")" == "$SHA256" ]]; then
  echo "ok       $FILE already present and verified"
  exit 0
fi

echo "Downloading MediaPipeUnityPlugin v${VERSION} (about 290 MB) ..."
TMP="$DEST.part"
curl -L --fail --progress-bar -o "$TMP" "$URL"
ACTUAL="$(checksum "$TMP")"
if [[ "$ACTUAL" != "$SHA256" ]]; then
  rm -f "$TMP"
  echo "ERROR    checksum mismatch for $FILE" >&2
  echo "         expected $SHA256" >&2
  echo "         actual   $ACTUAL" >&2
  exit 1
fi
mv "$TMP" "$DEST"
echo "ok       $FILE downloaded and verified"
echo "Next: open the project in Unity (or Package Manager > Resolve). The face tracking model ships inside the package."
