#!/usr/bin/env python3
"""Reads Tools/modtool.conf - the one file in this toolkit that differs per mod.

The rest of Tools/ is identical in every SloppyMod repository and should be
copied across wholesale when it changes. Everything mod-specific lives in the
config: what the mod is called, which namespace its C# uses, which assemblies
to build against which RimWorld versions, and what must be in a release zip.

Format is one `key = value` per line, `#` for comments. A key may repeat, and
repeated keys are read as a list in the order they appear.

As a command:  python3 Tools/modtool.py <key>   - prints the values, one a line
               python3 Tools/modtool.py --root   - prints the repository root
"""

import os
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
CONF = os.path.join(ROOT, "Tools", "modtool.conf")


def load(path=CONF):
    """key -> list of values, in file order."""
    conf = {}
    if not os.path.isfile(path):
        raise SystemExit("no config at %s - every repo using this toolkit needs one" % path)
    with open(path, encoding="utf-8") as handle:
        for number, line in enumerate(handle, 1):
            line = line.split("#", 1)[0].strip()
            if not line:
                continue
            if "=" not in line:
                raise SystemExit("%s line %d: expected 'key = value', got %r"
                                 % (path, number, line))
            key, value = line.split("=", 1)
            conf.setdefault(key.strip(), []).append(value.strip())
    return conf


def one(conf, key, default=None):
    """The single value for a key. Missing and required is a hard error."""
    values = conf.get(key)
    if not values:
        if default is None:
            raise SystemExit("Tools/modtool.conf is missing the required key '%s'" % key)
        return default
    if len(values) > 1:
        raise SystemExit("Tools/modtool.conf sets '%s' more than once" % key)
    return values[0]


def many(conf, key):
    return conf.get(key, [])


def builds(conf):
    """The `build` lines, as (output dir, ref version, [defines])."""
    parsed = []
    for line in many(conf, "build"):
        parts = line.split()
        if len(parts) < 2:
            raise SystemExit("build line needs '<output dir> <ref version> [defines]': %r" % line)
        parsed.append((parts[0], parts[1], parts[2:]))
    if not parsed:
        raise SystemExit("Tools/modtool.conf has no build lines")
    return parsed


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    if sys.argv[1] == "--root":
        print(ROOT)
    else:
        for value in many(load(), sys.argv[1]):
            print(value)
