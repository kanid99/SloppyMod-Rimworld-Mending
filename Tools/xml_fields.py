#!/usr/bin/env python3
"""Checks every XML tag this mod's defs use is a field the game actually has.

A def that sets a field one game version lacks loads fine on the other and
throws an XML error on this one - and compiling an assembly per version never
notices, because the defs are shared. This compares the tags against the field
list of the version's own Assembly-CSharp (and this mod's assembly, for its
comps), read from an ikdasm dump.

Usage: xml_fields.py <label> <mod root> <version folder or ""> <il dump>...
Called by check_api.sh once per supported version.
"""

import glob
import os
import re
import sys
import xml.etree.ElementTree as ET

label, root, version_folder = sys.argv[1], sys.argv[2], sys.argv[3]
dumps = sys.argv[4:]

# Instance fields of any access: the XML loader fills private ones too
# (vanilla's own defs set canOverlapZones, which is private). Constants and
# statics are never read from XML.
FIELD = re.compile(r"\s*\.field\s+(?!.*\b(?:literal|static)\b).*?\s([\w']+)\s*$")
CLASS = re.compile(r"^\s*\.class\s+.*?([\w.]+)\s*$")
fields = set()
classes = set()
for dump in dumps:
    with open(dump, encoding="utf-8", errors="replace") as handle:
        for line in handle:
            m = FIELD.match(line.rstrip())
            if m:
                fields.add(m.group(1).strip("'"))
            m = CLASS.match(line.rstrip())
            if m:
                classes.add(m.group(1))
                classes.add(m.group(1).split(".")[-1])

folders = [os.path.join(root, "Defs"), os.path.join(root, "Patches")]
if version_folder:
    folders += [os.path.join(root, version_folder, "Defs"), os.path.join(root, version_folder, "Patches")]

# Patch plumbing, and RimWorld's shorthand lists, whose children are named
# after defs rather than fields: <Steel>50</Steel> in a cost list,
# <Chemfuel>10</Chemfuel> in a recipe's products, <ThingDef>X</ThingDef> in
# description hyperlinks.
PLUMBING = {"Operation", "operations", "xpath", "value", "mods", "match", "nomatch",
            "success", "attribute", "order", "min", "max", "li"}
KEYED = ("costList", "statBases", "stuffCategories", "products", "descriptionHyperlinks",
         "statOffsets", "statFactors", "equippedStatOffsets", "butcherProducts",
         "smeltProducts", "killedLeavings", "leaveResourcesWhenKilled", "skillGains",
         "costListForDifficulty")

used = {}
for folder in folders:
    for path in glob.glob(os.path.join(folder, "**", "*.xml"), recursive=True):
        tree = ET.parse(path).getroot()
        keyed = set()
        for tag in KEYED:
            for node in tree.iter(tag):
                keyed |= {id(child) for child in node}
        # Anything inside a class neither the game nor this mod defines - an
        # optional patch filling in another mod's extension - has fields only
        # that mod could vouch for, so it is not checked here.
        for node in tree.iter():
            klass = node.get("Class")
            if klass and klass not in classes and klass.split(".")[-1] not in classes:
                keyed |= {id(d) for d in node.iter() if d is not node}
        for top in tree:
            for node in top.iter():
                if node is top or node.tag in PLUMBING or id(node) in keyed:
                    continue
                used.setdefault(node.tag, os.path.relpath(path, root))

missing = sorted(tag for tag in used if tag not in fields)
for tag in missing:
    print("  NOT A FIELD IN %s: <%s> (first used in %s)" % (label, tag, used[tag]))
print("  XML fields: %d tags checked, %s" % (len(used), "ok" if not missing else "%d missing" % len(missing)))
sys.exit(1 if missing else 0)
