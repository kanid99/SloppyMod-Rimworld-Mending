"""Composites a Gemini machine body onto the repair centre's slab.

Two things this does differently from the first pass:
  * luminance is HISTOGRAM-MATCHED to VFE Factory's own measured distribution rather than
    squeezed into a 36-132 band. The squeeze killed both ends - our sprite had no true black
    outline (p1 = 26) and no highlights (p99 = 121), against VFE's 0 and 196 - which is what
    made it read as a flat plate next to their machines.
  * the slab and the ten spot markers are drawn procedurally at exact cell centres, so they
    line up with the cells the C# actually reads, whatever the generated body looks like.
"""
import glob, os
import numpy as np
from PIL import Image, ImageDraw

PX = 192                      # pixels per map cell
CW, CH = 5, 3                 # the repair centre's footprint
W, H = CW * PX, CH * PX

SLAB      = (84, 84, 84, 255)
SLAB_STEP = (66, 66, 66, 255)
OUTLINE   = (20, 20, 20, 255)
GREEN, CYAN, ORANGE = (108, 178, 92, 255), (92, 196, 208, 255), (226, 126, 38, 255)

VFE = '/home/user/vanilla-expanded/vanillafurnitureexpanded-factory/Textures/Things/Building/Factories/*/*_north.png'


def vfe_luminance_profile(samples=20000):
    lums = []
    for f in glob.glob(VFE):
        a = np.asarray(Image.open(f).convert('RGBA')).astype(np.float32)
        vis = a[..., 3] > 90
        if vis.sum() == 0:
            continue
        lums.append((a[..., :3] @ np.array([0.299, 0.587, 0.114]))[vis])
    L = np.concatenate(lums)
    return np.sort(np.random.default_rng(0).choice(L, size=min(samples, L.size), replace=False))


def clean(path):
    """Key out the model's magenta ground; it never returns exactly (255,0,255)."""
    im = Image.open(path).convert('RGB')
    a = np.asarray(im).astype(np.float32) / 255.0
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mx, mn = a.max(2), a.min(2)
    sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    pink = (r > g + 0.10) & (b > g + 0.05) & (sat > 0.25)
    alpha = ((~pink).astype(np.uint8) * 255)
    from scipy import ndimage
    alpha = ndimage.grey_erosion(alpha, size=(3, 3))
    out = Image.fromarray(np.dstack([np.asarray(im), alpha]).astype(np.uint8), 'RGBA')
    return out.crop(out.getbbox())


def to_vfe_palette(im, profile):
    a = np.asarray(im).astype(np.float32)
    rgb, al = a[..., :3].copy(), a[..., 3]
    vis = al > 90
    if vis.sum() == 0:
        return im

    mx, mn = rgb.max(2), rgb.min(2)
    sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    accent = vis & (sat > 0.30) & (rgb[..., 2] > rgb[..., 0])   # the cyan only

    # Everything that is not the accent goes neutral, so stray colour casts do not survive.
    lum = rgb @ np.array([0.299, 0.587, 0.114])
    grey = np.repeat(lum[..., None], 3, axis=-1)
    rgb[vis & ~accent] = grey[vis & ~accent]

    # Histogram-match onto VFE's curve: same quantile, their value.
    src = lum[vis]
    order = np.argsort(src)
    ranks = np.empty(order.size, dtype=np.float64)
    ranks[order] = np.arange(order.size)
    q = ranks / max(1.0, order.size - 1)
    target = np.interp(q, np.linspace(0, 1, profile.size), profile)

    scale = np.ones_like(lum)
    scale[vis] = target / np.maximum(src, 1.0)
    rgb = np.clip(rgb * scale[..., None], 0, 255)
    return Image.fromarray(np.dstack([rgb, al]).astype(np.uint8), 'RGBA')


def port(img, cx, cy, colour, facing):
    d = ImageDraw.Draw(img)
    long_, short_ = int(PX * 0.62), int(PX * 0.30)
    w, h = (long_, short_) if facing in ("down", "up") else (short_, long_)
    d.rounded_rectangle([cx - w // 2, cy - h // 2, cx + w // 2, cy + h // 2], radius=6,
                        fill=(112, 112, 112, 255), outline=OUTLINE, width=6)
    a = int(min(w, h) * 0.55)
    b = int(max(w, h) * 0.30)
    pts = {"down":  [(cx, cy + a // 2), (cx - b // 2, cy - a // 2), (cx + b // 2, cy - a // 2)],
           "up":    [(cx, cy - a // 2), (cx - b // 2, cy + a // 2), (cx + b // 2, cy + a // 2)],
           "right": [(cx + a // 2, cy), (cx - a // 2, cy - b // 2), (cx - a // 2, cy + b // 2)],
           "left":  [(cx - a // 2, cy), (cx + a // 2, cy - b // 2), (cx + a // 2, cy + b // 2)]}[facing]
    d.polygon(pts, fill=colour, outline=OUTLINE)


def build(body_path, profile):
    slab = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(slab)
    d.rounded_rectangle([7, 7, W - 8, H - 8], radius=30, fill=SLAB, outline=OUTLINE, width=15)

    # The base was a single flat grey, and it is over a third of the sprite - enough to drag the
    # whole thing's contrast below VFE's even once the machine body was matched to their curve.
    # Their bases are modelled: lit along the top edge, shadowed along the bottom, and broken up
    # by panel seams. Same here.
    d.rounded_rectangle([18, 18, W - 19, 74], radius=18, fill=(116, 116, 116, 255))       # lit top
    d.rounded_rectangle([18, H - 86, W - 19, H - 19], radius=18, fill=(52, 52, 52, 255))  # shadow
    for i in range(1, CW):                                                                # seams
        x = i * PX
        d.line([(x, 84), (x, H - 96)], fill=(62, 62, 62, 255), width=5)
    d.rounded_rectangle([30, H - 96, W - 31, H - 30], radius=16, fill=SLAB_STEP)

    body = to_vfe_palette(clean(body_path), profile)
    # Fill as much of the slab as the port strips allow. The first pass capped height at 74%
    # and let that shrink the width too, which left a dead grey field around a small machine -
    # the opposite of how VFE's sprites read, where machinery runs to the edges.
    max_w, max_h = int(W * 0.92), int(H * 0.80)
    scale = min(max_w / body.width, max_h / body.height)
    bw, bh = int(body.width * scale), int(body.height * scale)
    body = body.resize((bw, bh), Image.LANCZOS)
    slab.alpha_composite(body, ((W - bw) // 2, (H - bh) // 2))

    mid = CW // 2
    for i in range(CW):                          # back edge: item port centre, materials either side
        port(slab, int((i + 0.5) * PX), int(PX * 0.16), CYAN if i == mid else GREEN, "down")
    for j in (0, 1):                             # rear-most cell down each flank
        cy = int((j + 0.5) * PX)
        port(slab, int(PX * 0.16), cy, GREEN, "right")
        port(slab, W - int(PX * 0.16), cy, GREEN, "left")
    port(slab, int((mid + 0.5) * PX), H - int(PX * 0.16), ORANGE, "down")
    return slab


def contrast(im):
    a = np.asarray(im).astype(np.float32)
    vis = a[..., 3] > 90
    v = (a[..., :3].mean(2) / 255.0)[vis]
    return v.std(), float(np.percentile(v, 99) * 255), float(np.percentile(v, 1) * 255)


def export(body_path, profile, outdir):
    """south is the composed slab; north is its 180 turn and east its quarter turn.

    This mapping is not arbitrary - it is exactly what the shipped textures already use
    (verified: north == rot180(south)), and the C# derives its spot cells from the building's
    rotation, so changing it here would put the drawn ports on different cells than the ones
    MenderSpots actually reads."""
    slab = build(body_path, profile)
    os.makedirs(outdir, exist_ok=True)
    slab.save(f"{outdir}/AutomatedMender_south.png")
    slab.rotate(180, expand=True).save(f"{outdir}/AutomatedMender_north.png")
    slab.rotate(-90, expand=True).save(f"{outdir}/AutomatedMender_east.png")
    return slab


if __name__ == "__main__":
    import sys
    profile = vfe_luminance_profile()
    os.makedirs("built", exist_ok=True)
    print(f"{'variant':12s} {'std':>6s} {'p1':>6s} {'p99':>6s}   (VFE: 0.148 / 0 / 196)")
    for f in sorted(glob.glob("out/repair_*.png")):
        name = os.path.basename(f)[:-4]
        img = build(f, profile)
        img.save(f"built/{name}.png")
        st, p99, p1 = contrast(img)
        print(f"{name:12s} {st:6.3f} {p1:6.0f} {p99:6.0f}")

    if len(sys.argv) > 2 and sys.argv[1] == "export":
        pick = sys.argv[2]
        export(f"out/{pick}.png", profile, "export")
        print(f"exported {pick} -> export/AutomatedMender_{{north,south,east}}.png")
