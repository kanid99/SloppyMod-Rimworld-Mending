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
| right-click repair | order one item repaired, or be told exactly why it can't be |
| right-click yourself | repair worn or wielded gear: take it off, repair it, put it back on |
| inspect pane | damaged gear shows what its repair would cost at your best crafter's skill |
| auto-repair threshold | automatic repair only picks up gear below a set share of its hit points |
| `Bill_Mend` | a mend bill pinned to one material, for installs without Material Filter |

`MendingUtility` is where the cost, time, waste and failure numbers are worked out, and it
is the file to read first. `WorkGiver_Mend` extends vanilla's `WorkGiver_DoBill` and
overrides only `JobOnThing`, so bill handling, ingredient search and the failed-bill throttle
stay vanilla's - an earlier version replaced the whole job-start path and cost about 40 TPS
on a heavily modded save.

Right-clicking a damaged weapon or piece of apparel with a colonist selected offers to
repair that specific item: the colonist carries it to the nearest mending station that can
take it, adding a one-off bill if that bench has none, and the bill is left behind at zero
so it can be reused. When the order is not possible the entry is still there, greyed out and
carrying the reason - which bench has no power, which materials are short and how many, which
skill is too low, or that the item is beyond what any station you have built can repair.
The bill it leaves behind is pinned to the item's own material, so it stays scoped to the gear
the order was about.

Material filtering works because `WorkGiver_Mend` honours the BILL's ingredient filter and not
just the recipe's - it read only the recipe's before, which meant narrowing a mend bill in its
own config dialog did nothing at all. With that fixed, the [Material Filter][mf] mod's
checkboxes restrict mend bills through vanilla: it builds a `SpecialThingFilterDef` per
material whose runtime-emitted worker compares `Thing.Stuff`, and `ThingFilter.Allows(Thing)`
evaluates those. Untick plasteel on a mend bill and plasteel swords stop being picked up.
`MaterialFilters` finds them by name, so there is no hard reference and no reflection.

Without that mod there is no material UI, and `Bill_Mend` is the fallback - the same
restriction carried on the bill and shown in its label. It is deliberately NOT used when the
mod is present, because a pin there would silently override the checkboxes the player can see.

[mf]: https://steamcommunity.com/workshop/filedetails/?id=1541305730

`MendJobMaker` is the whole of it and `FloatMenuOptionProvider_Repair` is the hook; there is
no Harmony patch involved, because 1.6's provider system finds any subclass on its own.

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
