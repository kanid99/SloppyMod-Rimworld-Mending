#!/usr/bin/env bash
# Builds this mod's assemblies, one per RimWorld version it supports.
#
# Reads Tools/modtool.conf, so the same script serves every SloppyMod
# repository: a mod that ships one assembly has one `build` line, and one that
# ships a separate assembly per game version has several, each with its own
# reference version and its own compile-time defines.
#
# Needs a C# compiler (mono-devel provides mcs) and, the first time, network
# access to nuget.org for RimWorld's reference assemblies. Those are Krafs'
# published reference packages: the public API with method bodies stripped, so
# they are redistributable and this builds on a machine with no RimWorld
# installed - which is the only way anyone but the author can check that a
# change compiles before it ships.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORK="${RIMWORLD_REFS:-${TMPDIR:-/tmp}/rimworld-refs}"
ASSEMBLY="$(python3 "$ROOT/Tools/modtool.py" assembly)"
SOURCE="$(python3 "$ROOT/Tools/modtool.py" source)"

# Referenced when present. RimWorld splits Unity across modules and different
# versions ship different ones, so each is added only if the package has it -
# naming one that is absent is a hard compile error, and naming one that is
# never used costs nothing.
REFS="mscorlib System System.Core System.Xml netstandard Assembly-CSharp
      UnityEngine UnityEngine.CoreModule UnityEngine.IMGUIModule
      UnityEngine.TextRenderingModule UnityEngine.ImageConversionModule
      UnityEngine.InputLegacyModule UnityEngine.AudioModule
      UnityEngine.PhysicsModule UnityEngine.AnimationModule"

fetch_refs() {
  local version="$1" dir="$WORK/$version"
  if [ ! -d "$dir/ref/net472" ]; then
    echo "Fetching RimWorld $version reference assemblies..." >&2
    mkdir -p "$dir"
    curl -sSL -o "$dir/refs.nupkg" \
      "https://api.nuget.org/v3-flatcontainer/krafs.rimworld.ref/$version/krafs.rimworld.ref.$version.nupkg"
    python3 -c "import zipfile,sys; zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])" \
      "$dir/refs.nupkg" "$dir" >&2
  fi
  echo "$dir/ref/net472"
}

build_one() {
  local outdir="$1" version="$2" defines="${3:-}"
  local refs; refs="$(fetch_refs "$version")"
  local args=(-target:library -langversion:latest -nostdlib -noconfig)
  [ -n "$defines" ] && args+=("-define:$defines")
  for name in $REFS; do
    [ -f "$refs/$name.dll" ] && args+=("-r:$refs/$name.dll")
  done

  mkdir -p "$ROOT/$outdir"
  echo "Compiling for $version -> $outdir${defines:+  (defines: $defines)}"
  mcs "${args[@]}" -out:"$ROOT/$outdir/$ASSEMBLY.dll" "$ROOT/$SOURCE/"*.cs
}

count=0
while read -r outdir version defines; do
  [ -n "$outdir" ] || continue
  build_one "$outdir" "$version" "$(echo "$defines" | tr ' ' ';')"
  count=$((count + 1))
done < <(python3 "$ROOT/Tools/modtool.py" build)

echo "Built $count assembl$([ "$count" = 1 ] && echo y || echo ies)."
