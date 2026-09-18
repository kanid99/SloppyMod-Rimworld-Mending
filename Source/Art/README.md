# Bench artwork

The two bench sprites are built from the PSDs here rather than painted as finished
PNGs, so the layout can be regenerated whenever the footprint or the tool set changes.

## Why the art is rebuilt rather than exported

A 1x3 building needs a 3:1 sprite. Both PSDs are authored at 224x96 with a 192x73
slab, which is 2.63:1 - exported straight out, the bench sits short of its own
footprint. `bench_compose.py` widens the slab to 3:1 with a horizontal 3-slice (so the
left and right edges keep their thickness) and then *positions* objects on it rather
than scaling it, so no spool becomes an ellipse.

## Why the objects are generated but the slab never is

Asking the model for a whole bench fails twice over. Its benches come out anywhere
from 2.7:1 to 5:1 - telling it "exactly three times as wide as deep" made that worse,
not better - and letterboxing a 5:1 body inside a 3:1 cell is what reads as a bench
too small for its own footprint. Worse, it bakes a material into the worktop: pale
wood, plank seams, brushed steel. A stuffable building has to take its colour from
whatever it was built out of, and there is no clean way to mask a slab that already
has wood grain painted on it.

So only the OBJECTS come from the model. `gen_bench_objects.py` asks for a row of
separate item sprites on a magenta ground, `cut.py` keys and splits them, and the cut
PNGs are committed under `objects/` so the benches rebuild without an API key.

## Rotations

`_south` is a copy of `_north`: the camera never rotates, so a 180 turn would put
the bench's legs on the ceiling. `_east` is built rather than rotated - `bench_east.py`
stands the slab on its end, stamps fresh leg tabs underneath, and moves each object to
its quarter-turned position. Every object in the current layouts is drawn looking
straight down, so they turn with the bench; an object drawn in side elevation would
have to stay upright instead.

## Stuff masks

Both benches are stuffable, so each texture has a companion mask. RimWorld reads the
mask's RED channel as "tint this with the thing's colour" and leaves BLACK areas with
the main texture's own colours, so the slab is red and everything standing on it is
black - a wooden bench still has steel tools on it, and the electric bench keeps its
sewing machine whatever it is built from. The masks come out of the same build as the
artwork; there is no separate pass.

The mask filename appends an `m` with **no separating underscore**
(`TableMending_Manual_northm.png`): `Graphic_Multi.Init` builds the path as
`path + "_north" + "m"`, and `_north_m` silently finds nothing and leaves the building
untinted.

```sh
python3 Source/Art/build_benches.py     # both benches, all rotations, art and masks
```

## Repair centre

The whole sprite is drawn by `compose_repair_center.py` and `vfe_chassis.py`. No image
generator is involved. Every proportion and colour below was read off VFE Factory's own
sprites pixel by pixel rather than described from memory.

### Why nothing here is generated

The generator's machines came back RENDERED - photoreal metal, fine bevels, a heavy black
outline of their own - and VFE's are ABSTRACT: a handful of plain rounded blocks, flat tonal
ramps, and detail only as a row of identical slats. Prompting could get the subject right but
never the drawing. Drawing it directly also disposed of two defects the generated body kept
dragging in: a violet fringe left by resampling its magenta ground, and its own black
silhouette ring sitting inside ours as an interior outline.

### drawSize is one cell larger than size

Every VFE machine draws at one cell larger than its footprint - their 5x5 assembler has
`drawSize (6,6)`. Ours matches: `size (5,5)`, `drawSize (6,6)`, a 1152x1152 sprite. The room is
there so the chassis can run to the footprint's full width and the bays can peek out of it.

### The bays are cut INTO the chassis

Measured on their assembler: the chassis edge is at y=66, a port's black lip starts at y=58 and
its roller bed runs y=65 to y=99. So a bay is 41px deep and only EIGHT of those - a sixteenth
of a cell - stick out past the chassis. The rest is cut into it. An earlier version ran the bay
across the whole half-cell overhang, which left the bays sitting outside the building rather
than in it.

Across a bay, as fractions of one cell: divider `.047` | cap `.094` | rail `.055` | roller bed
`.609` | rail `.055` | cap `.094` | divider `.047`. Neighbouring bays share their divider, so
they sit flush at one per cell. A roller is a six-step ramp from a `(97,106,119)` crown to a
`(60,63,68)` trough repeating every `.125` cell, **with no separator between one roller and the
next** - the trough is the line. The chevron is a flat triangle, no outline.

### Black is the silhouette, and nothing else

Pure black appears only on the outer silhouette, ~7px at their scale. Every division inside the
sprite is a 3px `(49,49,49)` dark grey or a plain tone step. Depth is tone: their chassis face
is a smooth `(138,134,132)` to `(119,115,113)` ramp over ~25px, and nothing is outlined to make
it read as raised. Outlining every element in black once gave this sprite a hard-edge density
of 70.7 per unit side against their 9.0-34.6.

### Orientation, and how it is checked

The machine takes material in on the side it FACES, which is the factory mod's convention -
point one of their machines north and it feeds from its north edge. Ours was the other way
round, so a repair centre dropped into a belt line laid out for their machines ran backwards.

The sprite is therefore drawn intake-up, which is the north view; south is its 180 turn and
east its quarter turn CLOCKWISE. PIL's `rotate()` is counter-clockwise for a positive angle, so
that is `rotate(-90)`. This sign is not cosmetic: the shipped east texture had it the other way
and drew the item port on the cell the C# reads as the OUTPUT, so an east or west facing
machine wanted gear on its output spot. Nobody caught it by looking.

The chevrons follow the MATERIAL, not the edge they sit on. Keying them off the edge alone
made every arrow point inwards, the output's included, so the machine read as taking things in
on all four sides and never putting anything out. VFE's all point the same way - in at the top,
through, out at the bottom - so the output bay's arrow points away from the machine.

`verify_spots.py` reimplements `MenderSpots` from the C# and samples the real texture at every
spot's position on the chassis edge, for all four rotations, checking the rail colour there is
the one that spot expects. Run it after any change to either side:

```sh
python3 Source/Art/verify_spots.py flipped
```

### Layout

Three bays on the intake edge - the item port at centre with a material port either side -
three more down each flank, and the single output bay opposite. Mirror-symmetric about the
vertical centre line, which is what every one of their machines is. Inside: a housing at each
corner and a ring of chamfered teal casings around a central machine - one inboard of each
flank bay, one either side of the conveyor at top and bottom - with a roller run in from the
item bay and another out to the output bay.

The teal is subdued deliberately. Measured: VFE's machining bay casings average 0.448
saturation over 17% of their sprite; ours average **0.274** over 27%. Per pixel it is the least
saturated of the three candidates that were on the table, which is what keeps a ring of ten
casings from reading as a colour accent.

Each casing is a raised structure, built the way theirs are: a top face that ramps only
gently, a thin dark seam where that face meets its side wall, a LIGHT grey wall below it, and a
soft shadow cast down and right onto the deck. The lift comes from the wall and the shadow, not
from shading the face steeply - a steeply shaded face just looks like a painted patch. A first
attempt read the dark band in the colour slice as the wall and painted the whole wall dark,
which made the casings look burnt; at 1:1 against their sprite the dark band is a seam and the
wall is `(93,93,93)`.

The machine in the middle has three things to say, and as a flat slab with a slat panel it
said none of them:

* **work passes through it.** The belt runs unbroken from the item bay to the output bay
  instead of stopping at a solid block. It is drawn in three segments - the accent rails mark
  it where it meets a port, and the stretch inside the machine is plain rollers. One continuous
  accented run put a stripe of orange down the whole sprite, which is not how they use it.
* **something acts on the work.** A gantry bridges the two housings across the belt with a tool
  head on its centre line, over a dark working face. A bridge over a belt is the clearest way a
  top-down sprite can say "this machine does something to an item".
* **it stands on the deck.** Housings, gantry and head all get the same side wall and cast
  shadow the casings get, so the middle reads as three stacked levels rather than one plate.

Every bay that feeds a casing runs INTO it, as one continuous channel. A stretch of roller
bed carries on from the bay's inner end and ends at a chute cut into the casing: a lit lip
across its mouth, then a dark throat broken up by grid lines. Their conveyor oven's chute is
exactly that - a wide dark recess under a raised lip.

Nothing changes width along the way, and that is the whole point. The run is drawn at the
bay's own bed width and holds the brightness the bay ends at, flat. A first attempt used a
narrower run: the step down at the seam was jarring, and VFE never change a belt's width
along its length. Letting the run keep darkening past the bay was no better - it arrived at
the chute as dark as the chute and the two merged into one black notch.

One number governs every belt on the sprite: `BED_W`, the roller bed's width at VFE's
authoring scale. The port bays, the feed runs, the chutes and the spine all derive from it, and
`Chassis.spine_width()` returns the box width a spine needs for its bed to come out at exactly
that. The spine used to take a hand-picked 0.62 cell, which gave it a 74px bed against the
ports' 117 - the belt narrowed to two thirds the moment it left the port. `spine()` now asserts
its box matches `spine_width()`, so that cannot drift back in unnoticed.

ONE belt runs the whole way through, intake port to output port, passing under the arm that
does the work. Its rails change colour where it goes under: cyan on the way in, which is the
gear-in port's own rail colour, orange on the way out, which is the output port's. That single
line states the machine's job in one read - a damaged item goes in at the top, something acts
on it in the middle, a repaired one comes out at the bottom. It was three separate segments
before, one of them unaccented, which read as three belts rather than one.

The bed also picks up the tone of the bay it leaves. A port bay's bed darkens 38% towards the
machine, so a belt meeting one at full brightness has a visible step in it at the join; the
run comes up from the bay's tone over the first fifth of its length and back down into the
output bay over the last.

The flank casings sit a third of a cell further in than the first layout put them, purely so
the run has somewhere to travel; butted against the bay there was nothing to see. The two
intake-edge casings sit on their bays' centre lines for the same reason.

Grey pipework links the ring, laid down before the casings so it passes under them and shows
only in the gaps. Straight runs only - routing it round corners left little hooks that read as
debris at play zoom.

Greebles are small repeated marks - racks of short parallel slashes, rows of square pads, faint
tracks down the working face - kept a couple of tone steps off whatever they sit on and never
black. They read as surface detail at full zoom and sink into the block at play zoom, instead
of turning into noise the way an outlined detail would.

The spot geometry follows the footprint, so the C# and the art cannot drift: see
`MenderSpots.ResourceInputCells`.

### Measured against their 19 sprites

| | ours | VFE mean | VFE range |
| --- | --- | --- | --- |
| contrast (std) | 0.131 | 0.151 | 0.11-0.23 |
| p99 highlight | 165 | 167 | 133-255 |
| near-black | 5.9% | 6.5% | 0-19.8% |
| saturated pixels | 32.1% | 13.2% | 1.4-33.9% |
| hard-edge density | 27.6 | 18.7 | 9.0-34.6 |

Saturated pixels sit near the top of their range, and that is AREA rather than intensity: ten
casings is simply more coloured surface than their machines carry. Per pixel the teal is the
least saturated of the three candidates, at 0.274 against their 0.448.

Note the sprite carries TEN bays where a VFE machine carries three or four; bays are
high-contrast by construction, so that is where most of our edge density goes.

```sh
python3 Source/Art/compose_repair_center.py    # writes the three rotations and scores them
```

## Store page art

`make_about_art.py` builds `About/Preview.png` (640x360) and `About/ModIcon.png` (256x256) by
compositing the real building sprites, so the store page cannot drift from what is in the game.
RimWorld looks for both at those exact filenames under `About/` - see `ModMetaData.PreviewImagePath`
and `ModIconImagePath` - so no `modIconPath` entry is needed. Re-run it after any texture change:

```sh
python3 Source/Art/make_about_art.py      # from the repo root
```

## Scripts

| script | builds |
| --- | --- |
| `build_benches.py` | both benches, north/south/east, artwork and masks |
| `bench_compose.py` | the 3:1 slab and the north layout |
| `bench_east.py` | the east rotation |
| `bench_layouts.py` | which objects sit where on each bench |
| `gen_bench_objects.py` | generates the object sprites (Gemini) |
| `compose_repair_center.py` | the repair centre, all three rotations |
| `vfe_chassis.py` | the drawing primitives, at VFE's measured proportions |
| `gen_tools.py` | generates the metalworking tool sprites (Gemini) |
| `cut.py` | chroma-keys a generated sheet and cuts it into individual objects |

`gen_bench_objects.py` and `gen_tools.py` need a Gemini API key; the cut object PNGs they
produce are committed under `objects/`, so they only need re-running to change the tool set.

Output goes to `Textures/Things/Building/Production/`: the benches are 576x192 for north and
south and 192x576 for east; the repair centre is 1152x1152 for all three.
