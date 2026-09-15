from psd_tools import PSDImage
from PIL import Image, ImageFilter
import numpy as np

PSD = '/root/.claude/uploads/72994f15-0d28-544a-9ec4-c4b93575cdf3/07780ef2-TableTailorHand_north.psd'
psd = PSDImage.open(PSD)
CANVAS = (0, 0, 224, 96)
groups = {l.name: l for l in psd}

SLAB_X0, SLAB_Y0, SLAB_X1, SLAB_Y1 = groups['border'].bbox    # 16,16,208,89
SLAB_W, SLAB_H = SLAB_X1 - SLAB_X0, SLAB_Y1 - SLAB_Y0         # 192 x 73
OUT_W, OUT_H = SLAB_H, SLAB_H * 3                             # 73 x 219
STRETCH = OUT_H / SLAB_W

def layer_img(name):
    l = groups[name]
    return l.composite(viewport=CANVAS).crop(l.bbox), l.bbox

border, bb = layer_img('border')
base, bsb = layer_img('base')

# Leg tabs live in the bottom rows of the border; measure rather than assume.
alpha = np.asarray(border)[..., 3] > 8
rows = alpha.sum(axis=1)
full = rows.max()
leg_top = next(y for y in range(len(rows) - 1, -1, -1) if rows[y] == full) + 1
LEG_H = border.size[1] - leg_top
cols = alpha[leg_top:, :].sum(axis=0)
runs, start = [], None
for x, c in enumerate(cols):
    if c and start is None: start = x
    elif not c and start is not None: runs.append((start, x)); start = None
if start is not None: runs.append((start, len(cols)))
tab = border.crop((runs[1][0], leg_top, runs[1][1], border.size[1]))

frame = Image.new('RGBA', (OUT_W, OUT_H), (0, 0, 0, 0))

# Body on its end; legs re-stamped underneath so they stay at the bottom of the
# sprite - the camera does not rotate with the building.
body = border.crop((0, 0, border.size[0], leg_top)).transpose(Image.ROTATE_270)
frame.alpha_composite(body.resize((OUT_W, OUT_H - LEG_H), Image.LANCZOS), (0, 0))
for x in (0, OUT_W - tab.size[0]):
    frame.alpha_composite(tab, (x, OUT_H - LEG_H))

INSET = bsb[0] - SLAB_X0     # base sits this far inside the border
base_body = base.crop((0, 0, base.size[0], leg_top - INSET)).transpose(Image.ROTATE_270)
frame.alpha_composite(
    base_body.resize((OUT_W - 2 * INSET, OUT_H - LEG_H - 2 * INSET), Image.LANCZOS),
    (INSET, INSET))

def place(img, cx, cy, shadow=True):
    """cx = position ALONG the bench, cy = ACROSS it, both in north-frame pixels.
    A quarter turn swaps those axes but never turns the object itself."""
    w, h = img.size
    M = INSET + 3   # keep sprites off the slab's black border, not just inside it
    x = max(M, min(OUT_W - w - M, round(cy - w / 2)))
    y = max(M, min(OUT_H - LEG_H - h - M, round(cx - h / 2)))
    if shadow:
        a = np.asarray(img)[..., 3]
        z = np.zeros_like(a)
        sh = Image.fromarray(np.dstack([z, z, z, (a * 0.33).astype(np.uint8)]), 'RGBA') \
                  .filter(ImageFilter.GaussianBlur(0.8))
        frame.alpha_composite(sh, (x - 1, y + 2))
    frame.alpha_composite(img, (x, y))

# Vanilla objects, at their widened north positions.
for name in ('scissor', 'spools'):
    img, bbox = layer_img(name)
    w = bbox[2] - bbox[0]
    centre = (bbox[0] + bbox[2]) / 2 - SLAB_X0
    fx = OUT_H / 2 + (centre - SLAB_W / 2) * STRETCH
    fy = (bbox[1] + bbox[3]) / 2 - SLAB_Y0
    place(img, fx, fy, shadow=False)

# Same tools, same sizes, same north-frame placements as compose_bench.py.
TOOLS = [
    ('out/metaltools_a_3.png', (28, 10), (62, 8)),
    ('out/metaltools_a_0.png', (36, 17), (58, 24)),
    ('out/metaltools_a_2.png', (20, 22), (60, 41)),
    ('out/metaltools_a_1.png', (11, 44), (100, 14)),
    ('out/plates.png',         (24, 27), (120, 17)),
    ('out/rivets.png',         (22, 15), (120, 47)),
]
# A plain rectangular block has no "up", so it turns with the bench instead of
# staying landscape and colliding with its neighbour across the narrow side.
ROTATE_IN_EAST = {'out/metaltools_a_3.png'}
# Objects that shared an along-bench position and only differed across it end up
# stacked once the axes swap; this separates them again.
EAST_NUDGE = {'out/metaltools_a_3.png': -28}

for path, size, (fx, fy) in TOOLS:
    img = Image.open(path).resize(size, Image.LANCZOS)
    if path in ROTATE_IN_EAST:
        img = img.transpose(Image.ROTATE_90)
    place(img, fx + size[0] / 2 + EAST_NUDGE.get(path, 0), fy + size[1] / 2)

frame.resize((192, 576), Image.LANCZOS).save('TableMending_Manual_east.png')
print('east built', frame.size, 'legs', LEG_H, 'tabs', runs)
