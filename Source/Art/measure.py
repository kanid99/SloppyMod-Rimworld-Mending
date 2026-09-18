"""Scores a sprite on the nine style metrics, against VFE Factory's own building sprites."""
import os, sys, glob
import numpy as np
from PIL import Image, ImageFilter

VFE = os.environ.get(
    'VFE_FACTORY_DIR',
    '/home/user/vanilla-expanded/vanillafurnitureexpanded-factory') + \
    '/Textures/Things/Building/'

def metrics(path):
    im = Image.open(path).convert('RGBA')
    im = im.crop(im.getbbox())
    a = np.asarray(im).astype(float)
    rgb, al = a[..., :3] / 255.0, a[..., 3] / 255.0
    m = al > 0.5
    if m.sum() < 100:
        return None
    lum = (0.299 * rgb[..., 0] + 0.587 * rgb[..., 1] + 0.114 * rgb[..., 2])
    v = lum[m]
    mx, mn = rgb.max(-1), rgb.min(-1)
    sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    # hue
    hsv = np.zeros(rgb.shape[:2])
    d = mx - mn
    nz = d > 1e-6
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    h = np.zeros_like(d)
    i = nz & (mx == r); h[i] = ((g - b)[i] / d[i]) % 6
    i = nz & (mx == g); h[i] = ((b - r)[i] / d[i]) + 2
    i = nz & (mx == b); h[i] = ((r - g)[i] / d[i]) + 4
    hsv = h * 60
    col = m & (sat > 0.20)
    warm = col & (((hsv < 60) | (hsv > 330)))
    cool = col & (hsv >= 150) & (hsv <= 260)
    # edges
    gl = Image.fromarray((lum * 255).astype('uint8'))
    ed = np.asarray(gl.filter(ImageFilter.FIND_EDGES)).astype(float)
    hard = (ed[m] > 40).mean() * 100
    # curvature: diagonal edge energy on the alpha silhouette
    sil = Image.fromarray((al * 255).astype('uint8'))
    se = np.asarray(sil.filter(ImageFilter.FIND_EDGES)).astype(float) / 255.0
    gy, gx = np.gradient((al > 0.5).astype(float))
    diag = (np.abs(gx) > 0.1) & (np.abs(gy) > 0.1)
    edge = se > 0.1
    curv = diag.sum() / max(edge.sum(), 1)
    return dict(contrast=v.std(), p99=np.percentile(v, 99) * 255,
                near_black=(v < 0.10).mean() * 100,
                sat=(sat[m] > 0.20).mean() * 100,
                hard=hard, warm=warm.sum() / m.sum() * 100,
                cool=cool.sum() / m.sum() * 100, curv=curv,
                median=np.median(v) * 255)

ref = []
for p in sorted(glob.glob(VFE + 'Factories/*/*_north.png')) + \
         sorted(glob.glob(VFE + 'Factories/*.png')):
    r = metrics(p)
    if r: ref.append(r)

keys = ['contrast', 'p99', 'median', 'near_black', 'sat', 'hard', 'warm', 'cool', 'curv']
print(f'{len(ref)} VFE sprites\n')
print(f'{"":16}{"ours":>8}{"VFE mean":>10}{"VFE range":>18}')
ours = metrics(sys.argv[1])
for k in keys:
    vals = [r[k] for r in ref]
    lo, hi = min(vals), max(vals)
    flag = '' if lo <= ours[k] <= hi else '   <-- OUT'
    print(f'{k:16}{ours[k]:8.3f}{np.mean(vals):10.3f}{lo:9.2f}-{hi:<8.2f}{flag}')
