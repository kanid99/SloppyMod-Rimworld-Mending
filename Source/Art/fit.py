"""Key, tighten and fit a Gemini bench body to the 3x1 footprint, and derive its stuff mask.

The old keyer only understood a magenta ground, and only eroded it - so a white ground or a
faint halo survived, the crop bbox came out far bigger than the machine, and the fitter then
scaled a small body into a big empty cell. This keys by flooding in from the frame edge
instead, which does not care what colour the ground is.
"""
import numpy as np
from PIL import Image
from scipy import ndimage

TARGET_W, TARGET_H = 576, 192          # 3x1 cells at 192px


def key(path, tol=34):
    """The model sometimes frames its magenta ground with a stripe of another colour, or draws
    the ground white inside a thin magenta border. So key from two different rings - the very
    edge and one inset - and keep whichever leaves the smaller plausible body."""
    im = Image.open(path).convert('RGB')
    best = None
    for inset in (0, 0.035):
        cand = _key_from_ring(im, inset, tol)
        if cand is None:
            continue
        if best is None or cand[1] < best[1]:
            best = cand
    body = best[0]
    alpha = (body * 255).astype(np.uint8)
    alpha = ndimage.grey_erosion(alpha, size=(3, 3))
    out = Image.fromarray(np.dstack([np.asarray(im), alpha]).astype(np.uint8), 'RGBA')
    return out.crop(out.getbbox())


def _key_from_ring(im, inset, tol):
    a_full = np.asarray(im).astype(np.int16)
    h, w, _ = a_full.shape
    k = int(round(min(h, w) * inset))
    a = a_full[k:h - k, k:w - k] if k else a_full

    ring = np.concatenate([a[0], a[-1], a[:, 0], a[:, -1]])
    bg = np.median(ring, axis=0)

    near = (np.abs(a - bg).max(2) <= tol)
    lab, n = ndimage.label(near)
    border = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]])))
    border.discard(0)
    ground = np.isin(lab, list(border))

    body = ~ground
    if k:                                   # everything outside the sampled ring is ground too
        full = np.zeros((h, w), dtype=bool)
        full[k:h - k, k:w - k] = body
        body = full

    body = ndimage.binary_closing(body, np.ones((5, 5)))
    body = ndimage.binary_fill_holes(body)

    # Drop anything that is not the bench itself (stray border strips, specks).
    lab, n = ndimage.label(body)
    if n:
        sizes = ndimage.sum(body, lab, range(1, n + 1))
        body = lab == (int(np.argmax(sizes)) + 1)

    frac = body.mean()
    if frac < 0.04 or frac > 0.985:
        return None
    return body, frac


def fit(im, tw=TARGET_W, th=TARGET_H, max_stretch=1.30):
    """Fill the footprint. A bench is a rectangle, so a small anisotropic stretch is invisible
    and is much better than letterboxing a 2.7:1 body inside a 3:1 cell."""
    sx, sy = tw / im.width, th / im.height
    if max(sx, sy) / min(sx, sy) <= max_stretch:
        return im.resize((tw, th), Image.LANCZOS)
    s = min(sx, sy)
    r = im.resize((max(1, round(im.width * s)), max(1, round(im.height * s))), Image.LANCZOS)
    out = Image.new('RGBA', (tw, th), (0, 0, 0, 0))
    out.alpha_composite(r, ((tw - r.width) // 2, (th - r.height) // 2))
    return out


def slab_mask(im, sat_thresh=0.16, dark_drop=46):
    """Red where the stuff colour should apply, black where the art keeps its own colours.

    The slab is the big smooth near-neutral field; anything sitting on it is either coloured
    or markedly darker than the slab around it."""
    a = np.asarray(im).astype(np.float32)
    rgb, al = a[..., :3], a[..., 3]
    vis = al > 90
    if vis.sum() == 0:
        return im

    mx, mn = rgb.max(2), rgb.min(2)
    sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    lum = rgb @ np.array([0.299, 0.587, 0.114])

    # Compare each pixel to the slab level around it rather than to one global number, so a
    # shaded end of the bench is not mistaken for an object lying on it.
    local = ndimage.median_filter(lum, size=61)
    obj = vis & ((sat > sat_thresh) | (lum < local - dark_drop))
    obj = ndimage.binary_closing(obj, np.ones((5, 5)))
    obj = ndimage.binary_fill_holes(obj)
    obj = ndimage.binary_opening(obj, np.ones((3, 3)))

    lab, n = ndimage.label(obj)
    if n:
        sizes = ndimage.sum(obj, lab, range(1, n + 1))
        small = np.isin(lab, [i + 1 for i, s in enumerate(sizes) if s < 40])
        obj &= ~small

    red = np.where(vis & ~obj, 255, 0).astype(np.uint8)
    zero = np.zeros_like(red)
    return Image.fromarray(np.dstack([red, zero, zero, al.astype(np.uint8)]), 'RGBA')


def tint(im, mask, colour):
    """Preview what RimWorld does: multiply the masked area by the stuff colour."""
    a = np.asarray(im).astype(np.float32)
    m = np.asarray(mask).astype(np.float32)[..., 0] / 255.0
    c = np.array(colour, dtype=np.float32) / 255.0
    rgb = a[..., :3] * (1 - m[..., None]) + a[..., :3] * c * m[..., None]
    return Image.fromarray(np.dstack([np.clip(rgb, 0, 255), a[..., 3]]).astype(np.uint8), 'RGBA')
