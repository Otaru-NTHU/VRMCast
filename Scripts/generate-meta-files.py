#!/usr/bin/env python3
"""Creates missing Unity .meta files under Assets/ with deterministic GUIDs.

Unity generates .meta files itself, but a fresh clone opened on another machine would then get random
GUIDs and break the scene's serialized references. Run this after adding files outside the editor.
GUIDs are derived from the asset path so every machine agrees. Existing .meta files are never touched.
"""
import hashlib
import os
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
ASSETS = os.path.join(ROOT, "Assets")

# Assets whose GUIDs are referenced from hand-written YAML must keep these exact values.
PINNED = {
    "Assets/VRMCast/Scenes/Main.unity": "5a1c0b4c6f0d4e6f9b3a2c1d0e9f8a71",
    "Assets/VRMCast/Runtime/App/AppBootstrap.cs": "a1b2c3d4e5f60718293a4b5c6d7e8f01",
    "Assets/VRMCast/UI/Main.uxml": "b2c3d4e5f6071829a3b4c5d6e7f80102",
    "Assets/VRMCast/UI/Main.uss": "c3d4e5f60718293a4b5c6d7e8f900203",
    "Assets/VRMCast/UI/VRMCastRuntimeTheme.tss": "d4e5f60718293a4b5c6d7e8f90010304",
    "Assets/VRMCast/UI/VRMCastPanelSettings.asset": "e5f60718293a4b5c6d7e8f9001020405",
    "Assets/VRMCast/UI/BackgroundImage.mat": "f60718293a4b5c6d7e8f900102030506",
}

FOLDER = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

SCRIPT = """fileFormatVersion: 2
guid: {guid}
MonoImporter:
  externalObjects: {{}}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {{instanceID: 0}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

ASMDEF = """fileFormatVersion: 2
guid: {guid}
AssemblyDefinitionImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

NATIVE = """fileFormatVersion: 2
guid: {guid}
NativeFormatImporter:
  externalObjects: {{}}
  mainObjectFileID: {main}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

# Unity fills in the importer block for these from the extension; only the GUID matters.
MINIMAL = """fileFormatVersion: 2
guid: {guid}
"""


def guid_for(rel):
    if rel in PINNED:
        return PINNED[rel]
    return hashlib.md5(("vrmcast:" + rel).encode("utf-8")).hexdigest()


def template_for(rel, is_dir):
    if is_dir:
        return FOLDER
    ext = os.path.splitext(rel)[1].lower()
    if ext == ".cs":
        return SCRIPT
    if ext == ".asmdef":
        return ASMDEF
    if ext == ".mat":
        return NATIVE.replace("{main}", "2100000")
    if ext == ".asset":
        return NATIVE.replace("{main}", "11400000")
    if ext == ".unity":
        return MINIMAL
    return MINIMAL


def main():
    created = 0
    for dirpath, dirnames, filenames in os.walk(ASSETS):
        dirnames[:] = sorted(d for d in dirnames if not d.startswith("."))
        entries = [(d, True) for d in dirnames] + [(f, False) for f in sorted(filenames) if not f.endswith(".meta") and not f.startswith(".")]
        for name, is_dir in entries:
            full = os.path.join(dirpath, name)
            rel = os.path.relpath(full, ROOT).replace(os.sep, "/")
            meta = full + ".meta"
            if os.path.exists(meta):
                continue
            with open(meta, "w", newline="\n") as f:
                f.write(template_for(rel, is_dir).format(guid=guid_for(rel)))
            created += 1
    print(f"created {created} .meta files")
    return 0


if __name__ == "__main__":
    sys.exit(main())
