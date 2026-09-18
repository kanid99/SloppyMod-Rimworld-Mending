"""Draws the repair centre's sprite: the chassis, its machinery and its conveyor bays.

Every proportion and colour here was measured off VFE Factory's own sprites rather than
described from memory - see Source/Art/README.md and vfe_chassis.py for what was measured and
why. Nothing in this file is generated.
"""
import numpy as np
import os
from PIL import Image
from vfe_chassis import Chassis, RAILS, TEAL_LIT, TEAL_DRK

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

    # Grey pipework linking the ring, laid down first so the casings sit on top of it and it
    # only shows in the gaps between them - which is what keeps it subtle. Straight runs only;
    # routing it round corners produced little hooks that read as debris at play zoom.
    for sx, tx, inner in ((1.00, 1.5, 1.44), (5 - 1.00, 5 - 1.5, 5 - 1.44)):
        c.pipe([P(sx, 0.88), P(sx, 4.12)])                 # the spine down the flank
        for row in (1.5, 2.5, 3.5):                        # stubs into the machine
            c.pipe([P(sx, row), P(inner, row)])
        for row in (0.88, 4.12):                           # and out to the corner casings
            c.pipe([P(sx, row), P(tx, row)])

    # The casings sit a third of a cell further in than they did, so the belt from each bay
    # has somewhere to travel before it reaches the hatch. Butted up against the bay there
    # was no run to see and the whole connection read as one dark notch.
    W_, H_ = 0.84 * PX, 0.88 * PX
    for side, sx, corner_in in ((-1, 1.00, 'r'), (1, 5 - 1.00, 'l')):
        for cy, vert in ((1.5, 't'), (2.5, None), (3.5, 'b')):
            x, y = c.x0 + sx * PX, c.y0 + cy * PX
            if vert is None:                            # the middle one is a plain casing
                c.raised_casing([(x - W_ / 2, y - H_ / 2), (x + W_ / 2, y - H_ / 2),
                                 (x + W_ / 2, y + H_ / 2), (x - W_ / 2, y + H_ / 2)])
            else:
                c.raised_casing(pentagon(x, y, W_, H_, vert + corner_in))
            # On the inboard half, clear of the hatch in the outer edge.
            c.slashes(x + W_ * 0.06, y - H_ * 0.22, n=4, colour=(118, 142, 148))
            c.slashes(x + W_ * 0.06, y + H_ * 0.08, n=2, colour=(168, 122, 74))

    # Lined up on the two material bays, so a run can drop straight into each one.
    for cx, corner in ((1.5, 'r'), (5 - 1.5, 'l')):
        for cy, vert in ((0.92, 'b'), (5 - 0.92, 't')):
            x, y = c.x0 + cx * PX, c.y0 + cy * PX
            c.raised_casing(pentagon(x, y, 0.96 * PX, 0.70 * PX, vert + corner))
            c.slashes(x - PX * 0.20, y - PX * 0.10, n=3, colour=(118, 142, 148))

    # The machine. Three things it has to say, which the old flat slab said none of:
    #   * work passes THROUGH it - the belt runs unbroken from the item bay to the output bay
    #     rather than stopping at a solid block,
    #   * something acts on the work - a gantry straddles the belt with a tool head centred on
    #     it, which is the clearest "this is a machine that does something to an item" there is
    #     in a top-down view,
    #   * it stands on the deck - side walls and a cast shadow, the same lift the casings get.
    # ONE belt, intake port to output port, passing under the arm that does the work. The rails
    # change colour where it goes under: cyan on the way in, which is the gear-in port's own
    # rail colour, orange on the way out, which is the output port's. That single line is the
    # whole machine's job stated in one read - a damaged item goes in at the top, something
    # acts on it in the middle, a repaired one leaves at the bottom.
    #
    # It was three separate segments before, one of them unaccented, which read as three
    # belts rather than one. The gantry is drawn after this, so it passes over the belt.
    GANTRY_MID = c.y0 + 2.27 * PX
    c.spine(cell(2.5 - 0.31, 0.26, 0.62, 4.74), accent=RAILS['cyan'][0],
            accent_after=RAILS['orange'][0], split=GANTRY_MID, ramp=0.19)

    GREY_LIT, GREY_DRK = (158, 152, 145), (108, 103, 98)
    for hx in (1.40, 5 - 1.40 - 0.76):                  # a housing either side of the belt
        box = cell(hx, 1.22, 0.76, 2.44)
        c.raised_casing([(box[0], box[1]), (box[2], box[1]), (box[2], box[3]), (box[0], box[3])],
                        lit=GREY_LIT, dark=GREY_DRK)
        # Stop the slats above the gantry: running them under it clipped the last one.
        c.slats([box[0] + int(PX * 0.12), box[1] + int(PX * 0.16),
                 box[2] - int(PX * 0.12), box[1] + int(PX * 0.66)], n=4)
        c.studs(box[0] + int(PX * 0.14), box[2] - int(PX * 0.14), box[1] + int(PX * 1.20), n=3)
        c.panel([box[0] + int(PX * 0.13), box[1] + int(PX * 1.38),
                 box[2] - int(PX * 0.13), box[3] - int(PX * 0.24)], colour=(124, 119, 113))
        c.slashes(box[0] + int(PX * 0.22), box[1] + int(PX * 1.52), n=4,
                  colour=(150, 144, 137))

    # The gantry bridges both housings across the belt; the head sits on its centre line.
    gantry = cell(1.24, 1.96, 2.52, 0.62)
    c.raised_casing([(gantry[0], gantry[1]), (gantry[2], gantry[1]),
                     (gantry[2], gantry[3]), (gantry[0], gantry[3])],
                    lit=(176, 170, 162), dark=(116, 111, 105))
    c.slats([gantry[0] + int(PX * 0.16), gantry[1] + int(PX * 0.14),
             gantry[2] - int(PX * 0.16), gantry[3] - int(PX * 0.16)], n=9, horizontal=False)

    head = cell(2.5 - 0.30, 2.02, 0.60, 0.50)
    c.raised_casing([(head[0], head[1]), (head[2], head[1]), (head[2], head[3]), (head[0], head[3])],
                    lit=(148, 143, 136), dark=(92, 88, 83))
    c.work_slot([head[0] + int(PX * 0.07), head[1] + int(PX * 0.09),
                 head[2] - int(PX * 0.07), head[3] - int(PX * 0.06)])

    # Every bay that feeds a casing runs into a hatch in it: a stretch of roller bed across the
    # deck, ending in a dark mouth cut into the casing's edge under a lit lip. Their conveyor
    # oven does the same thing - a run should disappear into the machine, not stop on bare deck.
    BAY_END = 36 * (PX / 128.0)                 # where a port bay's roller bed ends, in px
    for side in ('left', 'right'):
        for j in (1, 2, 3):
            along = c.y0 + int((j + 0.5) * PX)
            c.belt_run(along, side, BAY_END, 0.58 * PX)
            c.mouth(along, side, 0.58 * PX)
    for i in (CW // 2 - 1, CW // 2 + 1):        # the two material bays on the intake edge
        along = c.x0 + int((i + 0.5) * PX)
        c.belt_run(along, 'top', BAY_END, 0.55 * PX)
        c.mouth(along, 'top', 0.55 * PX)

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
