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
import fit as _fit
import numpy as np
from PIL import Image, ImageDraw

PX = 192                      # pixels per map cell
CW, CH = 5, 5                 # the repair centre's FOOTPRINT, in cells
# VFE draw a 5x5 machine at drawSize (6,6): the art overhangs the footprint by half a cell on
# every side, and their ingress and egress bays live in that overhang, protruding out over the
# very cells items are placed on. Drawing at drawSize == size is what made ours look small and
# its ports cramped, however big the footprint got.
MARGIN = 0.5
DW, DH = CW + 2 * MARGIN, CH + 2 * MARGIN
W, H = int(DW * PX), int(DH * PX)
MX, MY = int(MARGIN * PX), int(MARGIN * PX)       # chassis inset inside the canvas

# Measured off VFE Factory's own sprites, not guessed: their chassis is a warm grey-brown,
# their conveyor rollers are cool slate, and their port rails are a muted orange for an input
# and a green for an output.
FRAME     = (114, 108, 100, 255)      # thin lit strip around the chassis edge
FIELD     = (86, 80, 74, 255)         # the deck itself - darker than the machinery on it
FIELD_LIT = (101, 95, 88, 255)
FIELD_DRK = (66, 61, 56, 255)
SEAM      = (64, 60, 55, 255)
CAP       = (168, 163, 154, 255)      # pale end blocks either side of a port
BAY       = (44, 46, 50, 255)         # the shadow a port bay sits in
ROLL_HI   = (122, 131, 143, 255)      # roller crown
ROLL_LO   = (49, 51, 53, 255)         # roller trough
OUTLINE   = (20, 20, 20, 255)
GREEN, CYAN, ORANGE = (91, 175, 94, 255), (92, 196, 208, 255), (196, 126, 52, 255)

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
    # Was cyan-only (blue above red). The industrial bodies use a dull amber instead, so the
    # accent test is on saturation alone - whatever single colour the body carries survives,
    # and every stray colour cast still goes neutral. Magenta is the exception: the model
    # sometimes paints a pipe stub or a highlight in its own background colour, and with a
    # hue-blind accent rule that chroma leaked through onto the finished sprite as pink blobs.
    r_, g_, b_ = rgb[..., 0] / 255.0, rgb[..., 1] / 255.0, rgb[..., 2] / 255.0
    chroma = (r_ > g_ + 0.10) & (b_ > g_ + 0.05)
    accent = vis & (sat > 0.42) & ~chroma   # VFE use orange in thin strips, not over a whole casing

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


def _rollers(d, box, horizontal, n=6):
    """A roller bed: identical plain bars across the direction of travel, each a two-step
    light-to-dark gradient. This is the detail VFE uses inside every one of their ports."""
    x0, y0, x1, y1 = box
    span = (y1 - y0) if horizontal else (x1 - x0)
    step = span / n
    for i in range(n):
        a = y0 + i * step if horizontal else x0 + i * step
        b = a + step
        for k in range(6):                       # crown bright, trough dark
            t0, t1 = a + (b - a) * k / 6, a + (b - a) * (k + 1) / 6
            f = abs(k - 2.5) / 2.5
            c = tuple(int(ROLL_HI[j] + (ROLL_LO[j] - ROLL_HI[j]) * f) for j in range(3)) + (255,)
            if horizontal:
                d.rectangle([x0, t0, x1, t1], fill=c)
            else:
                d.rectangle([t0, y0, t1, y1], fill=c)
        c = OUTLINE if i else OUTLINE
        if horizontal:
            d.line([(x0, b), (x1, b)], fill=(32, 34, 37, 255), width=3)
        else:
            d.line([(b, y0), (b, y1)], fill=(32, 34, 37, 255), width=3)


def port(img, cx, cy, colour, facing, long_=None, deep_=None):
    """One ingress/egress bay, drawn the way VFE Factory draws theirs: a bay cut through the
    chassis frame, pale end blocks either side, a coloured rail down each inner edge and a
    roller bed between them, all inside a thick black outline.

    `facing` is the direction an item travels. The rollers always run across that direction.
    """
    d = ImageDraw.Draw(img)
    long_ = int(PX * 0.54) if long_ is None else long_
    deep_ = int(PX * 0.36) if deep_ is None else deep_
    vertical = facing in ("down", "up")
    w, h = (long_, deep_) if vertical else (deep_, long_)
    x0, y0, x1, y1 = cx - w // 2, cy - h // 2, cx + w // 2, cy + h // 2

    d.rounded_rectangle([x0 - 7, y0 - 7, x1 + 7, y1 + 7], radius=10, fill=OUTLINE)
    d.rectangle([x0, y0, x1, y1], fill=BAY)

    cap, rail = 20, 13
    if vertical:
        d.rectangle([x0, y0, x0 + cap, y1], fill=CAP)
        d.rectangle([x1 - cap, y0, x1, y1], fill=CAP)
        d.rectangle([x0 + cap, y0, x0 + cap + rail, y1], fill=colour)
        d.rectangle([x1 - cap - rail, y0, x1 - cap, y1], fill=colour)
        _rollers(d, (x0 + cap + rail, y0, x1 - cap - rail, y1), horizontal=True)
    else:
        d.rectangle([x0, y0, x1, y0 + cap], fill=CAP)
        d.rectangle([x0, y1 - cap, x1, y1], fill=CAP)
        d.rectangle([x0, y0 + cap, x1, y0 + cap + rail], fill=colour)
        d.rectangle([x0, y1 - cap - rail, x1, y1 - cap], fill=colour)
        _rollers(d, (x0, y0 + cap + rail, x1, y1 - cap - rail), horizontal=False)

    # The chevron sits on the DECK side of the bay - the side facing the middle of the sprite -
    # pointing the way items move, so a player can read an input from an output at a glance.
    # Keying it off the travel direction put the output port's chevron off the bottom edge.
    a, b, gap = int(PX * 0.15), int(PX * 0.11), int(PX * 0.10)
    if vertical:
        inward = 1 if cy < H / 2 else -1
        base = (y1 + gap) if inward > 0 else (y0 - gap)
        tip = (cx, base + inward * b) if facing == ("down" if inward > 0 else "up") \
            else (cx, base)
        tip = (cx, base + (b if facing == "down" else -b))
        pts = [tip, (cx - a // 2, base), (cx + a // 2, base)]
        if (facing == "down") != (inward > 0):        # pointing back at its own bay
            pts = [(cx, base), (cx - a // 2, base + inward * b), (cx + a // 2, base + inward * b)]
    else:
        inward = 1 if cx < W / 2 else -1
        base = (x1 + gap) if inward > 0 else (x0 - gap)
        pts = [(base + (b if facing == "right" else -b), cy),
               (base, cy - a // 2), (base, cy + a // 2)]
        if (facing == "right") != (inward > 0):
            pts = [(base, cy), (base + inward * b, cy - a // 2), (base + inward * b, cy + a // 2)]
    d.polygon(pts, fill=colour, outline=OUTLINE)


def fin_bank(d, x0, y0, x1, y1, n=6):
    """The row of rounded vertical fin blocks VFE put across the front of their larger
    machines. Without it a 5x5 deck is mostly bare plate below the machine body."""
    d.rounded_rectangle([x0 - 14, y0 - 14, x1 + 14, y1 + 14], radius=16, fill=OUTLINE)
    d.rounded_rectangle([x0 - 8, y0 - 8, x1 + 8, y1 + 8], radius=12, fill=FIELD_DRK)
    gap = 12
    w = ((x1 - x0) - gap * (n - 1)) / n
    for i in range(n):
        a = x0 + i * (w + gap)
        d.rounded_rectangle([a - 5, y0 - 5, a + w + 5, y1 + 5], radius=14, fill=OUTLINE)
        d.rounded_rectangle([a, y0, a + w, y1], radius=10, fill=(104, 99, 92, 255))
        d.rounded_rectangle([a + 6, y0 + 6, a + w - 6, y0 + (y1 - y0) * 0.42],
                            radius=8, fill=(134, 128, 120, 255))
        d.rounded_rectangle([a + 6, y1 - (y1 - y0) * 0.22, a + w - 6, y1 - 6],
                            radius=8, fill=(68, 64, 59, 255))


def build(body_path, profile):
    """A chassis inset half a cell inside the canvas, with the port bays protruding out of it.

    Measured off VFE's own sprites: a thin lit strip around the chassis edge, a deck darker
    than anything standing on it, and bays that straddle the edge rather than sitting inside a
    wide pale border.
    """
    slab = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(slab)

    x0, y0, x1, y1 = MX, MY, W - MX - 1, H - MY - 1
    d.rounded_rectangle([x0 - 9, y0 - 9, x1 + 9, y1 + 9], radius=24, fill=OUTLINE)
    d.rounded_rectangle([x0, y0, x1, y1], radius=18, fill=FRAME)
    d.rounded_rectangle([x0 + 20, y0 + 20, x1 - 20, y1 - 20], radius=12, fill=FIELD)
    d.rectangle([x0 + 20, y0 + 20, x1 - 20, y0 + 44], fill=FIELD_LIT)
    d.rectangle([x0 + 20, y1 - 44, x1 - 20, y1 - 20], fill=FIELD_DRK)
    for i in range(1, CW):                                   # deck seams on the cell lines
        x = x0 + i * PX
        d.line([(x, y0 + 44), (x, y1 - 44)], fill=SEAM, width=4)

    body = to_vfe_palette(_fit.key(body_path), profile)
    max_w, max_h = int(CW * PX * 0.99), int(CH * PX * 0.58)
    scale = min(max_w / body.width, max_h / body.height)
    bw, bh = int(body.width * scale), int(body.height * scale)
    body = body.resize((bw, bh), Image.LANCZOS)
    slab.alpha_composite(body, ((W - bw) // 2, y0 + int(CH * PX * 0.33) - bh // 2))

    # The fin bank fills the front rows, the way VFE fill the front of their 5x5 machines.
    fin_bank(d, x0 + int(PX * 0.30), y0 + int(CH * PX * 0.67),
                x1 - int(PX * 0.30), y0 + int(CH * PX * 0.87), n=7)

    # Five bays across the back - the middle one is where gear goes in - one long bay down each
    # flank covering the two rear spot rows, and the single output bay at the front. The flanks
    # get one bay rather than one per cell: at VFE's port size a per-cell bay collides with the
    # back row's corner bay, and that corner cell is fed by either of them anyway.
    mid = CW // 2
    lip = int(PX * 0.08)
    for i in range(CW):
        cx = x0 + int((i + 0.5) * PX)
        port(slab, cx, y0 - lip, CYAN if i == mid else GREEN, "down")
    for cx, facing in ((x0 - lip, "right"), (x1 + lip, "left")):
        port(slab, cx, y0 + int(PX * 1.0), GREEN, facing, long_=int(PX * 1.55))
    port(slab, x0 + int((mid + 0.5) * PX), y1 + lip, ORANGE, "down")
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
