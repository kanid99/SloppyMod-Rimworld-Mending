#!/usr/bin/env bash
#
# Builds a release zip that says what it is.
#
# A zip with nothing inside recording which commit it came from makes "is this
# the build with X in it?" unanswerable except by guessing - which has cost real
# time. This stamps the commit, the date and a monotonic build number into
# About.xml, so the mod list in game shows exactly which build is installed, and
# BUILD.txt inside the folder repeats it for grepping.
#
# It also refuses to ship a zip whose contents contradict the mod's claims: see
# the `require` and `forbid` keys in Tools/modtool.conf; `omit` lists repo-only
# paths to leave out.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

NAME="$(python3 Tools/modtool.py name)"
FOLDER="$(python3 Tools/modtool.py folder)"
ASSEMBLY="$(python3 Tools/modtool.py assembly)"
OUT="${1:-$ROOT/$FOLDER.zip}"

if [ -n "$(git status --porcelain)" ]; then
  echo "refusing to package: the working tree has uncommitted changes, so the" >&2
  echo "stamped commit would not describe what is actually in the zip." >&2
  git status --short >&2
  exit 1
fi

# The build number is the commit count, which a shallow clone does not have.
# Packaging from one would stamp a number far below the last release and make
# versions appear to go backwards, so refuse rather than quietly mislabel.
if [ "$(git rev-parse --is-shallow-repository)" = "true" ]; then
  echo "refusing to package: this is a shallow clone, so the commit count the" >&2
  echo "build number comes from is not the real one. Run:" >&2
  echo "  git fetch --unshallow   (or: git fetch --depth=100000)" >&2
  exit 1
fi

SHA="$(git rev-parse --short HEAD)"
DATE="$(git log -1 --format=%cd --date=format:%Y-%m-%d)"
BUILD="$(git rev-list --count HEAD)"
SERIES="$(python3 Tools/modtool.py series 2>/dev/null | head -1)"
VERSION="${SERIES:-0.9}.$BUILD"

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
mkdir -p "$STAGE/$FOLDER"
git archive HEAD | tar -x -C "$STAGE/$FOLDER"
rm -rf "$STAGE/$FOLDER/Source" "$STAGE/$FOLDER/Tools" "$STAGE/$FOLDER/.gitignore"

# Repo-only paths listed under `omit` in modtool.conf - Steam page art, docs -
# are kept in git but are no use inside the game's Mods folder.
while read -r extra; do
  [ -n "$extra" ] || continue
  rm -rf "${STAGE:?}/$FOLDER/$extra"
done < <(python3 Tools/modtool.py omit)

# The assemblies are build output; some repos commit them, some ignore them.
# Either way what ships has to be what is on disk right now.
while read -r outdir _ _; do
  [ -n "$outdir" ] || continue
  if [ -f "$ROOT/$outdir/$ASSEMBLY.dll" ]; then
    mkdir -p "$STAGE/$FOLDER/$outdir"
    cp "$ROOT/$outdir/$ASSEMBLY.dll" "$STAGE/$FOLDER/$outdir/"
  fi
done < <(python3 Tools/modtool.py build)

python3 - "$STAGE/$FOLDER/About/About.xml" "$VERSION" "$DATE" "$SHA" <<'PY'
import re
import sys
path, version, date, sha = sys.argv[1:5]
s = open(path, encoding="utf-8").read()
stamp = "%s (%s, %s)" % (version, date, sha)
if "<modVersion>" in s:
    s = re.sub(r"<modVersion>.*?</modVersion>",
               "<modVersion>%s</modVersion>" % stamp, s, flags=re.S)
else:
    s = s.replace("</packageId>", "</packageId>\n  <modVersion>%s</modVersion>" % stamp, 1)
# Repeat it at the top of the description, which is what the mod info panel
# shows - replacing the plain "Build x.y" line Tools/stamp_version.py leaves
# there for installs straight from GitHub, rather than adding a second one.
line = "Build %s\n\n" % stamp
if re.search(r"<description>Build [^\n<]*\n\n", s):
    s = re.sub(r"(<description>)Build [^\n<]*\n\n", lambda m: m.group(1) + line, s, count=1)
else:
    s = s.replace("<description>", "<description>" + line, 1)
open(path, "w", encoding="utf-8").write(s)
PY

cat > "$STAGE/$FOLDER/BUILD.txt" <<TXT
$NAME
version : $VERSION
built   : $DATE
commit  : $SHA
TXT

rm -f "$OUT"
(cd "$STAGE" && zip -qr "$OUT" "$FOLDER")

# --- what shipped, checked rather than assumed ------------------------------
fail=0

# A mod that claims to add no research, or no anything, should not quietly
# start shipping one because a stale def file survived in the tree.
while read -r deftype; do
  [ -n "$deftype" ] || continue
  if grep -rl "$deftype>" "$STAGE/$FOLDER/Defs" >/dev/null 2>&1; then
    echo "FAIL: this mod claims to add no $deftype, but the zip defines some:" >&2
    grep -rl "$deftype>" "$STAGE/$FOLDER/Defs" >&2
    fail=1
  fi
done < <(python3 Tools/modtool.py forbid)

while read -r want; do
  [ -n "$want" ] || continue
  if [ ! -e "$STAGE/$FOLDER/$want" ]; then
    echo "FAIL: zip is missing $want" >&2
    fail=1
  fi
done < <(python3 Tools/modtool.py require)

while read -r outdir _ _; do
  [ -n "$outdir" ] || continue
  if [ ! -e "$STAGE/$FOLDER/$outdir/$ASSEMBLY.dll" ]; then
    echo "FAIL: zip is missing $outdir/$ASSEMBLY.dll - run Tools/build.sh" >&2
    fail=1
  fi
done < <(python3 Tools/modtool.py build)

for leak in Source Tools; do
  if [ -e "$STAGE/$FOLDER/$leak" ]; then
    echo "FAIL: $leak/ leaked into the zip" >&2
    fail=1
  fi
done
[ "$fail" -eq 0 ] || { rm -f "$OUT"; exit 1; }

echo "$OUT"
echo "  version $VERSION  ($DATE, $SHA)"
echo "  $(unzip -l "$OUT" | tail -1 | awk '{print $2}') files"
