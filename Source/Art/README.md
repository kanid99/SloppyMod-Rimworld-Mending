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

## Scripts

| script | builds |
| --- | --- |
| `rebuild.py` | electrical station, north/south |
| `east.py` | electrical station, east |
| `gen_tools.py` | generates the metalworking tool sprites (Gemini) |
| `cut.py` | chroma-keys and cuts those sheets into individual objects |
| `compose_bench.py` | hand station, north/south |
| `east_bench.py` | hand station, east |

`gen_tools.py` needs a Gemini API key; the cut tool PNGs it produces are committed
as textures, so it only needs re-running to change the tool set.

Output goes to `Textures/Things/Building/Production/`: 576x192 for north and south,
192x576 for east.
