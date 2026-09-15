from psd_tools import PSDImage
from PIL import Image, ImageFilter
import numpy as np
from maskmode import MASK, prep

PSD = '/root/.claude/uploads/72994f15-0d28-544a-9ec4-c4b93575cdf3/07780ef2-TableTailorHand_north.psd'
psd = PSDImage.open(PSD)
CANVAS = (0, 0, 224, 96)

groups = {l.name: l for l in psd}

SLAB = groups['border'].bbox                # (16, 16, 208, 89)
SLAB_X0, SLAB_Y0, SLAB_X1, SLAB_Y1 = SLAB
SLAB_W, SLAB_H = SLAB_X1 - SLAB_X0, SLAB_Y1 - SLAB_Y0     # 192 x 73

OUT_H = SLAB_H                              # 73
OUT_W = OUT_H * 3                           # 219 - a 1x3 bench is a 3:1 sprite
STRETCH = OUT_W / SLAB_W

def layer_img(name):
    l = groups[name]
    return l.composite(viewport=CANVAS).crop(l.bbox), l.bbox

def hstretch_3slice(img, new_w, margin=10):
    w, h = img.size
    if new_w <= w:
        return img.resize((new_w, h), Image.LANCZOS)
    left, right = img.crop((0, 0, margin, h)), img.crop((w - margin, 0, w, h))
    mid = img.crop((margin, 0, w - margin, h)).resize((new_w - 2 * margin, h), Image.LANCZOS)
    out = Image.new('RGBA', (new_w, h), (0, 0, 0, 0))
    out.paste(left, (0, 0)); out.paste(mid, (margin, 0)); out.paste(right, (new_w - margin, 0))
    return out

frame = Image.new('RGBA', (OUT_W, OUT_H), (0, 0, 0, 0))

# --- vanilla slab, widened to 3:1 ------------------------------------------
for name in ('border', 'base'):
    img, bbox = layer_img(name)
    inset_l, inset_r = bbox[0] - SLAB_X0, SLAB_X1 - bbox[2]
    new_w = OUT_W - round(inset_l * STRETCH) - round(inset_r * STRETCH)
    frame.alpha_composite(prep(hstretch_3slice(img, new_w), slab=True),
                          (round(inset_l * STRETCH), bbox[1] - SLAB_Y0))

def place(img, bbox_or_xy, size=None, shadow=True):
    """Drop a sprite on the bench, with the soft offset shadow the vanilla
    objects use (their own shadow layers sit at 33% opacity, down and left)."""
    if size:
        img = img.resize(size, Image.LANCZOS)
    x, y = bbox_or_xy
    img = prep(img)
    if shadow:
        a = np.asarray(img)[..., 3]
        sh = Image.fromarray(np.dstack([
            np.zeros_like(a), np.zeros_like(a), np.zeros_like(a),
            (a * 0.33).astype(np.uint8)]), 'RGBA').filter(ImageFilter.GaussianBlur(0.8))
        frame.alpha_composite(sh, (x - 1, y + 2))
    frame.alpha_composite(img, (x, y))

# --- the vanilla objects, spread out with the slab --------------------------
for name in ('scissor', 'spools'):
    img, bbox = layer_img(name)
    w = bbox[2] - bbox[0]
    centre = (bbox[0] + bbox[2]) / 2 - SLAB_X0
    nx = round(OUT_W / 2 + (centre - SLAB_W / 2) * STRETCH - w / 2)
    place(img, (nx, bbox[1] - SLAB_Y0), shadow=False)   # they carry their own

# --- the new metalworking tools --------------------------------------------
# Sizes chosen against the vanilla scissors (18x33 source px) so nothing on the
# bench reads as a different scale.
# The bench interior runs y 3..67 and the legs start below that, so every sprite
# is kept inside y 5..63 - an object clipping the bottom edge is the thing that
# makes a workbench read as the wrong size for its footprint.
TOOLS = [
    ('out/metaltools_a_3.png', (28, 10), (62, 8)),     # whetstone
    ('out/metaltools_a_0.png', (36, 17), (58, 24)),    # ball-peen hammer
    ('out/metaltools_a_2.png', (20, 22), (60, 41)),    # hand file
    ('out/metaltools_a_1.png', (11, 44), (100, 14)),   # blacksmith tongs
    ('out/plates.png',         (24, 27), (120, 17)),   # steel patch plates
    ('out/rivets.png',         (22, 15), (120, 47)),   # loose rivets
]
for path, size, xy in TOOLS:
    place(Image.open(path), xy, size)

SUFFIX = 'm' if MASK else ''
out = frame.resize((576, 192), Image.LANCZOS)
out.save(f'TableMending_Manual_north{SUFFIX}.png')
out.save(f'TableMending_Manual_south{SUFFIX}.png')
print('built', frame.size, '-> (576, 192)  stretch %.3f' % STRETCH)
