# SloppyMods Mending Solutions

Repair damaged apparel and weapons in RimWorld 1.6. Three stations, gated behind three
research projects: a hand bench that works on what your colony could plausibly have made
by hand, an electrical bench that lifts those limits, and an industrial repair centre that
runs on conveyors with no pawn labour at all.

What a repair costs is not a flat price. Material, work time, waste and the chance of
botching it all scale together with how damaged the item is and how good the mender is,
and repair work always produces real waste - that is the mechanic, not an optional extra.

## Installing

This repository IS the mod, so it can be cloned straight into your mods folder:

```sh
git clone https://github.com/kanid99/SloppyMod-Rimworld-Mending-Mod.git "RimWorld/Mods/MendingSolutions"
```

Then build the assembly (below) and enable it in the mod list. A release copy needs only:

```
MendingSolutions/
    About/
    Assemblies/
    Defs/
    Languages/
    Patches/
    Textures/
```

`Source/` does not need to ship.

## Requirements

* **Biotech** - the basic subcore the repair centre is built around, and the mech tech
  research it sits behind.
* **Vanilla Recycling Expanded** - supplies the trash that repair work produces. Without it
  the stations fall back to wastepacks alone; the def names they look for are XML-tunable.

**Vanilla Furniture Expanded - Factory** is not required, but is detected. With it present
the mod's own feed port is dropped from the architect menu in favour of VFE's factory hopper,
which is belt-aware and carries the storage group tag the rest of that chain expects. See
`Patches/VFEFactory_Compat.xml`.

## Building the assembly

```sh
dotnet build Source/MendingMod/MendingMod.csproj
```

Output goes to `Assemblies/`, which is gitignored. The csproj references the DLLs in
RimWorld's `Managed` folder; point it at a non-default install with:

```sh
dotnet build Source/MendingMod/MendingMod.csproj \
    -p:RimWorldManagedDir="<path to>/RimWorldWin64_Data/Managed"
```

1.6 only, and deliberately so: the assembly is built against 1.6 signatures - `Thing.Tick`
and `Thing.DrawAt` are `protected` there and were `public` in 1.4/1.5 - so it cannot load on
earlier versions. Listing them in `About.xml` would hand those players a mod that installs
and silently does nothing.

## How it fits together

| piece | what it does |
| --- | --- |
| `TableMending_Manual` | hand bench, stuffable, no power |
| `TableMending_Electric` | powered bench, 4x faster, no tech or component limits |
| `AutomatedMender` | 5x5 conveyor-fed repair line, always works as a skill 10 mender |
| `MendingFeedPort` | a one-cell hopper for belts, dropped when VFE Factory is present |
| `MendApparel` / `MendWeapon` | the bills; both train Crafting, weapons route to Smithing work |
| `BasicMending` | behind Complex Clothing and Smithing |
| `ElectricMending` | behind Fabrication |
| `AutomatedMending` | behind Advanced Fabrication and Basic Mechtech |

`MendingUtility` is where the cost, time, waste and failure numbers are worked out, and it
is the file to read first. `WorkGiver_Mend` extends vanilla's `WorkGiver_DoBill` and
overrides only `JobOnThing`, so bill handling, ingredient search and the failed-bill throttle
stay vanilla's - an earlier version replaced the whole job-start path and cost about 40 TPS
on a heavily modded save.

`MenderSpots` derives the repair centre's ten spots from its footprint and rotation, so the
C# and the artwork cannot drift: `Source/Art/verify_spots.py` reimplements it and checks the
drawn bays land on the cells the C# actually reads, for all four rotations.

Design notes live next to the code they explain. The art pipeline has its own writeup in
[`Source/Art/README.md`](Source/Art/README.md) - it covers how the sprites are drawn and
measured against Vanilla Furniture Expanded's, and why nothing in the repair centre is
generated.

## Settings

Waste on or off, and how much. A simple mode that charges a flat steel cost instead of
pricing off the item. Work speed. Whether tech limits apply at all, and whether unresearched
gear is merely expensive or outright refused. Whether resources are required. Whether a
failed repair costs hit points, and whether quality can drop - always, only on failure, or
never. And the share of a tech tier your colony must have researched before it counts as
that tier.
