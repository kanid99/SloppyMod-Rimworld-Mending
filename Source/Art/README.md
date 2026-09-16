# Bench artwork

The two bench sprites are built from the PSDs here rather than painted as finished
PNGs, so the layout can be regenerated whenever the footprint or the tool set changes.

## Why the art is rebuilt rather than exported

A 1x3 building needs a 3:1 sprite. Both PSDs are authored at 224x96 with a 192x73
slab, which is 2.63:1 - exported straight out, the bench sits short of its own
footprint. The scripts widen the slab to 3:1 with a horizontal 3-slice (so the
left and right edges keep their thickness) and then *reposition* everything on it
rather than scaling it, so no spool becomes an ellipse.

## Rotations

`_south` is a copy of `_north`: the camera never rotates, so a 180 turn would put
the bench's legs on the ceiling. `_east` is built rather than rotated, for the same
reason - the slab is stood on its end, fresh leg tabs are stamped underneath, and
each object is moved to its quarter-turned position while staying upright. A
toolbox seen from the side is still drawn the right way up.

## Stuff masks

Both benches are stuffable, so each texture has a companion mask. RimWorld reads the
mask's RED channel as "tint this with the thing's colour" and leaves BLACK areas with
the main texture's own colours, so the slab is red and everything standing on it is
black - a wooden bench still has steel tools on it, and the electric bench keeps its
red toolbox whatever it is built from.

Run any script with `MENDING_MASK=1` to emit the mask instead of the artwork. The mask
filename appends an `m` with **no separating underscore** (`TableMending_Manual_northm.png`):
`Graphic_Multi.Init` builds the path as `path + "_north" + "m"`, and `_north_m` silently
finds nothing and leaves the building untinted.

```sh
python3 rebuild.py && MENDING_MASK=1 python3 rebuild.py
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
| `rebuild.py` | electrical station, north/south |
| `east.py` | electrical station, east |
| `gen_tools.py` | generates the metalworking tool sprites (Gemini) |
| `fit.py` | keys a generated body off its background and crops it to the real silhouette |
| `cut.py` | chroma-keys and cuts those sheets into individual objects |
| `compose_bench.py` | hand station, north/south |
| `east_bench.py` | hand station, east |

`gen_tools.py` needs a Gemini API key; the cut tool PNGs it produces are committed
as textures, so it only needs re-running to change the tool set.

Output goes to `Textures/Things/Building/Production/`: 576x192 for north and south,
192x576 for east.
