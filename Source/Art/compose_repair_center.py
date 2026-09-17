"""Draws the repair centre's sprite: the chassis, its machinery and its conveyor bays.

Every proportion and colour here was measured off VFE Factory's own sprites rather than
described from memory - see Source/Art/README.md and vfe_chassis.py for what was measured and
why. Nothing in this file is generated.
"""
import numpy as np
import os
from PIL import Image
from vfe_chassis import Chassis, TEAL_LIT, TEAL_DRK

PX = 192                      # pixels per map cell
CW, CH = 5, 5                 # the repair centre's FOOTPRINT, in cells
# VFE draw a 5x5 machine at drawSize (6,6): the art overhangs the footprint by half a cell on
# every side. Their conveyor bays are cut into the chassis and stick out only a sliver into that
# overhang - the room is there so the chassis can run to the footprint's full width.
MARGIN = 0.5


def build():
    """The whole sprite, drawn.

    No image generator is involved. Its machines came back RENDERED - photoreal metal, fine
    bevels, a heavy black outline of their own - and VFE's are ABSTRACT: plain blocks, flat
    tonal ramps, and detail only as small repeated marks. Drawing the interior gets that
    directly, and it disposes of two defects the generated body kept dragging in: a violet
    fringe from resampling its magenta ground, and its own black silhouette ring sitting
    inside ours as an interior outline.

    The layout is a ring of chamfered casings around a central machine, with a roller run in
    from the item bay and another out to the output bay.
    """
    c = Chassis(CW, CH, px=PX, margin=MARGIN)
    c.base()

    def cell(cx, cy, w, h):
        """A box given in cells from the chassis's top-left corner."""
        return [c.x0 + cx * PX, c.y0 + cy * PX, c.x0 + (cx + w) * PX, c.y0 + (cy + h) * PX]

    def P(cx, cy):
        return (c.x0 + cx * PX, c.y0 + cy * PX)

    c.corner_blocks(size_cells=0.58, inset_cells=0.08)
    for sx in (0.14, 5 - 0.14 - 0.46):                 # a greeble on each corner housing
        for sy in (0.16, 5 - 0.16 - 0.10):
            c.slashes(*P(sx, sy), n=3, colour=(136, 130, 123))

    # A ring of chamfered teal casings: one inboard of each flank bay, and one either side of
    # the conveyor top and bottom. Every one is mirrored across the centre line.
    def pentagon(cx, cy, w, h, point):
        """A casing with its corner cut away towards `point` - the ring's inward-facing face."""
        x0, y0, x1, y1 = cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2
        k = min(w, h) * 0.42
        cuts = {'tl': [(x0 + k, y0), (x1, y0), (x1, y1), (x0, y1), (x0, y0 + k)],
                'tr': [(x0, y0), (x1 - k, y0), (x1, y0 + k), (x1, y1), (x0, y1)],
                'bl': [(x0, y0), (x1, y0), (x1, y1), (x0 + k, y1), (x0, y1 - k)],
                'br': [(x0, y0), (x1, y0), (x1, y1 - k), (x1 - k, y1), (x0, y1)]}[point]
        return [P(0, 0)[0] + 0, 0] and cuts

    W_, H_ = 0.92 * PX, 0.80 * PX
    for side, sx, corner_in in ((-1, 0.84, 'r'), (1, 5 - 0.84, 'l')):
        for cy, vert in ((1.5, 't'), (2.5, None), (3.5, 'b')):
            x, y = c.x0 + sx * PX, c.y0 + cy * PX
            if vert is None:                            # the middle one is a plain casing
                c.poly_block([(x - W_ / 2, y - H_ / 2), (x + W_ / 2, y - H_ / 2),
                              (x + W_ / 2, y + H_ / 2), (x - W_ / 2, y + H_ / 2)])
            else:
                c.poly_block(pentagon(x, y, W_, H_, vert + corner_in))
            c.slashes(x - W_ * 0.26, y - H_ * 0.20, n=4, colour=(118, 142, 148))
            c.slashes(x - W_ * 0.26, y + H_ * 0.10, n=2, colour=(168, 122, 74))

    for cx, corner in ((1.42, 'r'), (5 - 1.42, 'l')):   # flanking the conveyor, top and bottom
        for cy, vert in ((0.72, 'b'), (5 - 0.72, 't')):
            x, y = c.x0 + cx * PX, c.y0 + cy * PX
            c.poly_block(pentagon(x, y, 0.86 * PX, 0.68 * PX, vert + corner))
            c.slashes(x - PX * 0.20, y - PX * 0.10, n=3, colour=(118, 142, 148))

    # The machine: one plain block, a slat panel, a dark working face, and a little hardware.
    body = cell(1.58, 1.34, 1.84, 2.28)
    c.block(body, lit=(168, 162, 154), dark=(88, 83, 78))
    c.slats([body[0] + int(PX * 0.20), body[1] + int(PX * 0.18),
             body[2] - int(PX * 0.20), body[1] + int(PX * 0.96)], n=6, horizontal=False)
    c.studs(body[0] + int(PX * 0.24), body[2] - int(PX * 0.24), body[1] + int(PX * 1.12), n=5)
    c.work_slot([body[0] + int(PX * 0.14), body[1] + int(PX * 1.32),
                 body[2] - int(PX * 0.14), body[3] - int(PX * 0.12)])

    # In from the item bay, out to the output bay.
    c.spine(cell(2.5 - 0.31, 0.26, 0.62, 1.14))
    c.spine(cell(2.5 - 0.31, 3.58, 0.62, 1.42))

    # Three bays on the intake edge - the item port at centre with a material port either side -
    # three more down each flank, and the single output bay opposite. Mirror-symmetric about the
    # vertical centre line, the way every one of their machines is.
    mid = CW // 2
    for i in (mid - 1, mid, mid + 1):
        c.port(c.x0 + int((i + 0.5) * PX), 'top', 'cyan' if i == mid else 'green')
    for side in ('left', 'right'):
        for j in (1, 2, 3):
            c.port(c.y0 + int((j + 0.5) * PX), side, 'green')
    c.port(c.x0 + int((mid + 0.5) * PX), 'bottom', 'orange', outward=True)
    return c.image()


def contrast(im):
    a = np.asarray(im).astype(np.float32)
    vis = a[..., 3] > 90
    v = (a[..., :3].mean(2) / 255.0)[vis]
    return v.std(), float(np.percentile(v, 99) * 255), float(np.percentile(v, 1) * 255)


def export(outdir):
    """The sprite is drawn intake-up, which is the NORTH view: MenderSpots takes material in on
    the side the building faces, so a north-facing machine feeds from its north edge.

    south is its 180 turn, and east its quarter turn clockwise - PIL's rotate() is
    counter-clockwise for a positive angle, so -90 is what carries the intake edge to the right,
    where a machine facing east wants it. Getting that sign wrong is not cosmetic: it drew the
    item port on the cell the C# reads as the output, which is exactly what the shipped east
    texture did until this was verified cell by cell against MenderSpots.
    """
    sprite = build()
    os.makedirs(outdir, exist_ok=True)
    sprite.save(f"{outdir}/AutomatedMender_north.png")
    sprite.rotate(180, expand=True).save(f"{outdir}/AutomatedMender_south.png")
    sprite.rotate(-90, expand=True).save(f"{outdir}/AutomatedMender_east.png")
    return sprite


if __name__ == "__main__":
    im = export("export")
    st, p99, p1 = contrast(im)
    print(f"contrast {st:.3f}  p1 {p1:.0f}  p99 {p99:.0f}   (VFE: 0.151 / 0 / 196)")
    print("wrote export/AutomatedMender_{north,south,east}.png")
