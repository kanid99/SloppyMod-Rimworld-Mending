#!/usr/bin/env python3
"""Stamps About/About.xml with the build number the next commit will carry.

The owner installs these mods straight from GitHub - RimSort clones the default
branch into the Mods folder and pulls it in place - so there is no release zip
to carry a version: About.xml as committed is what the game and RimSort show.
Run this right before every commit:

    python3 Tools/stamp_version.py           stamp for the commit about to be made
    python3 Tools/stamp_version.py --check   exit 1 if the stamp is stale

The build number is the commit count, the same number package.sh stamps into a
zip, so for the commit being made it is the current count plus one. It goes in
<modVersion> and as a "Build x.y" line at the top of the description, which is
what the mod info panel shows. Tools/validate.py runs the check.

Exit codes for --check: 0 current, 1 stale, 2 cannot tell (no git, or a
shallow clone whose commit count is not the real one).
"""

import os
import re
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import modtool  # noqa: E402

ABOUT = os.path.join(modtool.ROOT, "About", "About.xml")
BUILD_LINE = re.compile(r"(<description>)Build [^\n<]*\n\n")


def git(*args):
    return subprocess.run(["git", "-C", modtool.ROOT] + list(args),
                          capture_output=True, text=True, check=True).stdout.strip()


def build_numbers():
    """(commit count at HEAD, whether the working tree has changes)."""
    if git("rev-parse", "--is-shallow-repository") == "true":
        raise LookupError("shallow clone: the commit count is not the real one "
                          "(git fetch --unshallow)")
    count = int(git("rev-list", "--count", "HEAD"))
    dirty = bool(git("status", "--porcelain"))
    return count, dirty


def series():
    return modtool.one(modtool.load(), "series", "0.9")


def current_stamp(text):
    m = re.search(r"<modVersion>([^<]*)</modVersion>", text)
    return m.group(1).strip() if m else None


def stamp(text, version):
    if "<modVersion>" in text:
        text = re.sub(r"<modVersion>[^<]*</modVersion>",
                      "<modVersion>%s</modVersion>" % version, text, count=1)
    else:
        text = text.replace("</packageId>",
                            "</packageId>\n  <modVersion>%s</modVersion>" % version, 1)
    line = "Build %s\n\n" % version
    if BUILD_LINE.search(text):
        text = BUILD_LINE.sub(lambda m: m.group(1) + line, text, count=1)
    else:
        text = text.replace("<description>", "<description>" + line, 1)
    return text


def main():
    check = "--check" in sys.argv[1:]
    try:
        count, dirty = build_numbers()
    except (LookupError, subprocess.CalledProcessError, FileNotFoundError) as err:
        print("version stamp not checked: %s" % err)
        return 2

    with open(ABOUT, encoding="utf-8") as handle:
        text = handle.read()
    have = current_stamp(text)

    if check:
        # Uncommitted changes are about to become the next commit; a clean
        # tree is a commit already made.
        want = "%s.%d" % (series(), count + 1 if dirty else count)
        if have == want:
            print("About.xml is stamped %s" % have)
            return 0
        print("About.xml is stamped %s but this %s build %s: run python3 Tools/stamp_version.py"
              % (have or "with nothing", "commit will be" if dirty else "commit is", want))
        return 1

    want = "%s.%d" % (series(), count + 1)
    updated = stamp(text, want)
    if updated != text:
        with open(ABOUT, "w", encoding="utf-8") as handle:
            handle.write(updated)
    print("About.xml stamped %s" % want)
    return 0


if __name__ == "__main__":
    sys.exit(main())
