"""Summarise Il2CppInterop proxy types from an ilspycmd decompile.

MelonLoader's Il2CppAssemblies are interop *proxies*: every field becomes an
unsafe property backed by `il2cpp_field_get_offset`, and every method becomes a
trampoline. There are no real method bodies, so reading the decompiled source
directly is mostly noise -- 200 lines of pointer arithmetic to express "this
type has 9 fields".

This pulls out the part that matters: base class, the real IL2CPP
namespace/name, fields with their types, and method signatures. It is the
WHAT THE CAR? equivalent of reading golf's dump.cs.

stdlib only, so it runs without an Archipelago checkout or any package install.

Usage (from the repo root):
    python tools/typesummary.py <decomp-root> <TypeName> [TypeName ...]
    python tools/typesummary.py <decomp-root> --grep <regex>
"""

import os
import re
import sys

# `public unsafe List<ItemData> items` / `public unsafe string name`
_FIELD = re.compile(r"^\tpublic unsafe (?P<type>.+?) (?P<name>\w+)$")
# `public unsafe void SetState(bool on)` -- interop methods carry an attribute
# block, so we match on the signature line following `[CallerCount(...)]`.
_METHOD = re.compile(r"^\tpublic unsafe (?P<sig>[\w.<>\[\], ?]+ \w+\(.*\))$")
_CLASS = re.compile(r"^public (?:sealed )?(?:abstract )?class (?P<name>\w+)(?: : (?P<base>.+))?$")
# Enum members are emitted either as `Bronze,` (implicit, sequential from 0) or
# as `DOOR_OPEN = 1,` when the source assigned values. Match both.
_ENUMVAL = re.compile(r"^\t(?P<name>\w+)(?: = (?P<val>-?\d+))?,?$")
# IL2CPP.GetIl2CppClass("Speed.dll", "Speed.Overworld", "IslandDef")
_NATIVE = re.compile(r'GetIl2CppClass\("(?P<asm>[^"]*)", "(?P<ns>[^"]*)", "(?P<name>[^"]*)"\)')


def find_files(root):
    """Map bare type name -> source path, for every .cs under root."""
    index = {}
    for dirpath, _dirnames, filenames in os.walk(root):
        for f in filenames:
            if f.endswith(".cs"):
                index.setdefault(f[:-3], []).append(os.path.join(dirpath, f))
    return index


def summarise(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        lines = f.read().splitlines()

    out = []
    namespace = next((l[10:].rstrip(";") for l in lines if l.startswith("namespace ")), "?")

    native = next((m for m in (_NATIVE.search(l) for l in lines) if m), None)
    is_enum = any(l.startswith("public enum ") for l in lines)

    header = None
    for line in lines:
        m = _CLASS.match(line)
        if m:
            header = m.group("name") + (f"  :  {m.group('base')}" if m.group("base") else "")
            break
    if header is None and is_enum:
        header = next(l[len("public enum "):] for l in lines if l.startswith("public enum "))

    out.append(f"### {namespace}.{header or '?'}")
    if native:
        out.append(f"    IL2CPP: {native.group('asm')} :: {native.group('ns')}.{native.group('name')}")

    if is_enum:
        vals, implicit = [], 0
        for m in (_ENUMVAL.match(l) for l in lines):
            if not m:
                continue
            if m.group("val") is not None:
                implicit = int(m.group("val"))
            vals.append(f"{m.group('name')}={implicit}")
            implicit += 1
        out.append("  ENUM: " + (", ".join(vals) if vals else "(none)"))
        return "\n".join(out)

    fields, methods = [], []
    for i, line in enumerate(lines):
        fm = _FIELD.match(line)
        # A field's property block opens on the next line; a method's signature
        # ends in `)`. Distinguishing them this way avoids parsing the bodies.
        if fm and i + 1 < len(lines) and lines[i + 1].strip() == "{":
            fields.append(f"{fm.group('type')} {fm.group('name')}")
            continue
        mm = _METHOD.match(line)
        if mm and "(" in mm.group("sig"):
            methods.append(mm.group("sig"))

    out.append("  FIELDS:")
    out.extend(f"    {f}" for f in fields) if fields else out.append("    (none)")
    out.append("  METHODS:")
    seen = set()
    for m in methods:
        if m not in seen and not m.startswith("void .ctor"):
            seen.add(m)
            out.append(f"    {m}")
    if not seen:
        out.append("    (none)")
    return "\n".join(out)


def main(argv):
    if len(argv) < 3:
        print(__doc__)
        return 1
    root, rest = argv[1], argv[2:]
    index = find_files(root)

    if rest[0] == "--grep":
        pattern = re.compile(rest[1], re.I)
        names = sorted(n for n in index if pattern.search(n))
        print(f"{len(names)} type(s) matching /{rest[1]}/:")
        for n in names:
            print(f"  {n}")
        return 0

    for name in rest:
        paths = index.get(name)
        if not paths:
            print(f"### {name}\n  NOT FOUND\n")
            continue
        for p in paths:
            print(summarise(p))
            print()
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
