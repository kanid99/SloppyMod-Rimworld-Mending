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

`gen_repair_center.py` asks Gemini for the machine body only; `compose_repair_center.py` keys out
its magenta ground, pulls it onto VFE Factory's palette, and composites it on a procedurally drawn
slab with the ten spot markers at exact cell centres.

The palette step HISTOGRAM-MATCHES luminance to VFE's own measured distribution rather than
squeezing it into a fixed band. The old squeeze into 36-132 left the sprite with no true black
outline and no highlights - p1 26, p99 121, against VFE's 0 and 196 - which is what made it read
as a flat plate beside their machines.

`fit.py` does the keying. It floods in from the frame edge and keeps the largest blob rather than
matching magenta by hue, because the model sometimes returns a white ground inside a thin magenta
border, and even on a clean magenta ground it leaves a halo the hue key kept. That halo inflated the
crop box, so the fitter scaled a small machine into a big empty slab; keying this way lifted the
sprite's contrast from 0.128 to 0.147 against VFE's 0.148 without touching the palette step.

The markers are drawn, not generated, because the C# derives its spot cells from the building's
rotation; south is the composed slab, north its 180 turn, east its quarter turn, matching what
the shipped textures already used.

```sh
python3 gen.py                       # from this directory, needs a Gemini key
python3 compose.py                   # builds and scores every variant
python3 compose.py export repair_b3  # writes the three rotations
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
| `cut.py` | chroma-keys a generated sheet and cuts it into individual objects |

`gen_bench_objects.py` and `gen_tools.py` need a Gemini API key; the cut object PNGs they
produce are committed under `objects/`, so they only need re-running to change the tool set.

Output goes to `Textures/Things/Building/Production/`: 576x192 for north and south,
192x576 for east.
