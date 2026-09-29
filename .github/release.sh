#!/usr/bin/env bash
# Publishes a GitHub Release for the build at HEAD, so RimSort shows it.
#
# RimSort's GitHub Mods panel reads a mod's "Latest Version" from the newest
# GitHub Release and installs that release's single .zip asset; with no
# releases it shows only "HEAD" and a blank version. This script:
#
#   1. numbers the build series.<commit count> (the same number About.xml is
#      stamped with, series taken from its <modVersion>),
#   2. compiles the assemblies from source against Krafs' published RimWorld
#      reference assemblies, so the zip always matches the commit,
#   3. copies the folders RimWorld loads into <FOLDER>/, stamps its About.xml
#      and zips it,
#   4. publishes release v<version> with the zip attached, unless it exists.
#
# .github/workflows/release.yml runs it on every push to the default branch.
# By hand, `bash .github/release.sh --no-release` just builds the zip into
# dist/. Needs git (full history), python3, curl, mcs (mono-mcs) and, to
# publish, gh with GH_TOKEN.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

# ---------------------------------------------------------------- this mod
FOLDER=SloppyModMending

build() {
  # Harmony is only compiled against: the Harmony mod supplies it at run time.
  compile Assemblies/MendingMod.dll Source/MendingMod \
    "$(nuget Lib.Harmony 2.3.3 lib/net472/0Harmony.dll)"
}
# -------------------------------------------------------------------------

RIMWORLD_REF=1.6.4871
WORK="${RUNNER_TEMP:-${TMPDIR:-/tmp}}/sloppymods-release"
OUT="$ROOT/dist"
PUBLISH=1
[ "${1:-}" = "--no-release" ] && PUBLISH=0

# Everything else in the repository (Source, Tools, Workshop art, docs) is
# left out of the zip.
GAME_ITEMS="About LoadFolders.xml Assemblies 1.5 1.6 Common Defs Patches
            Textures Sounds Languages Mods LICENSE"

# Referenced when the reference package has them.
REFS="mscorlib System System.Core System.Xml System.Xml.Linq netstandard
      Assembly-CSharp UnityEngine UnityEngine.CoreModule
      UnityEngine.IMGUIModule UnityEngine.TextRenderingModule
      UnityEngine.ImageConversionModule UnityEngine.InputLegacyModule
      UnityEngine.AudioModule UnityEngine.PhysicsModule
      UnityEngine.AnimationModule"

nuget() {   # nuget <id> <version> <path inside the package>  -> local path
  local id="$1" version="$2" inner="$3" dir="$WORK/nuget/$1.$2"
  if [ ! -d "$dir" ]; then
    mkdir -p "$dir"
    curl -fsSL -o "$dir/pkg.nupkg" \
      "https://api.nuget.org/v3-flatcontainer/${id,,}/$version/${id,,}.$version.nupkg"
    python3 -c "import zipfile,sys; zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])" \
      "$dir/pkg.nupkg" "$dir"
  fi
  echo "$dir/$inner"
}

download() {   # download <url> <file name>  -> local path
  local file="$WORK/dl/$2"
  if [ ! -f "$file" ]; then
    mkdir -p "$(dirname "$file")"
    curl -fsSL -o "$file" "$1"
  fi
  echo "$file"
}

compile() {   # compile <output dll> <source dir> [extra reference dll ...]
  local out="$1" src="$2"; shift 2
  local refs; refs="$(nuget Krafs.Rimworld.Ref "$RIMWORLD_REF" ref/net472)"
  local args=(-target:library -langversion:latest -nostdlib -noconfig -optimize+ -nowarn:1701,1702)
  for name in $REFS; do
    [ -f "$refs/$name.dll" ] && args+=("-r:$refs/$name.dll")
  done
  for extra in "$@"; do args+=("-r:$extra"); done
  mkdir -p "$(dirname "$out")"
  echo "Compiling $src -> $out"
  local sources=()
  while IFS= read -r -d '' f; do sources+=("$f"); done \
    < <(find "$src" -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -print0 | sort -z)
  mcs "${args[@]}" -out:"$out" "${sources[@]}"
}

# ---- 1. the build number
if [ "$(git rev-parse --is-shallow-repository)" = "true" ]; then
  echo "Shallow clone: the commit count is not the build number (git fetch --unshallow)." >&2
  exit 1
fi
STAMPED="$(sed -n 's:.*<modVersion>\(.*\)</modVersion>.*:\1:p' About/About.xml | head -1)"
SERIES="${STAMPED%.*}"
[ -n "$SERIES" ] && [ "$SERIES" != "$STAMPED" ] || SERIES=0.9
VERSION="$SERIES.$(git rev-list --count HEAD)"
NAME="$(sed -n 's:.*<name>\(.*\)</name>.*:\1:p' About/About.xml | head -1)"
echo "$NAME build $VERSION"
if [ "$STAMPED" != "$VERSION" ]; then
  echo "note: About.xml is stamped $STAMPED; the release is numbered $VERSION" >&2
fi

if [ "$PUBLISH" = 1 ] && gh release view "v$VERSION" >/dev/null 2>&1; then
  echo "Release v$VERSION already exists."
  exit 0
fi

# ---- 2. the assemblies
build

# ---- 3. the zip
rm -rf "$OUT"
STAGE="$OUT/$FOLDER"
mkdir -p "$STAGE"
for item in $GAME_ITEMS; do
  [ -e "$item" ] && cp -r "$item" "$STAGE/"
done
find "$STAGE" \( -name '*.pdb' -o -name '*.mdb' -o -name '*.deps.json' -o -name '*.psd' \) -delete
python3 - "$STAGE/About/About.xml" "$VERSION" <<'PY'
import re, sys
path, version = sys.argv[1:3]
text = open(path, encoding="utf-8").read()
if "<modVersion>" in text:
    text = re.sub(r"<modVersion>[^<]*</modVersion>", "<modVersion>%s</modVersion>" % version, text, count=1)
else:
    text = text.replace("</packageId>", "</packageId>\n  <modVersion>%s</modVersion>" % version, 1)
line = "Build %s\n\n" % version
if re.search(r"<description>Build [^\n<]*\n\n", text):
    text = re.sub(r"(<description>)Build [^\n<]*\n\n", lambda m: m.group(1) + line, text, count=1)
else:
    text = text.replace("<description>", "<description>" + line, 1)
open(path, "w", encoding="utf-8").write(text)
PY
ZIP="$OUT/$FOLDER.zip"
(cd "$OUT" && zip -qr "$ZIP" "$FOLDER")
echo "Packaged $ZIP ($(du -h "$ZIP" | cut -f1))"

# ---- 4. the release
[ "$PUBLISH" = 1 ] || exit 0
NOTES="$WORK/notes.md"
git log -1 --format=%B | grep -vE '^(Co-Authored-By|Claude-Session):' > "$NOTES"
gh release create "v$VERSION" "$ZIP" --title "$NAME $VERSION" \
  --notes-file "$NOTES" --target "$(git rev-parse HEAD)"
