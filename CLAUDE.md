# Working on this repo

## The default branch is the release

The owner installs and updates the SloppyMods mods straight from GitHub: RimSort
clones this repository's default branch (`main`) into RimWorld's Mods
folder and pulls it in place. There is no separate release step, so **every
commit pushed to `main` is what the game loads next**.

- Push finished work to `main`. Nothing half-done: each commit must load
  and play as-is.
- **Commit the compiled assemblies.** RimWorld loads the DLL, not the source,
  and a clone gets only what is in git. This mod needs:
  - `Assemblies/MendingMod.dll`
  `.gitignore` must not exclude them (a bare `Assemblies/` rule does, at any
  depth - un-ignore those paths). Rebuild and commit the DLLs in the same
  commit as any C# change; a stale DLL is what players get.
- **Stamp the build number before every commit** so the mod list and RimSort
  show which build is installed: `<modVersion>` in `About/About.xml`, and a
  `Build x.y` line at the top of its description, set to
  `series.<commit count after this commit>` (series 0.9 unless
  `Tools/modtool.conf` says otherwise). With the shared tooling in `Tools/`
  that is `python3 Tools/stamp_version.py`, and `Tools/validate.py` fails
  while it is stale; without it, set the same value by hand. Never leave a
  placeholder like `0.9.0-dev`.
- Keep `packageId` unchanged: saves and RimSort key on it. The display
  `<name>` is "SloppyMods Mending Solutions".
- Everything in the repository root lands in the Mods folder, so keep
  anything RimWorld might try to load (Defs, Patches, Textures, LoadFolders)
  deliberate; `Source/`, `Tools/`, docs and images are ignored by the game.
- A zip for the Steam Workshop or manual testing still comes from
  `bash Tools/package.sh` (shared tooling) when asked.
- **Every push to the default branch publishes a GitHub Release**,
  `v<build>`, with the mod zip attached (`.github/workflows/release.yml`,
  which runs `.github/release.sh`). RimSort's GitHub Mods panel reads "Latest
  Version" from the newest release and installs its zip, so this is what
  RimSort users get. The script compiles the assemblies from source with mcs
  against Krafs' reference assemblies, so the zip always matches the commit;
  if the build changes (a new assembly, a new reference), update `build()` at
  the top of `release.sh`, and check it with
  `bash .github/release.sh --no-release` (zip in `dist/`). mcs is stricter
  than Roslyn in places (e.g. two `out var` of the same name in one method),
  so keep the source compiling under it.
- Harmony is provided at runtime by the Harmony mod: compile against it, never ship 0Harmony.dll.

## Standing preferences

- Licence is CC0 for every SloppyMods mod.
- Replies to the owner: concise summaries; attach a build zip when they will
  test in game.
- For art changes, show prototypes or before/after comparisons and get the
  owner's approval per item before replacing anything.

