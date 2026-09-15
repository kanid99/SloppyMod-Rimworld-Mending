from psd_tools import PSDImage
from PIL import Image

SRC = '/root/.claude/uploads/72994f15-0d28-544a-9ec4-c4b93575cdf3/0b343283-MendingBench_electric_north.psd'
psd = PSDImage.open(SRC)
CANVAS = (0, 0, 224, 96)

layers = {}
def flat(group):
    for l in group:
        if l.is_group():
            flat(l)
        else:
            layers[l.name] = l
flat(psd)

def render(name):
    """That layer alone, on the full canvas, cropped to its own bbox."""
    l = layers[name]
    full = l.composite(viewport=CANVAS)
    return full.crop(l.bbox), l.bbox

# --- source geometry -------------------------------------------------------
# The slab is the 'border' layer; everything else sits on it.
SLAB = layers['border'].bbox                      # (16, 16, 208, 89)
SLAB_X0, SLAB_Y0, SLAB_X1, SLAB_Y1 = SLAB
SLAB_W = SLAB_X1 - SLAB_X0                        # 192
SLAB_H = SLAB_Y1 - SLAB_Y0                        # 73

# --- target geometry -------------------------------------------------------
# A 1x3 building needs a 3:1 texture. Widening the slab to 3:1 at the source
# height is the only change; nothing on it is scaled, so no circle becomes an
# ellipse and the sewing machine keeps its proportions.
OUT_H = SLAB_H                                    # 73
OUT_W = OUT_H * 3                                 # 219
STRETCH = OUT_W / SLAB_W                          # ~1.14
TOP_BLEED = SLAB_Y0 - layers['sewingMachine'].bbox[1]   # machine overhangs the slab
FRAME_H = OUT_H + TOP_BLEED
FRAME_W = FRAME_H * 3

def hstretch_3slice(img, new_w, margin=10):
    """Widen a slab-like image without thinning its left and right edges."""
    w, h = img.size
    if new_w <= w:
        return img.resize((new_w, h), Image.LANCZOS)
    left = img.crop((0, 0, margin, h))
    right = img.crop((w - margin, 0, w, h))
    mid = img.crop((margin, 0, w - margin, h)).resize((new_w - 2 * margin, h), Image.LANCZOS)
    out = Image.new('RGBA', (new_w, h), (0, 0, 0, 0))
    out.paste(left, (0, 0))
    out.paste(mid, (margin, 0))
    out.paste(right, (new_w - margin, 0))
    return out

frame = Image.new('RGBA', (FRAME_W, FRAME_H), (0, 0, 0, 0))
SLAB_TOP = TOP_BLEED     # where the slab starts inside the frame

# Slab: border then base, both widened by 3-slice so their edges stay crisp.
for name in ('border', 'base'):
    img, bbox = render(name)
    inset_l = bbox[0] - SLAB_X0
    inset_r = SLAB_X1 - bbox[2]
    new_w = OUT_W - round(inset_l * STRETCH) - round(inset_r * STRETCH)
    wide = hstretch_3slice(img, new_w)
    frame.alpha_composite(wide, (round(inset_l * STRETCH), SLAB_TOP + (bbox[1] - SLAB_Y0)))

# Everything on the slab keeps its own size; only its position is spread out,
# so the extra width becomes breathing room between objects rather than stretch.
def place(name, stretch_width=False):
    img, bbox = render(name)
    w = bbox[2] - bbox[0]
    centre = (bbox[0] + bbox[2]) / 2 - SLAB_X0
    new_centre = OUT_W / 2 + (centre - SLAB_W / 2) * STRETCH
    if stretch_width:
        w = round(w * STRETCH)
        img = img.resize((w, img.size[1]), Image.LANCZOS)
    x = round(new_centre - w / 2)
    y = SLAB_TOP + (bbox[1] - SLAB_Y0)
    frame.alpha_composite(img, (x, y))

place('Panel', stretch_width=True)   # flat translucent work surface, safe to widen
for name in ('spools', 'Layer 2', 'scissor', 'steel', 'Layer 1', 'sewingMachine', 'allshad'):
    place(name)

# --- output ----------------------------------------------------------------
TARGET = (576, 192)
north = frame.resize(TARGET, Image.LANCZOS)
north.save('TableMending_Electric_north.png')

# South is the same bench seen from the other side, east is it turned a quarter
# turn. Deriving both from this one image keeps the palette identical across
# rotations - drawing them separately is what caused colour drift last time.
north.rotate(180).save('TableMending_Electric_south.png')
north.transpose(Image.ROTATE_270).save('TableMending_Electric_east.png')

for f in ('north', 'south', 'east'):
    im = Image.open(f'TableMending_Electric_{f}.png')
    print(f, im.size, im.mode)
print('frame', frame.size, 'stretch %.3f' % STRETCH, 'top bleed', TOP_BLEED)
