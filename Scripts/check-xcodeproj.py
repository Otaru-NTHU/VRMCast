#!/usr/bin/env python3
"""Structural check for hand-maintained Xcode project files (no Xcode needed).

Parses the OpenStep-format project.pbxproj, then verifies that every object reference resolves,
that every object has an `isa`, that build files point at file references, that build phases and
configuration lists belong to targets, and that every referenced source file exists on disk.

Usage: Scripts/check-xcodeproj.py <path/to/Foo.xcodeproj>
"""
import os
import re
import sys


class Parser:
    def __init__(self, text):
        self.text = text
        self.pos = 0

    def error(self, msg):
        line = self.text.count("\n", 0, self.pos) + 1
        raise SystemExit(f"parse error line {line}: {msg}")

    def skip(self):
        while self.pos < len(self.text):
            c = self.text[self.pos]
            if c.isspace():
                self.pos += 1
            elif self.text.startswith("//", self.pos):
                end = self.text.find("\n", self.pos)
                self.pos = len(self.text) if end < 0 else end
            elif self.text.startswith("/*", self.pos):
                end = self.text.find("*/", self.pos)
                if end < 0:
                    self.error("unterminated comment")
                self.pos = end + 2
            else:
                return

    def value(self):
        self.skip()
        if self.pos >= len(self.text):
            self.error("unexpected end")
        c = self.text[self.pos]
        if c == "{":
            return self.dict()
        if c == "(":
            return self.array()
        if c == '"':
            return self.quoted()
        m = re.match(r"[A-Za-z0-9_$./:@+-]+", self.text[self.pos:])
        if not m:
            self.error(f"unexpected character {c!r}")
        self.pos += m.end()
        return m.group(0)

    def quoted(self):
        self.pos += 1
        out = []
        while True:
            c = self.text[self.pos]
            if c == "\\":
                out.append(self.text[self.pos + 1])
                self.pos += 2
            elif c == '"':
                self.pos += 1
                return "".join(out)
            else:
                out.append(c)
                self.pos += 1

    def dict(self):
        self.pos += 1
        result = {}
        while True:
            self.skip()
            if self.text[self.pos] == "}":
                self.pos += 1
                return result
            key = self.value()
            self.skip()
            if self.text[self.pos] != "=":
                self.error(f"expected '=' after key {key}")
            self.pos += 1
            val = self.value()
            self.skip()
            if self.text[self.pos] != ";":
                self.error(f"expected ';' after value of {key}")
            self.pos += 1
            result[key] = val

    def array(self):
        self.pos += 1
        result = []
        while True:
            self.skip()
            if self.text[self.pos] == ")":
                self.pos += 1
                return result
            result.append(self.value())
            self.skip()
            if self.text[self.pos] == ",":
                self.pos += 1
            elif self.text[self.pos] != ")":
                self.error("expected ',' or ')' in array")


def walk(value):
    if isinstance(value, dict):
        for v in value.values():
            yield from walk(v)
    elif isinstance(value, list):
        for v in value:
            yield from walk(v)
    else:
        yield value


def main():
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    project_dir = sys.argv[1]
    path = os.path.join(project_dir, "project.pbxproj")
    with open(path, encoding="utf-8") as f:
        text = f.read()
    if not text.startswith("// !$*UTF8*$!"):
        raise SystemExit("missing UTF8 header")
    root = Parser(text).value()
    objects = root["objects"]
    problems = []
    id_re = re.compile(r"^[0-9A-F]{24}$")

    for oid, obj in objects.items():
        if not id_re.match(oid):
            problems.append(f"{oid}: not a 24-hex identifier")
        if "isa" not in obj:
            problems.append(f"{oid}: missing isa")

    def resolve(ref, where):
        if ref not in objects:
            problems.append(f"{where}: dangling reference {ref}")
            return None
        return objects[ref]

    if root.get("rootObject") not in objects:
        problems.append("rootObject does not resolve")
    for oid, obj in objects.items():
        for leaf in walk({k: v for k, v in obj.items() if k != "isa"}):
            if isinstance(leaf, str) and id_re.match(leaf) and leaf not in objects:
                problems.append(f"{oid} ({obj.get('isa')}): dangling reference {leaf}")

    base = os.path.dirname(os.path.abspath(project_dir))
    group_paths = {}

    def group_path(gid, prefix):
        group = objects[gid]
        p = os.path.join(prefix, group.get("path", "")) if group.get("path") else prefix
        for child in group.get("children", []):
            c = resolve(child, gid)
            if c is None:
                continue
            if c.get("isa") == "PBXGroup":
                group_path(child, p)
            elif c.get("isa") == "PBXFileReference" and c.get("sourceTree") == "<group>":
                group_paths[child] = os.path.join(p, c["path"])

    project = objects[root["rootObject"]]
    group_path(project["mainGroup"], base)
    for fid, fpath in group_paths.items():
        if not os.path.exists(fpath):
            problems.append(f"{fid}: file missing on disk: {os.path.relpath(fpath, base)}")

    for oid, obj in objects.items():
        isa = obj.get("isa")
        if isa == "PBXBuildFile":
            ref = resolve(obj.get("fileRef", ""), oid)
            if ref is not None and ref.get("isa") != "PBXFileReference":
                problems.append(f"{oid}: fileRef is not a PBXFileReference")
        if isa == "PBXNativeTarget":
            for phase in obj["buildPhases"]:
                p = resolve(phase, oid)
                if p is not None and not p["isa"].endswith("BuildPhase"):
                    problems.append(f"{oid}: {phase} is not a build phase")
            cl = resolve(obj["buildConfigurationList"], oid)
            if cl is not None and cl["isa"] != "XCConfigurationList":
                problems.append(f"{oid}: buildConfigurationList is not XCConfigurationList")
            for dep in obj.get("dependencies", []):
                d = resolve(dep, oid)
                if d is not None:
                    proxy = resolve(d["targetProxy"], dep)
                    if proxy is not None and proxy["remoteGlobalIDString"] != d["target"]:
                        problems.append(f"{dep}: proxy remoteGlobalIDString != target")
        if isa == "PBXSourcesBuildPhase":
            for bf in obj["files"]:
                b = resolve(bf, oid)
                if b is not None:
                    ref = objects.get(b.get("fileRef", ""), {})
                    if ref.get("lastKnownFileType") not in ("sourcecode.swift", "sourcecode.c.objc", "sourcecode.cpp.objcpp", "sourcecode.c.c", "sourcecode.cpp.cpp"):
                        problems.append(f"{oid}: {bf} compiles a non-source file {ref.get('path')}")

    # Every target that produces an app must embed every system-extension product it depends on.
    for oid, obj in objects.items():
        if obj.get("isa") == "PBXNativeTarget" and obj["productType"] == "com.apple.product-type.application":
            embedded = set()
            for phase in obj["buildPhases"]:
                p = objects[phase]
                if p["isa"] == "PBXCopyFilesBuildPhase" and p.get("dstSubfolderSpec") == "16":
                    for bf in p["files"]:
                        embedded.add(objects[bf]["fileRef"])
            for dep in obj.get("dependencies", []):
                target = objects[objects[dep]["target"]]
                if target["productType"] == "com.apple.product-type.system-extension" and target["productReference"] not in embedded:
                    problems.append(f"{oid}: system extension {target['name']} is a dependency but not embedded")

    schemes_dir = os.path.join(project_dir, "xcshareddata", "xcschemes")
    if os.path.isdir(schemes_dir):
        for name in os.listdir(schemes_dir):
            with open(os.path.join(schemes_dir, name), encoding="utf-8") as f:
                for ref in re.findall(r'BlueprintIdentifier = "([^"]+)"', f.read()):
                    if ref not in objects:
                        problems.append(f"scheme {name}: BlueprintIdentifier {ref} does not resolve")

    if problems:
        print("\n".join(problems))
        raise SystemExit(f"{len(problems)} problem(s) in {path}")
    print(f"OK: {len(objects)} objects, {len(group_paths)} files, "
          f"{sum(1 for o in objects.values() if o.get('isa') == 'PBXNativeTarget')} targets")


if __name__ == "__main__":
    main()
