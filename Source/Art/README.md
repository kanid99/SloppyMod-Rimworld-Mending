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

## Repair centre body

`gen_repair_center.py` asks Gemini for the machine body only; `compose_repair_center.py` keys
out its magenta ground, pulls it onto VFE Factory's palette, and composites it on a
procedurally drawn chassis with the ingress and egress bays at exact cell positions.

### drawSize is one cell larger than size

Every VFE Factory machine draws at one cell larger than its footprint - their 5x5 assembler has
`drawSize (6,6)` - so the art overhangs half a cell all round and their conveyor bays live in
that overhang, protruding out over the very cells items are placed on. The repair centre does
the same: `size (5,5)`, `drawSize (6,6)`, a 1152x1152 sprite whose chassis occupies the inner
five cells. Drawing at `drawSize == size` is what made it read as a small machine with cramped
ports however large the footprint was.

### The ports are spread round the perimeter, not queued on one edge

Three bays on the intake edge - the item port at centre with a material port either side -
three more down each flank, and the single output bay opposite. Mirror-symmetric about the
vertical centre line, which is what every one of their machines is.

They used to fill the whole back edge first and only spill onto the flanks, which put seven
bays shoulder to shoulder along one edge. `MenderSpots.ResourceInputCells` now takes two cells
from the back edge and the rest from the flanks, and `SideCells` starts a cell in from the
corner so no two ports meet corner to corner. The count is unchanged: eight material spots, one
item spot, one output.

Note the sprite carries TEN bays where a VFE machine carries three or four. Port bays are
high-contrast by construction, so ours sits at the top of their hard-edge range rather than the
middle; that is the cost of ten spots, not a drawing error.

### The structure is drawn, not generated

A machine sitting on a plate does not read as a factory building. Theirs are built from
repeated mirrored structure, so `vfe_chassis.py` draws that: a housing at each corner, a rail
down each flank, a framed bay holding the machine, and a pair of slatted blocks either side of
a recessed conveyor spine that carries work out through the output port. Only the centrepiece
comes from the generator, and `symmetrise` reflects its left half onto its right so the mirror
symmetry is exact rather than approximate.

### The ports are drawn to VFE's measured anatomy

They are not styled after VFE's ports - they are the same construction, read off their sprites
pixel by pixel with `vfe_chassis.py` reproducing it. Their machines are authored at 128px per
map cell; ours at 192, so every number scales by 1.5.

Across a port, as fractions of one cell: divider `.047` | cap `.094` | rail `.055` |
roller bed `.609` | rail `.055` | cap `.094` | divider `.047`. Neighbouring bays share their
divider, so ports sit flush at exactly one per cell. A bay runs the full half-cell overhang,
from the canvas edge to the chassis face.

A roller is a six-step ramp from a `(97,106,119)` crown to a `(60,63,68)` trough, repeating
every `.125` cell, **with no separator between one roller and the next** - the trough is the
line. The whole bed darkens by about 38% towards the machine. The chevron on the deck is a
flat triangle with no outline at all.

### Black is the silhouette, and nothing else

The single biggest style error in the earlier version. Measured against VFE: pure black
appears only on the outer silhouette, ~7px at their scale. Every division inside the sprite -
between two adjacent ports, around a bay, between panels - is a 3px `(49,49,49)` dark grey.
Depth comes from tone: their chassis face is a smooth `(138,134,132)` to `(119,115,113)` ramp
over ~25px, and nothing is outlined to make it read as raised.

Outlining every element in black gave our sprite a hard-edge density of 70.7 per unit side
against their 9.0-34.6, and 10.4% near-black pixels against their 6.5%. Drawing it their way
brought both inside their range (33.6 and 6.1%). The body prompt carries the same rule now:
one black outline on the silhouette, dark grey or nothing inside it, depth from soft ramps.

### Palette and contrast

Luminance is HISTOGRAM-MATCHED to VFE's own measured distribution rather than squeezed into a
fixed band. The old squeeze into 36-132 left the sprite with no true black outline and no
highlights - p1 26, p99 121, against VFE's 0 and 196 - which is what made it read as a flat
plate beside their machines. The accent test keeps any single saturated colour the body
carries but excludes magenta hues, because the model sometimes paints a pipe stub in its own
background colour.

`kill_chroma` runs AFTER the body is resized, not before. LANCZOS rings: downscaling a
saturated orange accent against a dark neighbour overshoots each channel independently, and
the undershoot on the warm channels beside the overshoot on blue lands as a violet fringe.
That fringe is created by the resize, so cleaning the source alone never moved the count -
it went from 3938 stray pixels to 235 once the pass was moved after the resize.

The markers are drawn, not generated, because the C# derives its spot cells from the
building's rotation; south is the composed chassis, north its 180 turn, east its quarter turn,
matching what the shipped textures already used.

```sh
python3 gen_repair_center.py         # from this directory, needs a Gemini key
python3 compose_repair_center.py     # builds and scores every variant
python3 compose_repair_center.py export repair_s4
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
| `gen_tools.py` | generates the metalworking tool sprites (Gemini) |
| `fit.py` | keys a generated body off its background and crops it to the real silhouette |
| `vfe_chassis.py` | the chassis, its structure and the ingress/egress bays, at VFE's measured proportions |
| `cut.py` | chroma-keys a generated sheet and cuts it into individual objects |

`gen_bench_objects.py` and `gen_tools.py` need a Gemini API key; the cut object PNGs they
produce are committed under `objects/`, so they only need re-running to change the tool set.

Output goes to `Textures/Things/Building/Production/`: 576x192 for north and south,
192x576 for east.
