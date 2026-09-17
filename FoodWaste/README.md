# SloppyMods Food Waste

RimWorld 1.6. Food that rots away leaves **spoiled food** instead of vanishing. Spoiled food
refines into chemfuel, composts into Vanilla Recycling Expanded's reclaimed biopacks, or seals
into wastepacks.

Requires **Biotech** and **Vanilla Recycling Expanded** (which itself requires Vanilla Expanded
Framework).

## What it does

| | |
|---|---|
| Rotted food leaves | `SloppyFW_SpoiledFood`, scaled to the nutrition lost |
| At a biofuel refinery | 10 spoiled food → 20 chemfuel (needs `BiofuelRefining`) |
| At a VRE recycling bench | 10 spoiled food → 1 reclaimed biopack (needs `VRecyclingE_BasicRecycling`) |
| At a VRE recycling bench | 6 spoiled food → 1 wastepack (needs `VRecyclingE_BasicRecycling`) |

Reclaimed biopacks are the point of the mod. In VRE a stack of five left to dissolve on ordinary
soil raises its fertility, and before this mod there was no deliberate way to make one — the only
source is a byproduct of the wastepack-to-chempack recipe, one per three wastepacks. Food waste to
compost to better soil is the loop this closes.

## Design notes

**Yield is measured in nutrition, not items.** Raw food is 0.05 nutrition a unit and a meal is
0.9, so counting items would have made a rotted 75-stack of rice worth eight times a rotted
10-stack of simple meals. It is the other way round. One unit of spoiled food is one nutrition of
food that went bad: about four units from the rice, about nine from the meals.

**Spoilage stays a loss.** Vanilla refines 35 raw food — 1.75 nutrition — into 35 chemfuel, so
fresh organics run about 20 chemfuel per nutrition. The spoiled-food recipe pays 2. Deliberately
rotting your stores to make fuel is roughly ten times worse than refining them fresh. These
recipes decide how total the loss is, never whether it is one.

**Spoiled food rots away by itself.** Four days, temperature-gated like all rot. A player who does
not want the feature is not taxed by it: ignore the piles and they are gone. Keep it cold if you
mean to process it. Without this the mod would slowly turn every colony into a midden and become a
hauling chore rather than an option.

**The early exit exists on purpose.** The biofuel refinery route needs no recycling research, so a
young colony has something to do with spoiled food before it unlocks the benches. Gating everything
behind VRE research would have meant watching it pile up with no answer.

## How it hooks into rot

No Harmony, matching the Sloppy mending mod's stance.

`FoodWasteDefPatches` runs at `[StaticConstructorOnStartup]` and injects `CompSpoilageResidue` into
every `ThingDef` that (a) has a `CompProperties_Rottable` with `rotDestroys`, (b) has ingestible
properties, and (c) has nutrition above zero. The comp's `PostDestroy` spawns the residue.

Asking the def database that question, rather than listing food defs in XML, is what makes the mod
cover modded food nobody has written yet. It also keeps the code off `Thing.Destroy`, which is one
of the hottest paths in the game, and out of `CompRottable`'s private tick internals.

Two deliberate non-behaviours:

- Food that rots inside a pawn's inventory or a caravan pack leaves nothing. `Thing.Destroy`
  captures `Map` before despawning and `Thing.Map` is null for anything not standing on the map,
  so a non-null `previousMap` is exactly "this was spawned". There is no sensible cell to drop a
  heap of mush onto when a pawn's rations turn.
- Food that burns or is deconstructed leaves nothing. A `rotDestroys` item is destroyed in the
  same tick it first reads `RotStage.Rotting`, so the stage check admits the rot path and
  essentially nothing else.

## Building

The assembly is not committed (see `.gitignore`). Point the project at your RimWorld install and
build; output lands in `Assemblies/`.

```
dotnet build Source/FoodWasteMod/FoodWasteMod.csproj \
  -p:RimWorldManagedDir="/path/to/RimWorld/RimWorldWin64_Data/Managed"
```

## Art

`Source/Art/make_spoiled_food.py` draws the three `Graphic_StackCount` variants with no
third-party imaging library — it writes the PNGs directly. Rerun it after changing the palette or
the lump layout:

```
python3 Source/Art/make_spoiled_food.py
```

## Where this lives

This mod sits in a subfolder of the Sloppy mending mod's repository rather than in one of its
own. RimWorld only reads `About/`, `Defs/`, `Textures/` and friends at the *root* of a mod folder,
plus version folders like `1.6/` - a folder called `FoodWaste` is neither, so it is completely
inert as far as the mending mod is concerned and the two cannot interfere.

To play it, this folder has to reach `RimWorld/Mods/` on its own. Copy it, or symlink it, which
means a `git pull` updates the mod in place with nothing to re-copy:

```
# Linux / macOS
ln -s /path/to/SloppyMod-Rimworld-Mending-Mod/FoodWaste ~/.steam/steam/steamapps/common/RimWorld/Mods/SloppyFoodWaste

# Windows, elevated prompt
mklink /D "C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\SloppyFoodWaste" "C:\path\to\SloppyMod-Rimworld-Mending-Mod\FoodWaste"
```

## Status

**Compiles clean** - zero errors, zero warnings - against RimWorld 1.6 reference assemblies
(`Krafs.Rimworld.Ref`), which also confirms every API signature and every type the XML names,
including `GasType.RotStink` and the `CompProperties_Rottable` fields.

**Not yet run in game.** Reference assemblies carry signatures and no IL, so two things remain
unverified by anything but play:

- The balance numbers. None of them have been played.
- That `CompRottable` disposes of rotted food with `DestroyMode.Vanish`. Every other guard in
  `CompSpoilageResidue` is checkable and checks out, but this one cannot be read out of a
  reference assembly. If it is wrong the mod goes quiet rather than breaking, so the check logs
  a dev-mode warning naming itself as the cause instead of failing silently.
