#!/usr/bin/env python3
"""Sanity-checks a mod's defs without launching RimWorld.

Catches the mistakes that are silent until runtime:
  * malformed XML
  * a recreation building that no JoyGiverDef lists, so pawns never use it
    (a joyKind on a ThingDef is only a label)
  * a texPath with no matching file, or a Graphic_Multi missing a rotation
  * a jobDef/thingDef/research reference this mod makes but never defines
  * an animation comp whose frame textures are missing
  * a def declaring a comp its parent also declares (def inheritance APPENDS
    list entries, so that silently gives the building two of them)
  * a [DefOf] field naming a def of this mod's that nothing defines - which
    throws on startup, before any of the mod loads
  * a vanilla def name that does not exist - research, joy kinds, stuff, items
    and so on - when pointed at a copy of the game's own defs:

        RIMWORLD_CORE_DEFS=".../RimWorld/Data/Core/Defs" python3 Tools/validate.py

    Without that variable this check is skipped, since the game's files cannot
    ship here. It is worth running before release: a research prerequisite that
    does not resolve takes the whole building down with it.
  * XML naming a C# class this mod's source does not define, or naming one at
    all when the assembly has not been built

Everything mod-specific comes from Tools/modtool.conf, so this file is the same
in every SloppyMod repository.

Usage: python3 Tools/validate.py
"""

import os
import re
import sys
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import modtool

ROOT = modtool.ROOT
CONF = modtool.load()
DEFS = os.path.join(ROOT, "Defs")
TEX = os.path.join(ROOT, "Textures")
PATCHES = os.path.join(ROOT, "Patches")
NAMESPACE = modtool.one(CONF, "namespace")
SRC = os.path.join(ROOT, modtool.one(CONF, "source"))
ASSEMBLY = modtool.one(CONF, "assembly")
ASSEMBLIES = [os.path.join(ROOT, outdir, ASSEMBLY + ".dll")
              for outdir, _, _ in modtool.builds(CONF)]
# Optional: defName prefixes this mod owns. With one set, a reference that
# looks like ours but matches nothing is a typo and is reported. Without one,
# such a reference is indistinguishable from a vanilla def and is only checked
# when RIMWORLD_CORE_DEFS points at the game's own defs.
PREFIXES = tuple(modtool.many(CONF, "prefix"))
FOREIGN = set(modtool.many(CONF, "foreign"))

# Graphic classes load a texPath in three different shapes, so checking one
# shape for all of them reports missing files that are not missing.
#
#   Graphic_Single      <path>.png
#   Graphic_Multi       <path>_north/_south/_east/_west.png - and only three of
#                       those need exist, because Graphic_Multi carries
#                       westFlipped/eastFlipped and mirrors one side onto the
#                       other when the file for it is absent.
#   Graphic_Collection  <path>/ as a FOLDER of variants, enumerated at load.
#                       Graphic_Random and Graphic_StackCount are both this.
#
# Anything else is a graphic class from another mod whose layout is not ours to
# guess, so those are only reported when neither shape is present at all.
COLLECTION_GRAPHICS = ("Graphic_Random", "Graphic_StackCount", "Graphic_Appearances")


def has_folder(base):
    return os.path.isdir(base) and any(f.endswith(".png") for f in os.listdir(base))


def texture_problem(klass, base):
    """What is wrong with this texture path, or None if nothing is."""
    if klass == "Graphic_Multi":
        have = {r: os.path.isfile("%s_%s.png" % (base, r))
                for r in ("north", "east", "south", "west")}
        missing = [r for r in ("north", "south") if not have[r]]
        if not have["east"] and not have["west"]:
            missing.append("east or west")
        return "Graphic_Multi missing " + ", ".join(missing) if missing else None
    if klass in COLLECTION_GRAPHICS:
        return None if has_folder(base) else "%s needs a folder of textures, and there is none" % klass
    if klass in (None, "", "Graphic_Single"):
        return None if os.path.isfile(base + ".png") else "no texture file"
    # A graphic class we do not know. Accept either shape.
    if os.path.isfile(base + ".png") or has_folder(base):
        return None
    return "no texture file or folder (graphic class %s)" % klass


problems = []
thing_defs = {}      # defName -> element (concrete buildings)
named = {}           # Name="..." -> element (including Abstract parents)
joy_givers = []
job_defs = set()
research_defs = set()
terrain_defs = {}
defs_by_type = {}    # "JobDef" -> {defName, ...}, for the DefOf check


def fail(msg):
    problems.append(msg)


def ours(name):
    """Does this name look like a def this mod owns?"""
    return bool(PREFIXES) and name.startswith(PREFIXES)


def xml_files(folder):
    for where, _, files in os.walk(folder):
        for filename in sorted(files):
            if filename.endswith(".xml"):
                yield os.path.join(where, filename)


def parsed(path):
    try:
        return ET.parse(path).getroot()
    except ET.ParseError as exc:
        fail("%s: malformed XML (%s)" % (os.path.relpath(path, ROOT), exc))
        return None


for path in xml_files(DEFS):
    root = parsed(path)
    if root is None:
        continue
    for node in root:
        name = node.findtext("defName")
        if name:
            defs_by_type.setdefault(node.tag, set()).add(name)
        if node.tag == "ThingDef":
            if node.get("Name"):
                named[node.get("Name")] = node
            if name:
                thing_defs[name] = node
        elif node.tag == "JoyGiverDef":
            joy_givers.append(node)
        elif node.tag == "JobDef" and name:
            job_defs.add(name)
        elif node.tag == "ResearchProjectDef" and name:
            research_defs.add(name)
        elif node.tag == "TerrainDef" and name:
            terrain_defs[name] = node

all_our_defs = set()
for names in defs_by_type.values():
    all_our_defs |= names

# --- terrain ----------------------------------------------------------------
# Terrain carries its texture in texturePath, not graphicData/texPath, so the
# building check below never looks at it.
for name, node in terrain_defs.items():
    tex = node.findtext("texturePath")
    if not tex:
        fail("TerrainDef %s has no texturePath" % name)
    elif not os.path.isfile(os.path.join(TEX, tex.replace("/", os.sep)) + ".png"):
        fail("TerrainDef %s: no texture file for %s" % (name, tex))

# --- every recreation building must be reachable through a JoyGiverDef ------
listed = {}
listed_jobs = {}     # building -> {jobDef: [givers]}
for giver in joy_givers:
    giver_name = giver.findtext("defName")
    for li in giver.findall("./thingDefs/li"):
        listed.setdefault(li.text, []).append(giver_name)
        listed_jobs.setdefault(li.text, {}).setdefault(giver.findtext("jobDef"), []).append(giver_name)
    job = giver.findtext("jobDef")
    if job and ours(job) and job not in job_defs:
        fail("JoyGiverDef %s points at undefined jobDef %s" % (giver_name, job))

for name, node in thing_defs.items():
    if node.find("./building/joyKind") is None:
        continue
    if name not in listed:
        fail("%s has a joyKind but no JoyGiverDef lists it - pawns would never "
             "use it" % name)
    else:
        # Several givers are fine when they hand out different jobs - one
        # colonist tells a story, the rest listen. Two giving the same job is
        # a copy-paste mistake, and doubles how often the building is chosen.
        for job, givers in sorted(listed_jobs[name].items()):
            if len(givers) > 1:
                fail("%s is listed by several JoyGiverDefs with the same jobDef %s: %s"
                     % (name, job, givers))

for name, givers in listed.items():
    if name not in thing_defs:
        fail("JoyGiverDef %s lists unknown ThingDef %s" % (givers[0], name))

# --- textures ---------------------------------------------------------------
for name, node in thing_defs.items():
    # graphicClass is often set once on an abstract parent, so walk up to it.
    tex = node.findtext("./graphicData/texPath")
    klass = node.findtext("./graphicData/graphicClass")
    parent = node.get("ParentName")
    while klass is None and parent in named:
        klass = named[parent].findtext("./graphicData/graphicClass")
        parent = named[parent].get("ParentName")
    if not tex:
        continue
    base = os.path.join(TEX, tex.replace("/", os.sep))
    problem = texture_problem(klass, base)
    if problem:
        fail("%s: %s for %s" % (name, problem, tex))

# --- internal references ----------------------------------------------------
for name, node in thing_defs.items():
    for li in node.findall("./researchPrerequisites/li"):
        if li.text and ours(li.text) and li.text not in research_defs:
            fail("%s requires undefined research %s" % (name, li.text))

for path in xml_files(DEFS):
    root = parsed(path)
    if root is None:
        continue
    for node in root.iter("prerequisites"):
        for li in node.findall("li"):
            if li.text and ours(li.text) and li.text not in research_defs:
                fail("undefined research prerequisite %s" % li.text)

# --- comps declared by both a def and its parent chain ----------------------
def comp_classes(node):
    return [li.get("Class") for li in node.findall("./comps/li") if li.get("Class")]


for name, node in thing_defs.items():
    own = comp_classes(node)
    parent = node.get("ParentName")
    chain = []
    while parent in named:
        chain.append((parent, comp_classes(named[parent])))
        parent = named[parent].get("ParentName")
    for ancestor, inherited in chain:
        clash = sorted(set(own) & set(inherited))
        if clash:
            fail("%s and its parent %s both declare %s - inheritance appends "
                 "list entries, so the building would get two of each"
                 % (name, ancestor, ", ".join(clash)))

# --- C# classes named from XML, and their animation frames ------------------
source_classes = set()
defof_fields = []        # (DefOf class, def type, defName)
if os.path.isdir(SRC):
    for filename in sorted(os.listdir(SRC)):
        if not filename.endswith(".cs"):
            continue
        with open(os.path.join(SRC, filename), encoding="utf-8") as handle:
            text = handle.read()
        for line in text.splitlines():
            stripped = line.strip()
            if stripped.startswith("public class ") or stripped.startswith("public abstract class "):
                source_classes.add(stripped.split("class ", 1)[1].split()[0].split(":")[0])
        # A [DefOf] field is looked up by its own name at startup. One naming a
        # def nothing defines throws before any of the mod loads, and the
        # message names the field rather than the file, which is a poor place
        # to start looking.
        for block in re.finditer(r"\[DefOf\][^{]*?class\s+(\w+)\s*\{(.*?)\n    \}",
                                 text, re.S):
            klass, body = block.group(1), block.group(2)
            for field in re.finditer(r"public\s+static\s+(\w+)\s+(\w+)\s*;", body):
                defof_fields.append((klass, field.group(1), field.group(2)))

for klass, def_type, def_name in defof_fields:
    known = defs_by_type.get(def_type, set())
    if def_name in known:
        continue
    if def_name in all_our_defs:
        fail("%s.%s is declared as a %s, but this mod defines %s as something else"
             % (klass, def_name, def_type, def_name))
    elif ours(def_name):
        fail("%s.%s names %s, which no %s in this mod defines - a [DefOf] field "
             "that does not resolve throws on startup"
             % (klass, def_name, def_name, def_type))

our_classes_used = set()
for path in xml_files(DEFS):
    root = parsed(path)
    if root is None:
        continue
    for node in root.iter():
        for value in [node.get("Class"), node.text]:
            if value and value.strip().startswith(NAMESPACE + "."):
                our_classes_used.add(value.strip().split(".", 1)[1])
    # Every frame strip referenced anywhere must be complete on disk,
    # play loops and idle attract loops alike.
    for node in root.iter():
        for path_tag, count_tag in (("framePath", "frameCount"),
                                    ("idleFramePath", "idleFrameCount")):
            frame_path = node.findtext(path_tag)
            if not frame_path:
                continue
            count_text = node.findtext(count_tag)
            count = int(count_text) if count_text else 0
            if count < 1:
                fail("%s: %s must be at least 1" % (frame_path, count_tag))
                continue
            # perFacing strips are one per facing, the facing's name appended.
            per_facing = (node.findtext("perFacing") or "").strip().lower() == "true"
            for strip in ([frame_path + f for f in ("North", "East", "South", "West")]
                          if per_facing else [frame_path]):
                missing = [i for i in range(count)
                           if not os.path.isfile(os.path.join(TEX, strip.replace("/", os.sep)) + "_%d.png" % i)]
                if missing:
                    fail("%s: missing frame textures %s"
                         % (strip, ", ".join(str(i) for i in missing)))

# A texture path outside graphicData belongs to a comp - an aquarium's fish, a
# set of cornhole sacks - and the building check above never looks at those.
# Any tag ending in texPath counts, so a comp inventing its own name for the
# field is still covered.
for path in xml_files(DEFS):
    root = parsed(path)
    if root is None:
        continue
    for node in root.iter():
        if node.tag == "graphicData":
            continue
        for child in node:
            if not child.tag.lower().endswith("texpath") or not child.text:
                continue
            comp_base = os.path.join(TEX, child.text.replace("/", os.sep))
            if not os.path.isfile(comp_base + ".png") and not has_folder(comp_base):
                fail("no texture file for %s (<%s>)" % (child.text, child.tag))

for klass in sorted(our_classes_used):
    if klass not in source_classes:
        fail("XML names %s.%s but no C# source defines it" % (NAMESPACE, klass))
built = [a for a in ASSEMBLIES if os.path.isfile(a)]
if our_classes_used and len(built) < len(ASSEMBLIES):
    missing = [os.path.relpath(a, ROOT) for a in ASSEMBLIES if not os.path.isfile(a)]
    fail("XML names C# classes but these assemblies are missing: %s - run "
         "Tools/build.sh (defs naming a missing class will not load)"
         % ", ".join(missing))

# --- sounds -----------------------------------------------------------------
# A SoundDef names its clips by path under Sounds/, and a wrong path is silent
# in every sense: the sound simply never plays. Only paths under a folder this
# mod ships are checked - a SoundDef may perfectly well point at one of the
# game's own clips, which live inside the game, not here.
SOUNDS = os.path.join(ROOT, "Sounds")
AUDIO = (".wav", ".ogg", ".mp3")
sound_clips = 0
if os.path.isdir(SOUNDS):
    ours_top = set(os.listdir(SOUNDS))
    for path in xml_files(DEFS):
        root = parsed(path)
        if root is None:
            continue
        for grain in root.iter("li"):
            klass = grain.get("Class") or ""
            for tag, is_folder in (("clipPath", False), ("folderPath", True)):
                clip = grain.findtext(tag)
                if not clip or klass not in ("AudioGrain_Clip", "AudioGrain_Folder"):
                    continue
                if clip.split("/")[0] not in ours_top:
                    continue                    # the game's own, or another mod's
                sound_clips += 1
                base = os.path.join(SOUNDS, clip.replace("/", os.sep))
                if is_folder:
                    found = os.path.isdir(base) and any(f.endswith(AUDIO) for f in os.listdir(base))
                else:
                    found = any(os.path.isfile(base + ext) for ext in AUDIO)
                if not found:
                    fail("%s: no sound %s for %s" % (os.path.relpath(path, ROOT),
                                                   "folder" if is_folder else "file", clip))

# --- references to this mod's own defs ----------------------------------------
# Any value that looks like one of this mod's defNames has to be one. RimWorld
# does report an unresolved cross-reference, but only once the game is loading
# the mod; this reports it now, and covers every tag at once - a sound on a
# comp, a job on a giver, a thought on a reaction - rather than one check per
# kind of field.
if PREFIXES:
    defined = all_our_defs | set(named)
    own_ref = re.compile(r"^(?:%s)[A-Za-z0-9_]+$" % "|".join(re.escape(p) for p in PREFIXES))
    for path in xml_files(DEFS):
        root = parsed(path)
        if root is None:
            continue
        for node in root.iter():
            if len(node) or node.tag in ("defName",) or not node.text:
                continue
            value = node.text.strip()
            if own_ref.match(value) and value not in defined:
                fail("%s: <%s> names %s, which this mod does not define"
                     % (os.path.relpath(path, ROOT), node.tag, value))

# --- patch targets ----------------------------------------------------------
# A patch only runs when its gate matches, so an xpath left pointing at a def
# this mod has since renamed fails silently and forever. Every one of our own
# defNames named in a patch has to exist.
patch_targets = 0
if os.path.isdir(PATCHES) and PREFIXES:
    known = all_our_defs | set(named)
    pattern = re.compile(r'defName\s*=\s*"((?:%s)[A-Za-z0-9_]*)"'
                         % "|".join(re.escape(p) for p in PREFIXES))
    for path in xml_files(PATCHES):
        root = parsed(path)
        if root is None:
            continue
        for node in root.iter("xpath"):
            if not node.text:
                continue
            for name in pattern.findall(node.text):
                patch_targets += 1
                if name not in known:
                    fail("%s: xpath targets %s, which no def defines"
                         % (os.path.relpath(path, ROOT), name))

# --- vanilla def names, when the game's own defs are available -------------
CORE_DEFS = os.environ.get("RIMWORLD_CORE_DEFS")
# XML tags whose value names a def the base game owns.
VANILLA_REFS = {
    "researchPrerequisites", "designationCategory", "thingCategories",
    "stuffCategories", "joyKind", "joySkill", "performanceSkill",
    "performerSkill", "taleOnCompletion", "requiredCapacities", "minifiedDef",
    "workType", "constructEffect",
}

if CORE_DEFS:
    if not os.path.isdir(CORE_DEFS):
        fail("RIMWORLD_CORE_DEFS is not a directory: %s" % CORE_DEFS)
    else:
        core_names = set()
        for folder, _, files in os.walk(CORE_DEFS):
            for filename in files:
                if not filename.endswith(".xml"):
                    continue
                try:
                    core_root = ET.parse(os.path.join(folder, filename)).getroot()
                except ET.ParseError:
                    continue
                for node in core_root.iter():
                    if node.tag.endswith("Def") and len(node):
                        name = node.findtext("defName")
                        if name:
                            core_names.add(name)

        checked_refs = 0
        for folder, _, files in os.walk(ROOT):
            if os.path.basename(folder) in (".git", "Source", "Tools", "promo", "docs"):
                continue
            for filename in files:
                if not filename.endswith(".xml") or filename == "LoadFolders.xml":
                    continue
                path = os.path.join(folder, filename)
                try:
                    root = ET.parse(path).getroot()
                except ET.ParseError:
                    continue
                for node in root.iter():
                    values = []
                    if node.tag in VANILLA_REFS:
                        values = ([li.text for li in node.findall("li")] if len(node)
                                  else ([node.text] if node.text else []))
                    elif node.tag == "costList":
                        values = [child.tag for child in node]
                    for value in values:
                        if not value:
                            continue
                        value = value.strip()
                        # our own defs, and defs owned by a mod we only patch
                        if value in all_our_defs or ours(value) or value in FOREIGN:
                            continue
                        checked_refs += 1
                        if value not in core_names:
                            fail("%s names '%s' (<%s>), which is not a def in %s"
                                 % (filename, value, node.tag, CORE_DEFS))
        print("%d vanilla def references checked against %s"
              % (checked_refs, os.path.basename(CORE_DEFS.rstrip("/"))))
else:
    print("vanilla def names not checked (set RIMWORLD_CORE_DEFS to a copy of "
          "the game's Defs folder to enable)")

# A def type the mod says it does not add. package.sh checks the zip; checking
# here catches it at the point the file is added rather than at release.
for deftype in modtool.many(CONF, "forbid"):
    present = sorted(defs_by_type.get(deftype, set()))
    if present:
        fail("this mod adds no %s by design, but defines %d: %s"
             % (deftype, len(present), ", ".join(present)))
    else:
        print("no %s, as intended" % deftype)

if not PREFIXES:
    print("no defName prefix configured: references that look like this mod's "
          "own are not cross-checked (see Tools/modtool.conf)")

print("%d C# classes referenced from XML, %d [DefOf] fields, "
      "%d/%d version assemblies built"
      % (len(our_classes_used), len(defof_fields), len(built), len(ASSEMBLIES)))
print(", ".join("%d %s%s" % (len(names), tag, "" if len(names) == 1 else "s")
                for tag, names in sorted(defs_by_type.items()))
      + ", %d patch targets, %d sound clips" % (patch_targets, sound_clips))
if problems:
    print("\nFAILED:")
    for line in problems:
        print("  - " + line)
    sys.exit(1)
print("All checks passed.")
