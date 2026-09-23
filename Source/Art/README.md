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
corner and a ring of chamfered casings around a central machine - one inboard of each flank
bay, one either side of the conveyor at the intake edge, two tanks along the bottom - with a
roller run in from the item bay and another out to the output bay.

The chassis is an octagon, not a rounded square: 0.30 of a cell is cut off each corner
(`base(chamfer=...)`). Almost nothing in the Factory set is a plain square - their silhouettes
are cut, notched or stepped - and a 5x5 square read as the flattest shape in the pack.

The casings are NOT all the same colour, and that is the hierarchy. Teal is reserved for the
two hoppers on the intake edge and the two tanks at the bottom: four accents, not ten. The six
belt-fed flank casings are grey, a step darker than the machine's own housings, so the sprite
reads machine -> hoppers -> deck rather than one flat ring of colour. When all ten were teal,
cool-hued pixels covered 21% of the sprite against their 2.9% mean - their machines carry a
colour accent, they are not painted in one.

The two bottom casings are cylindrical tanks rather than boxes, built from the same parts as a
casing (side wall, cast shadow, banded top, lit cap). Every VFE machine has at least one round
form somewhere; a sprite of nothing but rectangles is the tell. They sit at the two positions
no bay feeds, so nothing had to be re-routed for them, and low enough that the gantry does not
cover them - the first attempt put them on the flank row where the gantry hid them completely.

The teal that is left is subdued deliberately. Measured: VFE's machining bay casings average
0.448 saturation; ours average **0.274**, the least saturated of the three candidates that were
on the table.

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

A port's accent rail does not stop at the bay, and neither does the bed. The bay and the run
past it are ONE channel, and the whole of it - rail, rollers and all - is on a single slope.

Sampled at 2px down their assembler's port, outer lip (y700) to inner end (y606), 94px or 0.73
of a cell:

| | at the lip | at the machine |
| --- | --- | --- |
| rail | `(91,175,94)` | 0.18 of that |
| bed | full roller crown | `(31,31,31)`, 0.32 of the crown |
| roller relief | full | **none** - by y630 the bed is one flat tone |

Both hold for the outer 30% and then fall dead straight. `chan_tone()` is that profile;
`CHAN_FLAT`, `RAIL_SHADE` and `BED_SHADE` are those numbers. Ours measures flat to x=136 then
linear to 0.183 at x=206, with the bed arriving at `(30,30,30)`.

The roller relief collapsing is half of the effect and the part that is easy to miss. Their
bed does not just get darker, it stops having rollers in it: each roller's six steps are pulled
towards their own mean until the far end is featureless. Detail that fades out looks like fog;
detail that flattens looks like something has gone under a lid.

That fall is the entry slope. In section the channel is not a floor meeting a wall, it is a
floor that climbs into the body - flat, then a ramp, then the machine's own level - and from
directly above the only thing that can carry a slope is tone. So a step from one tone to
another is wrong twice over: it reads as a wall, and it puts a hard edge where the sprite
should have none. Three earlier tries got this wrong: the rail stopping dead at the bay's inner
end, which left the accent as a stub outside the building; carrying on at a flat dimmer tone,
which just moved the wall inward; and then a slope on the rail ALONE, stopping at 0.49, which
came out as a wash because everything around it stayed flat. A slope has to take the whole
channel with it or it is just a painted stripe. The 0.49 came from sampling with a
green-dominant mask, which stops finding the rail once it is dark enough - the rail carries on
well past where that mask gives up, which is exactly the stretch that matters.

Both halves are handed the same `rail_span` - where the channel starts and ends, measured from
the chassis face - rather than each grading over its own length, because the knee in the
profile falls just inside the bay's inner end and grading twice would crease the slope at the
seam. `rail_band()` paints it in 1px slices for the same reason: one two-colour ramp cannot
bend. `rollers()` takes the same span and does the same thing to the bed; without a span it
keeps the old local 38% falloff, which is what a bay feeding the spine wants.

The item port and the output get no ramp. They feed the spine, which runs its rails at full
tone end to end because it has to read as one belt through the machine, and a ramp into it
would put back exactly the step this removes.

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

### Reject chutes

Two red bays either side of the orange output, both doing the same job. Two rather than one
because RimWorld draws west by MIRRORING the east texture when there is no `_west` file, so a
single off-centre chute would be drawn on one side of the output and read by the C# on the
other whenever the machine faced west. A symmetric pair lands on the same cells both ways,
keeps the layout mirror-symmetric like every factory machine, and `verify_spots.py` checks it -
including a negative test, moving the chutes to the wrong cells, which it catches.

The two bottom drums were placed where they were because nothing fed those positions. They are
now the reject hoppers: each chute's run is laid down before its drum, so the tank is drawn
over the run's inner end and the belt reads as coming out from under it. The red is not a VFE
colour - they have no reject port - so it is pitched to sit with theirs: the lightness of their
orange at the same muted saturation.

### Measured against their 19 sprites

`measure.py` scores a sprite on nine metrics and prints them against the same nine measured
over every machine sprite in `Textures/Things/Building/Factories/`. Every one of ours now falls
inside their observed range:

| | ours | VFE mean | VFE range |
| --- | --- | --- | --- |
| contrast (std) | 0.141 | 0.151 | 0.11-0.23 |
| p99 highlight | 166 | 166 | 133-255 |
| median luminance | 86 | 93 | 48-116 |
| near-black | 5.8% | 6.6% | 0-19.7% |
| saturated pixels | 13.3% | 12.5% | 1.0-33.3% |
| warm-hued | 1.1% | 8.8% | 0-33.3% |
| cool-hued | 10.2% | 2.9% | 0-24.3% |
| hard-edge density | 5.5 | 6.7 | 3.3-9.7 |
| diagonal silhouette | 0.141 | 0.054 | 0-0.22 |

The grey/tank/chamfer pass is what brought the last two outliers in. Before it, saturated
pixels were 23.3% and cool-hued 21.1% - a ring of ten teal casings - and the silhouette scored
0.041 against their 0.054, because a square has no diagonals in it at all. After: 12.3%, 10.2%
and 0.141. Carrying the rails inward afterwards put saturated pixels at 13.3%.

Cool still sits above their mean, and that is the mod's own accent rather than drift - the
gear-in belt is cyan by design, and 10% is comfortably inside what their own sprites do.

Note the sprite carries TEN bays where a VFE machine carries three or four; bays are
high-contrast by construction, so that is where most of our edge density goes.

```sh
python3 Source/Art/compose_repair_center.py    # writes the three rotations and scores them
python3 Source/Art/measure.py Textures/Things/Building/Production/AutomatedMender_north.png
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
| `measure.py` | scores a sprite on nine style metrics against VFE's machine sprites |
| `gen_tools.py` | generates the metalworking tool sprites (Gemini) |
| `cut.py` | chroma-keys a generated sheet and cuts it into individual objects |

`gen_bench_objects.py` and `gen_tools.py` need a Gemini API key; the cut object PNGs they
produce are committed under `objects/`, so they only need re-running to change the tool set.

Output goes to `Textures/Things/Building/Production/`: the benches are 576x192 for north and
south and 192x576 for east; the repair centre is 1152x1152 for all three.
