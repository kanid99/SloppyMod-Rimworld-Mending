# Riimba artwork

Every sprite in this mod is drawn from primitives by the scripts in this folder. No
image generator is involved and there are no source PSDs, so the art rebuilds from a
clean checkout with nothing installed but Pillow.

```sh
pip install pillow
python3 Riimba/Source/Art/riimba_unit.py       # the drone, three views
python3 Riimba/Source/Art/riimba_station.py    # the station, three rotations
python3 Riimba/Source/Art/verify_bays.py       # checks the station art against the C#
python3 Riimba/Source/Art/make_about_art.py    # store page art, from the above
```

All four are run from the repo root.

## Why it is drawn rather than generated

The mending mod's art README works through this at length for its repair centre and the
conclusion holds here. Generators produce RENDERED machines - photoreal metal, fine
bevels, a heavy black outline of their own - and RimWorld's are ABSTRACT: a handful of
plain rounded blocks, flat tonal ramps, and detail only as a row of identical marks.
Prompting can get the subject right but not the drawing.

For the drone specifically there is a second problem. A Riimba is a disc, and a
generated disc comes back as an ellipse at some arbitrary aspect ratio with its
highlight in whatever place the model felt like. Three views of one object have to
agree about where the sensor is and which way the light falls, and nothing enforces
that across three separate generations.

## Depth is tone, not outline

Pure black appears only on the outer silhouette. Everything inside is a tone step:

* The drone's shell is three concentric discs, each lighter and each offset a few
  pixels UP. The dark crescent that leaves along the bottom edge is the entire shading
  model. A fourth disc starts to look like a rendered sphere, which is the wrong
  register for this game's top-down art.
* The station's case is a dark slab, a mid slab inset and stopped short of the bottom,
  and a lit slab across the top 42% of the SHORT side. Proportional to the short side,
  not the canvas: on the 192x576 east texture, using the canvas height put a light band
  across the top 240px that read as two materials bolted together.

One saturated colour, the teal, and it only ever marks something powered - the drone's
sensor, its pilot lamp, the station's subcore window and the bay lamps. Everything else
is desaturated grey so a Riimba reads as machinery beside RimWorld's warm-toned
colonists instead of competing with them.

## Rotations are views, not rotations

RimWorld's camera never turns, so `_north`, `_east` and `_south` are three views of the
same object. Turning the south sprite 180 degrees would move the sensor to the far side
of the disc but also light it from below, which reads as a hole rather than a bump.

`_west` is never drawn for either thing. `Graphic_Multi` mirrors `_east` horizontally
for west, and that is correct for both: the drone is symmetric about that axis, and
mirroring moves the station's bays from the right edge to the left, which is where
west's `FacingCell` of `(-1, 0, 0)` puts them.

## The station's bays are a contract with the C#

`RiimbaSpots.BayCells` returns the row of cells just outside `rot.FacingCell`, and
`Building_RiimbaStation` sends each unit to the bay matching its roster position. So
the art has to put a bay where the code puts one, in every rotation:

| rotation | facing | bays drawn on | canvas |
| --- | --- | --- | --- |
| north | `(0, 1)` | top edge | 576x192 |
| east | `(1, 0)` | right edge | 192x576 |
| south | `(0, -1)` | bottom edge | 576x192 |
| west | `(-1, 0)` | left edge, by mirroring east | - |

Note the inversion on the horizontal pair: map z runs UP and screen y runs DOWN, so
north is the TOP of the texture. That is exactly the sign error that shipped in the
mending mod's east texture, where the item port landed on the cell the C# reads as the
output, and nobody caught it by looking.

So it is not checked by looking. `verify_bays.py` reimplements `BayCells` from the C#,
runs it for all four rotations, translates each cell into a pixel position in whichever
texture that rotation draws, and asserts the gold charging-contact colour is there and
is NOT on the opposite edge. It exits non-zero on a mismatch. Run it after any change
to either side:

```sh
python3 Riimba/Source/Art/verify_bays.py
```

The contacts are used as the marker rather than the bay recess because that gold is the
only colour that appears nowhere else on the building; the recess tone is close enough
to the chassis shadow to give false positives.

## Supersampling

`riimba_draw.py` draws everything at 4x and reduces with LANCZOS. PIL's `ellipse` and
`polygon` are hard-aliased, and a 96px disc drawn directly has a visible staircase on
its rim at RimWorld's default zoom. 4x is where further increases stop being visible
after the downsample.

## Sizes

| texture | size | why |
| --- | --- | --- |
| `Riimba_*.png` | 256x256 | pawn sprite, drawn at `drawSize 1.1` |
| `RiimbaStation_north/south.png` | 576x192 | 3x1 footprint at 192px per cell, exact |
| `RiimbaStation_east.png` | 192x576 | the same, axes swapped |
| `About/Preview.png` | 640x360 | what the workshop shows |
| `About/ModIcon.png` | 256x256 | shown at about 32px in the mod list |

The station draws at `drawSize (3,1)`, equal to its `size`, so the art lands inside its
own cells with no overhang - the same choice the mending benches make, and unlike the
repair centre, which deliberately overhangs by half a cell to match the factory
machines it sits beside.

`make_about_art.py` composites the store art out of the real shipped textures rather
than drawing its own, so the store page cannot drift from what is in the game. The icon
is the drone alone: at 32px a station with three bays on it is an indistinct grey bar,
while a dark disc with one teal eye still reads.

## Scripts

| script | builds |
| --- | --- |
| `riimba_draw.py` | shared primitives, the palette, and the supersampling |
| `riimba_unit.py` | the drone, north/east/south |
| `riimba_station.py` | the station, north/east/south |
| `verify_bays.py` | checks the station textures against `RiimbaSpots.BayCells` |
| `make_about_art.py` | `About/Preview.png` and `About/ModIcon.png` |
