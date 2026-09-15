from PIL import Image
import numpy as np

# Geometry measured off the PSD's own layers (see notes in rebuild.py).
CROP_W, CROP_H = 192, 76          # psd.bbox space
BORDER_BODY = (3, 71)             # rows of the black slab body
BORDER_LEGS = (71, 76)            # rows of the leg tabs
BASE_INSET  = 3                   # base sits 3px inside the border
SLAB_LEFT, SLAB_TOP = 0, 3        # slab origin inside the crop
SLAB_W, SLAB_H = 192, 73          # slab length and depth

OUT_W, OUT_H = SLAB_H, SLAB_H * 3     # 73 x 219 - a 1x3 bench stood on end
LENGTH_SCALE = OUT_H / SLAB_W

border = Image.open('layer_0_border.png')
base   = Image.open('layer_1_base.png')

frame = Image.new('RGBA', (OUT_W, OUT_H), (0, 0, 0, 0))

# --- slab -------------------------------------------------------------------
# The legs belong at the bottom of the sprite in every rotation - the camera
# does not turn - so the body is turned on its end and fresh leg tabs are
# stamped underneath, rather than rotating the whole slab and landing the legs
# on its side.
LEG_H = BORDER_LEGS[1] - BORDER_LEGS[0]
body = border.crop((0, BORDER_BODY[0], CROP_W, BORDER_BODY[1]))
body = body.transpose(Image.ROTATE_270).resize((OUT_W, OUT_H - LEG_H), Image.LANCZOS)
frame.alpha_composite(body, (0, 0))

leg = border.crop((59, BORDER_LEGS[0], 71, BORDER_LEGS[1]))   # one clean tab
for x in (0, OUT_W - leg.size[0]):
    frame.alpha_composite(leg, (x, OUT_H - LEG_H))

base_body = base.crop((0, BORDER_BODY[0] + BASE_INSET, CROP_W, BORDER_BODY[1] - BASE_INSET))
base_body = base_body.transpose(Image.ROTATE_270).resize(
    (OUT_W - 2 * BASE_INSET, OUT_H - LEG_H - 2 * BASE_INSET), Image.LANCZOS)
frame.alpha_composite(base_body, (BASE_INSET, BASE_INSET))

# --- everything on the bench ------------------------------------------------
# A quarter turn moves an object's POSITION but must not turn the object: a
# toolbox seen from the side is still drawn upright. So along-the-bench maps to
# down-the-sprite, and across-the-bench maps to across-the-sprite.
# A few objects sit at the same point ALONG the bench and only differ across it,
# which a quarter turn stacks on top of each other; these nudge them apart.
NUDGE = {'layer_7_allshad.png': (-6, 26)}

LAYERS = [
    ('layer_2_Panel.png',        (65, 36, 147, 81)),
    ('layer_3_spools.png',       (19, 19, 71, 61)),
    ('layer_9_Layer_2.png',      (22, 30, 82, 73)),
    ('layer_5_scissor.png',      (52, 49, 90, 75)),
    ('layer_4_steel.png',        (76, 19, 135, 67)),
    ('layer_8_Layer_1.png',      (128, 43, 155, 80)),
    ('layer_6_sewingMachine.png',(145, 13, 203, 53)),
    ('layer_7_allshad.png',      (159, 66, 193, 82)),
]
PSD_SLAB_X0, PSD_SLAB_Y0 = 16, 16     # slab origin in PSD canvas coords
CROP_OFF_X, CROP_OFF_Y = 16, 13       # psd.bbox origin

for fname, bbox in LAYERS:
    img = Image.open(fname).crop((bbox[0] - CROP_OFF_X, bbox[1] - CROP_OFF_Y,
                                  bbox[2] - CROP_OFF_X, bbox[3] - CROP_OFF_Y))
    # The work panel is a flat translucent rectangle, so it turns with the bench
    # instead of staying upright - it is surface, not an object standing on it.
    if fname == 'layer_2_Panel.png':
        img = img.transpose(Image.ROTATE_270).resize(
            (bbox[3] - bbox[1], round((bbox[2] - bbox[0]) * LENGTH_SCALE)), Image.LANCZOS)
    w, h = img.size
    cx = (bbox[0] + bbox[2]) / 2 - PSD_SLAB_X0     # along the bench
    cy = (bbox[1] + bbox[3]) / 2 - PSD_SLAB_Y0     # across the bench
    dx, dy = NUDGE.get(fname, (0, 0))
    nx = cy - w / 2 + dx
    ny = cx * LENGTH_SCALE - h / 2 + dy
    nx = max(2, min(OUT_W - w - 2, round(nx)))
    ny = max(2, min(OUT_H - LEG_H - h - 1, round(ny)))
    frame.alpha_composite(img, (nx, ny))

frame.resize((192, 576), Image.LANCZOS).save('TableMending_Electric_east.png')
print('east built', frame.size, '->', (192, 576))
