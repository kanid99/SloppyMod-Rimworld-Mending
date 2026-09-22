# Shared mod tooling

Every file here except `modtool.conf` is **identical across the SloppyMod
RimWorld repositories**. Change one, copy it to the others. Everything
mod-specific lives in `modtool.conf`.

| | |
|---|---|
| `modtool.conf` | the only per-mod file: names, namespace, what to build, what a release must and must not contain |
| `build.sh` | compiles one assembly per `build` line, against RimWorld reference assemblies pulled from NuGet |
| `validate.py` | def sanity checks that would otherwise only show up at runtime |
| `check_api.sh` | confirms every C# class the XML names still exists in each supported game version |
| `package.sh` | stamps version/date/commit into About.xml and builds a release zip, refusing to ship one whose contents contradict `modtool.conf` |
| `modtool.py` | reads `modtool.conf`; also a CLI (`python3 Tools/modtool.py <key>`) so the shell scripts can read it |

## Usage

```bash
./Tools/build.sh            # compile
python3 Tools/validate.py   # check the defs
./Tools/check_api.sh        # check XML class names against each game version
./Tools/package.sh          # build a stamped release zip
```

`build.sh` and `check_api.sh` need a C# compiler (`mono-devel` provides `mcs`)
and, the first time, network access to nuget.org. They use Krafs' published
RimWorld reference assemblies — the public API with method bodies stripped — so
they run on a machine with no copy of the game, which is what lets anyone but
the author confirm a change compiles before it ships.

Point `validate.py` at the game's own defs and it also checks every vanilla def
name the mod uses:

```bash
RIMWORLD_CORE_DEFS="/path/to/RimWorld/Data/Core/Defs" python3 Tools/validate.py
```

## Why a copy rather than a dependency

These are four separate mods with no shared assembly and no load-order
relationship between them, which is deliberate. The tooling is shared by
copying the files, so each repository stays buildable on its own with nothing
checked out beside it.
