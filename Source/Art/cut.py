import numpy as np
from PIL import Image
from scipy import ndimage

# The model never returns exactly (255,0,255) - it renders a pink of its own - so
# the key is hue-based with a saturation floor rather than a fixed colour match.
def key_out(path):
    im = Image.open(path).convert('RGB')
    a = np.asarray(im).astype(np.float32) / 255.0
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mx, mn = a.max(2), a.min(2)
    sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    # Magenta/pink: red and blue both clearly above green.
    pink = (r > g + 0.10) & (b > g + 0.05) & (sat > 0.25)
    pink = ndimage.binary_closing(pink, np.ones((3, 3)))
    pink = ndimage.binary_fill_holes(pink) & pink
    alpha = (~pink).astype(np.uint8) * 255
    # Shave the pink fringe that survives on antialiased edges.
    alpha = ndimage.grey_erosion(alpha, size=(3, 3))
    out = np.dstack([np.asarray(im), alpha])
    return Image.fromarray(out, 'RGBA')

def components(img, min_px=1500):
    a = np.asarray(img)[..., 3] > 40
    a = ndimage.binary_closing(a, np.ones((9, 9)))
    lab, n = ndimage.label(a)
    boxes = []
    for i in range(1, n + 1):
        ys, xs = np.where(lab == i)
        if len(ys) < min_px:
            continue
        boxes.append((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1, len(ys)))
    boxes.sort(key=lambda b: b[0])
    return boxes

import sys
for sheet in (sys.argv[1:] or ['metaltools_a', 'metaltools_b']):
    img = key_out(f'out/{sheet}.png')
    img.save(f'out/{sheet}_keyed.png')
    for i, (x0, y0, x1, y1, n) in enumerate(components(img)):
        img.crop((x0, y0, x1, y1)).save(f'out/{sheet}_{i}.png')
        print(f'{sheet}_{i}', (x1 - x0, y1 - y0), 'px', n)
