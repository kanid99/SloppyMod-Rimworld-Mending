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
from scipy import ndimage
from vfe_chassis import SEAM as VC_SEAM
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


def kill_chroma(im):
    """Neutralise magenta/violet pixels.

    LANCZOS rings: downscaling an orange accent against a dark neighbour overshoots each
    channel independently, and the undershoot on the warm channels next to the overshoot on
    blue lands as a violet fringe. It is created by the RESIZE, so it has to be cleaned after
    it, not before - which is why neutralising the source alone never shifted the count.
    """
    a = np.asarray(im).astype(np.float32)
    rgb, al = a[..., :3].copy(), a[..., 3]
    r_, g_, b_ = rgb[..., 0] / 255.0, rgb[..., 1] / 255.0, rgb[..., 2] / 255.0
    bad = (r_ > g_ + 0.04) & (b_ > g_ + 0.02)
    lum = rgb @ np.array([0.299, 0.587, 0.114])
    rgb[bad] = np.repeat(lum[..., None], 3, axis=-1)[bad]
    return Image.fromarray(np.dstack([rgb, al]).astype(np.uint8), 'RGBA')


def to_vfe_palette(im, profile):
    """Neutralise the body's colour and match its luminance to VFE's own distribution.

    Two things here are load-bearing:

      * The colour work runs on EVERY pixel, including fully transparent ones. The keyer only
        sets alpha - a transparent pixel keeps the model's magenta in its RGB - and LANCZOS
        resamples RGB and alpha independently, so that magenta gets pulled into the opaque rim
        when the body is scaled. Neutralising only the solid pixels is what left a coloured
        fringe all round the machine.
      * The luminance statistics still come from the SOLID pixels only, or the transparent
        surround would drag the histogram.
    """
    a = np.asarray(im).astype(np.float32)
    rgb, al = a[..., :3].copy(), a[..., 3]
    solid = al > 90
    if solid.sum() == 0:
        return im

    mx, mn = rgb.max(2), rgb.min(2)
    sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    # The accent test is on saturation alone, so whatever single colour the body carries
    # survives and every stray cast goes neutral - except magenta, which is the model's own
    # background and must never be treated as an accent.
    r_, g_, b_ = rgb[..., 0] / 255.0, rgb[..., 1] / 255.0, rgb[..., 2] / 255.0
    chroma = (r_ > g_ + 0.10) & (b_ > g_ + 0.05)
    accent = (sat > 0.42) & ~chroma          # VFE use orange in thin strips, not whole casings

    lum = rgb @ np.array([0.299, 0.587, 0.114])
    grey = np.repeat(lum[..., None], 3, axis=-1)
    rgb[~accent] = grey[~accent]

    # Histogram-match onto VFE's curve: same quantile, their value.
    src = lum[solid]
    order = np.argsort(src)
    ranks = np.empty(order.size, dtype=np.float64)
    ranks[order] = np.arange(order.size)
    q = ranks / max(1.0, order.size - 1)
    target = np.interp(q, np.linspace(0, 1, profile.size), profile)

    scale = np.ones_like(lum)
    scale[solid] = target / np.maximum(src, 1.0)
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


def symmetrise(im):
    """Mirror the body's left half onto its right.

    Every factory machine in VFE is mirror-symmetric about its vertical centre line. Asking the
    generator for symmetry gets it approximately; taking one half and reflecting it gets it
    exactly, and an exactly symmetric machine is most of what reads as "factory" rather than
    "object".
    """
    a = np.asarray(im)
    h, w = a.shape[:2]
    half = w // 2
    left = a[:, :half]
    out = np.concatenate([left, left[:, ::-1][:, w - 2 * half:]], axis=1) if w % 2 == 0 \
        else np.concatenate([left, a[:, half:half + 1], left[:, ::-1]], axis=1)
    return Image.fromarray(out[:, :w], 'RGBA')


def soften_rim(im, band=5):
    """Lift the body's own black silhouette ring to the chassis's dark grey.

    Measured against VFE: pure black belongs to the OUTER silhouette of the whole building and
    nowhere else. The generated body arrives with a heavy black ring of its own, and once it is
    composited onto the deck that ring is an interior outline - the one style cue the sprite was
    still breaking.
    """
    a = np.asarray(im).astype(np.float32)
    rgb, al = a[..., :3].copy(), a[..., 3]
    solid = al > 90
    rim = solid & ~ndimage.binary_erosion(solid, np.ones((3, 3)), iterations=band)
    lum = rgb @ np.array([0.299, 0.587, 0.114])
    hit = rim & (lum < 40)
    rgb[hit] = np.array(VC_SEAM, np.float32)
    return Image.fromarray(np.dstack([rgb, al]).astype(np.uint8), 'RGBA')


def build(body_path, profile):
    """The chassis, its structure and its ports are all drawn; only the centrepiece machine
    comes from the generator, and it is mirrored to be exactly symmetric."""
    import vfe_chassis as VC
    c = VC.Chassis(CW, CH, px=PX, margin=MARGIN)
    c.base()

    bay = [c.x0 + int(PX * 0.92), c.y0 + int(PX * 0.80),
           c.x1 - int(PX * 0.92), c.y0 + int(PX * 2.95)]
    c.flank_rails(c.y0 + int(PX * 0.86), c.y1 - int(PX * 0.86), width_cells=0.24, inset_cells=0.13)
    c.corner_blocks(size_cells=0.66, inset_cells=0.12)
    c.inner_bay(bay)

    body = to_vfe_palette(_fit.key(body_path), profile)
    bw_max, bh_max = bay[2] - bay[0] - int(PX * 0.12), bay[3] - bay[1] - int(PX * 0.12)
    scale = min(bw_max / body.width, bh_max / body.height)
    bw, bh = int(body.width * scale), int(body.height * scale)
    body = soften_rim(symmetrise(kill_chroma(body.resize((bw, bh), Image.LANCZOS))))
    c.paste(body, ((c.W - bw) // 2, (bay[1] + bay[3]) // 2 - bh // 2))

    # From the machine down to the output port: a matched block either side of a recessed
    # channel, centred on the same axis as the output bay.
    sy0, sy1 = c.y0 + int(PX * 3.22), c.y1 - int(PX * 0.30)
    for sx in (c.x0 + int(PX * 1.02), c.x1 - int(PX * 1.02) - int(PX * 1.05)):
        box = [sx, sy0, sx + int(PX * 1.05), sy1]
        c.block(box, lit=(148, 142, 135), dark=(76, 72, 67))
        c.slats([box[0] + int(PX * 0.14), box[1] + int(PX * 0.16),
                 box[2] - int(PX * 0.14), box[3] - int(PX * 0.16)], n=3)
    c.spine([c.W // 2 - int(PX * 0.31), sy0 + int(PX * 0.06),
             c.W // 2 + int(PX * 0.31), sy1 + int(PX * 0.12)])

    # Three bays on the intake edge - the item port at centre with a material port either side -
    # three more down each flank, and the single output bay opposite. Mirror-symmetric about the
    # vertical centre line, the way every one of their machines is.
    mid = CW // 2
    for i in (mid - 1, mid, mid + 1):
        c.port(c.x0 + int((i + 0.5) * PX), 'top', 'cyan' if i == mid else 'green')
    for side in ('left', 'right'):
        for j in (1, 2, 3):
            c.port(c.y0 + int((j + 0.5) * PX), side, 'green')
    c.port(c.x0 + int((mid + 0.5) * PX), 'bottom', 'orange')
    return c.image()


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
