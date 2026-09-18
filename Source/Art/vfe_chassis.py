"""The chassis and the ingress/egress ports, drawn to VFE Factory's measured anatomy.

Every number here was read off their sprites pixel by pixel rather than eyeballed. Their
machines are authored at 128 px per map cell; REF is that scale and K scales it to ours.

What the measurement actually showed, and what the old renderer got wrong:

  * PURE BLACK IS ONLY THE OUTER SILHOUETTE. Inside the sprite every division is a 3px
    (49,49,49) dark grey line - between two adjacent ports, around a port, between panels.
    The old renderer outlined every element in black, which is why our hard-edge density came
    out at 70 per unit side against their 9-35.
  * DEPTH IS TONE, NOT LINE. A roller is a six-step ramp from a (97,106,119) crown down to a
    (60,63,68) trough with no separator between rollers at all. The chassis face is a smooth
    138 -> 119 ramp over ~25px. Nothing is outlined to make it read as raised.
  * THE PORT EXACTLY FILLS THE HALF-CELL OVERHANG. Their drawSize is one cell larger than
    their size, and a port runs from the outer edge of that margin to the chassis face.
"""
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

REF = 128.0                       # VFE's pixels per map cell

# --- measured colours -------------------------------------------------------
BLACK      = (0, 0, 0)
SEAM       = (49, 49, 49)         # every internal division, 3px at REF
CAP        = (126, 126, 126)      # the pale end block either side of a bed
DECK       = (85, 78, 73)         # the chassis deck
FACE_HI    = (138, 134, 132)      # chassis face, lit edge
FACE_LO    = (119, 115, 113)      # chassis face, 25px further in
BAY_SHADOW = (50, 50, 50)

# A roller, as measured: fractions of one period and the colour each spans.
ROLLER = [(0.000, 0.250, (97, 106, 119)),
          (0.250, 0.312, (90,  96, 105)),
          (0.312, 0.625, (87,  92, 100)),
          (0.625, 0.875, (79,  83,  89)),
          (0.875, 0.938, (65,  68,  73)),
          (0.938, 1.000, (60,  63,  68))]
ROLLER_PERIOD = 16.0              # at REF
BED_W      = 78.0                 # a belt's roller bed, at REF - ports, runs and the
RAIL_W     = 7.0                  # spine all use these, so no join changes width
BED_MARGIN = 6.0                  # bed to the inside of the spine's frame
PROTRUDE   = 5.0                  # how far a bay sticks out past the chassis, at REF
BAY_DEPTH  = 41.0                 # its whole depth, lip to the inboard end of the bed

# Their machining bay's casings, sampled: a desaturated teal running (35,48,53) to (47,87,100),
# and it covers 16% of that sprite. It is the body accent VFE use, and having none is most of
# why ours measured a third as colourful as theirs.
# Subdued from the first pass: same hue, roughly a third less saturation, so the casings
# read as painted steel rather than as a colour accent.
TEAL_LIT = (80, 103, 110)
TEAL_DRK = (56, 72, 78)          # the top face ramps gently; the lift is the side face
# Sliced down one of their teal casings, top to deck: a 9-12px NEUTRAL GREY collar, then a 4-7px
# dark near-neutral rim, then a top face that ramps only ~12% across its whole height. The lift
# is carried by the collar and the rim, not by shading the face steeply.
PLINTH = (97, 94, 91)
WALL = (93, 93, 93)              # a casing's side wall, lit
WALL_SEAM = (35, 48, 53)         # the thin dark line where its top face meets that wall
CASING_RIM = (35, 48, 53)

RUN_RAIL = 0.80                   # how much of a port rail's tone it keeps once it is inside
RAILS = {'orange': ((175, 120, 65), (109, 79, 49)),
         'green':  ((91, 175, 94),  (57, 109, 59)),
         'cyan':   ((72, 168, 178), (45, 105, 111))}


class Chassis:
    def __init__(self, cw, ch, px=192, margin=0.5):
        self.cw, self.ch, self.px, self.margin = cw, ch, px, margin
        self.K = px / REF
        self.W = int((cw + 2 * margin) * px)
        self.H = int((ch + 2 * margin) * px)
        self.x0 = int(margin * px)
        self.y0 = int(margin * px)
        self.x1 = self.W - self.x0
        self.y1 = self.H - self.y0
        self.rgb = np.zeros((self.H, self.W, 3), np.float32)
        self.a = np.zeros((self.H, self.W), np.float32)

    def r(self, v):
        return v * self.K

    # -- primitives ----------------------------------------------------------
    def _mask(self, box, radius=0):
        m = Image.new('L', (self.W, self.H), 0)
        ImageDraw.Draw(m).rounded_rectangle(box, radius=radius, fill=255)
        return np.asarray(m).astype(np.float32) / 255.0

    def fill(self, box, colour, radius=0):
        m = self._mask(box, radius)
        self.rgb = self.rgb * (1 - m[..., None]) + np.array(colour, np.float32) * m[..., None]
        self.a = np.maximum(self.a, m)

    def vgrad(self, box, top, bottom, radius=0):
        """A smooth vertical ramp - the way VFE make a face read as lit."""
        m = self._mask(box, radius)
        ys = np.arange(self.H, dtype=np.float32)
        t = np.clip((ys - box[1]) / max(1.0, box[3] - box[1]), 0, 1)[:, None]
        g = np.array(top, np.float32) * (1 - t[..., None]) + np.array(bottom, np.float32) * t[..., None]
        self.rgb = self.rgb * (1 - m[..., None]) + g * m[..., None]
        self.a = np.maximum(self.a, m)

    def seam(self, p0, p1):
        d = ImageDraw.Draw(Image.new('L', (1, 1)))          # placeholder, keeps linters quiet
        m = Image.new('L', (self.W, self.H), 0)
        ImageDraw.Draw(m).line([p0, p1], fill=255, width=max(1, int(self.r(3))))
        m = np.asarray(m).astype(np.float32) / 255.0
        self.rgb = self.rgb * (1 - m[..., None]) + np.array(SEAM, np.float32) * m[..., None]
        self.a = np.maximum(self.a, m)

    def image(self):
        out = np.dstack([np.clip(self.rgb, 0, 255), np.clip(self.a * 255, 0, 255)])
        return Image.fromarray(out.astype(np.uint8), 'RGBA')

    def paste(self, img, at):
        im = np.asarray(img).astype(np.float32)
        h, w = im.shape[:2]
        x, y = at
        sub = self.rgb[y:y + h, x:x + w]
        al = im[..., 3:4] / 255.0
        self.rgb[y:y + h, x:x + w] = sub * (1 - al) + im[..., :3] * al
        self.a[y:y + h, x:x + w] = np.maximum(self.a[y:y + h, x:x + w], im[..., 3] / 255.0)

    # -- the chassis ---------------------------------------------------------
    def base(self, chamfer=0.30):
        """The chassis: a square with its corners cut.

        Measured across their sprites, a body fills 93% of its own bounding box on average and
        its corners are 71% opaque; a plain rounded square comes out at 99% and 96%, which is
        the very top of their range. Cutting the corners breaks the outline the way theirs do.
        """
        o = int(self.r(7))
        k = chamfer * self.px

        def octagon(inset):
            x0, y0 = self.x0 + inset, self.y0 + inset
            x1, y1 = self.x1 - inset, self.y1 - inset
            c = max(k - inset, 0)
            return [(x0 + c, y0), (x1 - c, y0), (x1, y0 + c), (x1, y1 - c),
                    (x1 - c, y1), (x0 + c, y1), (x0, y1 - c), (x0, y0 + c)]

        self._paint(self._poly_mask(octagon(-o)), BLACK)
        box = (self.x0, self.y0, self.x1, self.y1)
        self._paint_grad(self._poly_mask(octagon(0)), box, FACE_HI, FACE_LO)
        inset = int(self.r(16))
        self._paint_grad(self._poly_mask(octagon(inset)),
                         (self.x0 + inset, self.y0 + inset, self.x1 - inset, self.y1 - inset),
                         tuple(min(255, c + 8) for c in DECK), DECK)
        for i in range(1, self.cw):                              # deck seams on the cell lines
            x = self.x0 + i * self.px
            self.seam((x, self.y0 + inset + int(self.r(6))), (x, self.y1 - inset - int(self.r(6))))

    # -- structural modules -------------------------------------------------
    # The factory machines do not read as "a machine sitting on a plate". They are built from
    # repeated, mirrored structure: a block at each corner, a framed bay in the middle, rails
    # down the flanks. These draw that structure, so the symmetry is guaranteed rather than
    # left to whatever the generator returned.

    def block(self, box, lit=(164, 158, 150), dark=(84, 79, 74), rad=None):
        """One raised block: a soft top-light ramp into a dark lower edge, and nothing else.

        It used to carry a 3px grey border all round. Against a light top face that border is a
        step of well over a hundred levels, and with a block at each corner, a rail down each
        flank and a pair beside the spine it put the sprite's hard-edge count back above VFE's
        range. Their blocks meet the deck on tone alone; so do these.
        """
        rad = int(self.r(10)) if rad is None else rad
        x0, y0, x1, y1 = box
        self.vgrad([x0, y0, x1, y1], lit, dark, radius=rad)
        self.vgrad([x0 + self.r(7), y0 + self.r(6), x1 - self.r(7), y0 + (y1 - y0) * 0.40],
                   tuple(min(255, c + 16) for c in lit), lit, radius=int(self.r(7)))

    def corner_blocks(self, size_cells=0.82, inset_cells=0.10):
        w = size_cells * self.px
        i = inset_cells * self.px
        for cx in (self.x0 + i, self.x1 - i - w):
            for cy in (self.y0 + i, self.y1 - i - w):
                self.block([cx, cy, cx + w, cy + w])

    def flank_rails(self, y0, y1, width_cells=0.30, inset_cells=0.12):
        """A matched rail down each flank, mirrored - the repeated structure their machines
        carry between the corner housings."""
        w = width_cells * self.px
        i = inset_cells * self.px
        for x in (self.x0 + i, self.x1 - i - w):
            self.block([x, y0, x + w, y1], lit=(144, 138, 131), dark=(74, 70, 66))

    def slats(self, box, n=4, horizontal=True):
        """A row of soft slats on a block - the repeated plain detail their casings carry.
        Each slat is a gentle ramp, separated by the chassis's dark grey, never by black."""
        x0, y0, x1, y1 = box
        span = (y1 - y0) if horizontal else (x1 - x0)
        gap = self.r(7)
        w = (span - gap * (n - 1)) / n
        for i in range(n):
            a = (y0 if horizontal else x0) + i * (w + gap)
            b = a + w
            sub = [x0, a, x1, b] if horizontal else [a, y0, b, y1]
            self.vgrad(sub, (118, 112, 105), (80, 76, 71), radius=int(self.r(5)))

    def spine_width(self):
        """How wide a spine's box must be for its roller bed to match a port bay's."""
        return self.r(BED_W) + 2 * (self.r(BED_MARGIN) + self.r(RAIL_W))

    def spine(self, box, accent=(175, 120, 65), accent_after=None, split=None, ramp=None):
        """The belt that carries an item through the machine.

        One run from the intake port to the output port, so it reads as a single belt rather
        than three pieces of one. `split` is a canvas y at which the rail colour changes -
        cyan above, orange below, with the arm that does the work sitting over that line - and
        `ramp` is how far, as a fraction of the run, the bed takes to come up from the tone a
        port bay leaves it at to full brightness. A port bay's bed darkens 38% towards the
        machine, so without that the belt has a visible step in it where it leaves the bay.
        """
        x0, y0, x1, y1 = box
        assert abs((x1 - x0) - self.spine_width()) < 1.5, (
            "the spine's box must be spine_width() wide, or its bed comes out a different "
            "width from the port bays' and the belt visibly steps in where they meet")
        f = self.r(10)
        self.fill([x0 - f, y0 - f, x1 + f, y1 + f], SEAM, radius=int(self.r(10)))
        self.vgrad([x0, y0, x1, y1], (58, 55, 52), (78, 74, 70), radius=int(self.r(8)))

        rail = self.r(RAIL_W) if accent else 0.0
        if accent:
            lo, hi = y0 + self.r(6), y1 - self.r(6)
            cut = hi if (split is None or accent_after is None) else max(lo, min(hi, split))
            self.fill([x0 + self.r(6), lo, x1 - self.r(6), cut], accent)
            if accent_after is not None:
                self.fill([x0 + self.r(6), cut, x1 - self.r(6), hi], accent_after)
        bx0, bx1 = x0 + self.r(BED_MARGIN) + rail, x1 - self.r(BED_MARGIN) - rail

        period = self.r(ROLLER_PERIOD)
        d0, d1 = y0 + self.r(6), y1 - self.r(6)
        n = int((d1 - d0) / period) + 1
        for i in range(n):
            for f0, f1, col in ROLLER:
                a = d0 + (i + f0) * period
                b = min(d0 + (i + f1) * period, d1)
                if b <= a:
                    continue
                sh = 1.0
                if ramp:
                    t = (a - d0) / max(d1 - d0, 1.0)
                    sh = 0.62 + 0.38 * min(t / ramp, (1.0 - t) / ramp, 1.0)
                self.fill([bx0, a, bx1, b], tuple(v * sh for v in col))

    def work_slot(self, box, accent=(175, 120, 65)):
        """A dark recess with a thin accent lip along its lower edge - how their machines show
        a working face without drawing a light source."""
        x0, y0, x1, y1 = box
        self.vgrad([x0, y0, x1, y1], (58, 55, 52), (76, 72, 68), radius=int(self.r(6)))
        # Two faint tracks down the face, so it is not a flat black hole at full zoom.
        for f in (0.30, 0.70):
            x = x0 + (x1 - x0) * f
            self.fill([x - self.r(2), y0 + self.r(10), x + self.r(2), y1 - self.r(12)], (70, 67, 63))
        self.fill([x0 + self.r(6), y1 - self.r(9), x1 - self.r(6), y1 - self.r(3)], accent)

    def inner_bay(self, box, rad=None):
        """A recessed working bay with a chamfered frame - what the machine sits inside."""
        rad = int(self.r(12)) if rad is None else rad
        x0, y0, x1, y1 = box
        f = self.r(13)
        self.vgrad([x0 - f, y0 - f, x1 + f, y1 + f], (146, 141, 134), (104, 99, 93), radius=rad + int(f))
        # A mid grey, not the 49 used between two ports: against the bay frame's light face
        # a 49 step is over a hundred levels, and this ring runs the whole way round.
        self.fill([x0 - self.r(3), y0 - self.r(3), x1 + self.r(3), y1 + self.r(3)],
                  (82, 78, 74), radius=rad)
        self.vgrad([x0, y0, x1, y1], (70, 66, 62), (88, 83, 78), radius=rad)

    # -- polygon forms and greebles -----------------------------------------

    def _poly_mask(self, pts):
        m = Image.new('L', (self.W, self.H), 0)
        ImageDraw.Draw(m).polygon([(float(x), float(y)) for x, y in pts], fill=255)
        return np.asarray(m).astype(np.float32) / 255.0

    def _paint(self, m, colour):
        self.rgb = self.rgb * (1 - m[..., None]) + np.array(colour, np.float32) * m[..., None]
        self.a = np.maximum(self.a, m)

    def _paint_grad(self, m, box, top, bottom):
        ys = np.arange(self.H, dtype=np.float32)
        t = np.clip((ys - box[1]) / max(1.0, box[3] - box[1]), 0, 1)[:, None]
        g = np.array(top, np.float32) * (1 - t[..., None]) + np.array(bottom, np.float32) * t[..., None]
        self.rgb = self.rgb * (1 - m[..., None]) + g * m[..., None]
        self.a = np.maximum(self.a, m)

    def raised_casing(self, pts, lit=TEAL_LIT, dark=TEAL_DRK, lift=None):
        """A structure standing proud of the deck.

        Read off their machining bay at 1:1 rather than from a colour slice: the lift comes
        from a DARKER SIDE FACE along the lower edge plus a soft SHADOW cast down and right
        onto whatever is underneath. The first attempt put a grey collar round a dark rim -
        that was a neighbouring structure in the slice, not part of the casing, and it read as
        an inset panel rather than a raised one.
        """
        lift = self.r(13) if lift is None else lift
        off = self.r(7)

        top = self._poly_mask(pts)

        # The deck shadow the whole structure casts, down and to the right.
        shadow = self._poly_mask([(x + off, y + off * 1.15) for x, y in pts])
        shadow = ndimage.gaussian_filter(shadow, self.r(5))
        self.rgb *= (1 - np.clip(shadow - top, 0, 1)[..., None] * 0.36)

        # The wall itself is a LIGHT grey catching the same overhead light as the top face, with
        # a thin dark line where the face meets it. Their slice reads face, then (35,48,53) for
        # 4-7px, then (93,93,93) for 9-12px - so the dark is a seam, not the wall. Painting the
        # whole wall dark made the casings look burnt rather than raised.
        self._paint(self._poly_mask([(x, y + lift) for x, y in pts]), WALL)
        self._paint(self._poly_mask([(x, y + self.r(5)) for x, y in pts]), WALL_SEAM)

        xs = [p[0] for p in pts]
        ys = [p[1] for p in pts]
        self._paint_grad(top, (min(xs), min(ys), max(xs), max(ys)), lit, dark)

    def drum(self, cx, cy, r, lit=TEAL_LIT, dark=TEAL_DRK, bands=2):
        """A cylindrical tank seen from above.

        Their sprites lean on round forms - the distillery's vessel, the cannery's drum, the
        machining bay's rounded caps - and a sprite built only from rectangles measures near
        the floor of their diagonal-edge energy. A drum is the cheapest way to put curvature
        back in: side wall, cast shadow and banded top, the same construction as a casing.
        """
        def circle(ccx, ccy, rr):
            m = Image.new('L', (self.W, self.H), 0)
            ImageDraw.Draw(m).ellipse([ccx - rr, ccy - rr, ccx + rr, ccy + rr], fill=255)
            return np.asarray(m).astype(np.float32) / 255.0

        lift, off = self.r(13), self.r(7)
        top = circle(cx, cy, r)
        shadow = ndimage.gaussian_filter(circle(cx + off, cy + off * 1.15, r), self.r(5))
        self.rgb *= (1 - np.clip(shadow - top, 0, 1)[..., None] * 0.36)
        self._paint(circle(cx, cy + lift, r), WALL)
        self._paint(circle(cx, cy + self.r(5), r), WALL_SEAM)
        self._paint_grad(top, (cx - r, cy - r, cx + r, cy + r), lit, dark)
        for i in range(1, bands + 1):                      # concentric bands, faintly darker
            rr = r * (1 - i / (bands + 1.0))
            ring = circle(cx, cy, rr + self.r(1.5)) - circle(cx, cy, rr - self.r(1.5))
            self._paint_grad(np.clip(ring, 0, 1), (cx - r, cy - r, cx + r, cy + r),
                             tuple(int(v * 0.88) for v in lit), tuple(int(v * 0.91) for v in dark))
        cap = circle(cx, cy - r * 0.16, r * 0.30)          # a small lit cap on top
        self._paint_grad(cap, (cx - r, cy - r, cx + r, cy + r),
                         tuple(min(255, int(v * 1.18)) for v in lit), lit)

    def pipe(self, route, width=None, colour=PLINTH):
        """Grey pipework, as axis-aligned runs with square elbows. Drawn BEFORE the structures
        it links, so it passes under them and only shows in the gaps between."""
        w = (self.r(8) if width is None else width) / 2.0
        edge = tuple(int(v * 0.78) for v in colour)
        for (x0, y0), (x1, y1) in zip(route, route[1:]):
            a = [min(x0, x1) - w, min(y0, y1) - w, max(x0, x1) + w, max(y0, y1) + w]
            self.fill(a, edge, radius=int(w))
            self.fill([a[0] + self.r(2), a[1] + self.r(2), a[2] - self.r(2), a[3] - self.r(2)],
                      colour, radius=int(w))

    def slashes(self, x, y, n=4, length=None, colour=None, step=None, horizontal=True):
        """A little rack of short parallel lines - RimWorld's greeble of choice. Kept a couple
        of tone steps off its casing, never black, so it reads as surface detail at full zoom
        and disappears into the block at play zoom instead of turning into noise."""
        length = self.r(13) if length is None else length
        step = self.r(6) if step is None else step
        colour = (140, 150, 152) if colour is None else colour
        w = self.r(2.5)
        for i in range(n):
            o = i * step
            box = ([x, y + o, x + length, y + o + w] if horizontal
                   else [x + o, y, x + o + w, y + length])
            self.fill(box, colour)

    def studs(self, x0, x1, y, n=4, colour=(150, 144, 136)):
        """A row of small square pads."""
        s = self.r(3.5)
        span = (x1 - x0) * 0.62
        a = (x0 + x1) / 2 - span / 2
        for i in range(n):
            cx = a + span * (i + 0.5) / n
            self.fill([cx - s, y - s, cx + s, y + s], colour, radius=int(self.r(2)))

    def panel(self, box, colour=(96, 91, 86)):
        """A shallow inset panel: one flat tone a step below its surroundings."""
        self.fill(box, colour, radius=int(self.r(5)))

    def belt_run(self, along, side, d0, d1, width=None, shade0=0.62, shade1=0.62, rail=None):
        """A stretch of roller bed carrying on from a port bay.

        It defaults to the BAY'S OWN BED WIDTH and holds the brightness the bay ends at, flat,
        because the join has to be invisible. A narrower run stepped down from the bay and that
        change of width at the seam was the thing that read as jarring - VFE never change a
        belt's width along its length. Letting the run keep darkening past the bay was no better:
        it arrived at the chute as dark as the chute and the two merged.

        `rail` carries the port's own accent line on inward alongside the run, at RUN_RAIL of
        its tone. Without it the rail stopped at the bay's inner end, which left the accent as a
        stub on the outside of the building; carried through, the two lines converge on the
        chute and the bay reads as the mouth of an angled channel rather than a painted mark.
        Dimming is what sells it - a rail at full tone the whole way just looks like a long
        stripe, and the step from lit to dim is where the lip appears to turn.
        """
        w = (self.r(BED_W) if width is None else width) / 2.0
        face = {'top': self.y0, 'bottom': self.y1, 'left': self.x0, 'right': self.x1}[side]
        inward = 1.0 if side in ('top', 'left') else -1.0
        vertical = side in ('top', 'bottom')
        a, b = face + inward * d0, face + inward * d1
        span = max(abs(b - a), 1.0)

        def rect(a0, a1, e0, e1):
            box = [a0, e0, a1, e1] if vertical else [e0, a0, e1, a1]
            return [min(box[0], box[2]), min(box[1], box[3]), max(box[0], box[2]), max(box[1], box[3])]

        if rail is not None:
            # laid down first and at the port's own half-width, so the rollers cover the middle
            # and leave exactly the rail_w strip either side that a bay leaves.
            dim = tuple(v * RUN_RAIL for v in RAILS[rail][0])
            self.fill(rect(along - w - self.r(RAIL_W), along + w + self.r(RAIL_W), a, b), dim)

        period = self.r(ROLLER_PERIOD)
        n = int(span / period) + 1
        for i in range(n):
            for f0, f1, col in ROLLER:
                e0 = a + inward * (i + f0) * period
                e1 = a + inward * (i + f1) * period
                if abs(e0 - a) > span:
                    continue
                if abs(e1 - a) > span:
                    e1 = b
                t = min(1.0, abs(e0 - a) / span)
                sh = shade0 + (shade1 - shade0) * t
                box = rect(along - w, along + w, e0, e1)
                if box[2] - box[0] >= 1 and box[3] - box[1] >= 1:
                    self.fill(box, tuple(v * sh for v in col))

    def mouth(self, along, side, depth_at, width=None, deep=None):
        """The dark opening a belt runs into.

        Their conveyor oven does exactly this: a run ends under a raised lip and disappears
        into a dark grid-lined recess. Ours is the same idea - a lit lip across the far end, a
        dark throat, and a shadow where the belt goes under."""
        w = (self.r(BED_W) if width is None else width) / 2.0
        deep = self.r(30) if deep is None else deep
        face = {'top': self.y0, 'bottom': self.y1, 'left': self.x0, 'right': self.x1}[side]
        inward = 1.0 if side in ('top', 'left') else -1.0
        vertical = side in ('top', 'bottom')
        a = face + inward * depth_at
        b = a + inward * deep

        def rect(a0, a1, e0, e1):
            box = [a0, e0, a1, e1] if vertical else [e0, a0, e1, a1]
            return [min(box[0], box[2]), min(box[1], box[3]), max(box[0], box[2]), max(box[1], box[3])]

        # The chute is the SAME WIDTH as the belt feeding it, so the channel never steps in or
        # out along its length. A lit lip runs across its mouth - their oven's chute is a wide
        # dark recess under exactly such a lip - and the throat behind it is broken up by grid
        # lines rather than being a flat black hole.
        lip = self.r(7)
        self.fill(rect(along - w, along + w, a, a + inward * lip), (118, 113, 107))
        self.fill(rect(along - w, along + w, a + inward * lip, b), (46, 48, 50))
        self.fill(rect(along - w, along + w, a + inward * lip,
                       a + inward * (lip + self.r(5))), (28, 29, 31))
        for f in (0.34, 0.66):
            e = a + inward * (lip + deep * f)
            self.fill(rect(along - w + self.r(4), along + w - self.r(4), e,
                           e + inward * self.r(3)), (72, 74, 77))

    # -- one ingress/egress port --------------------------------------------
    def port(self, along, side, rail, chevron=True, outward=False):
        """`along` is the port's centre on the edge's own axis, in canvas pixels.

        Widths, in REF pixels and therefore in VFE's own proportions:
          seam 3 | cap 12 | rail 7 | bed 78 | rail 7 | cap 12 | seam 3   = 122 of a 128 pitch

        DEPTH, measured off their assembler rather than assumed: the chassis edge is at y=66,
        the port's black lip starts at y=58 and its roller bed runs y=65 to y=99. So the bay is
        41px deep and only EIGHT of those - a sixteenth of a cell - stick out past the chassis.
        The rest is cut into it. An earlier version ran the bay across the whole half-cell
        overhang, which left the bays sitting outside the building instead of in it.
        """
        bed, rail_w, cap_w = self.r(BED_W), self.r(RAIL_W), self.r(12)
        half = self.px / 2.0
        seam_w = half - (bed / 2 + rail_w + cap_w)
        lo, hi = RAILS[rail]

        vertical = side in ('top', 'bottom')
        face = {'top': self.y0, 'bottom': self.y1, 'left': self.x0, 'right': self.x1}[side]
        inward = 1.0 if side in ('top', 'left') else -1.0     # from that edge towards the deck
        outer = face - inward * self.r(PROTRUDE)              # barely past the chassis
        deep = face + inward * self.r(BAY_DEPTH - PROTRUDE)   # and well into it

        def rect(a0, a1, d0, d1):
            """across (a) and depth (d) -> a canvas box, whichever edge this port is on."""
            box = [a0, d0, a1, d1] if vertical else [d0, a0, d1, a1]
            return [min(box[0], box[2]), min(box[1], box[3]), max(box[0], box[2]), max(box[1], box[3])]

        self.fill(rect(along - half, along + half, outer, deep), SEAM)
        self.fill(rect(along - half + seam_w, along + half - seam_w, outer, deep), CAP)
        self.fill(rect(along - bed / 2 - rail_w, along + bed / 2 + rail_w, outer, deep), lo)
        self._bed(along, bed, outer, deep, inward, rect)

        # The bay's outer lip is part of the machine's silhouette, so it - and only it - is black.
        # Long enough to meet the chassis outline either side of the bay, so the silhouette
        # stays unbroken where a port cuts through it.
        self.fill(rect(along - half, along + half, outer, outer + inward * self.r(11)), BLACK)

        if chevron:
            # The arrow follows the MATERIAL, not the edge. Keying it off the edge alone made
            # every arrow on the sprite point inwards, including the output's - so the machine
            # read as taking things in on all four sides and never putting anything out. VFE's
            # point with the flow: in at the top, out at the bottom, same direction throughout.
            self._chevron(along, face, inward, vertical, lo, bed, outward=outward)

    def _bed(self, along, bed, outer, face, inward, rect):
        """Rollers: a six-step ramp per roller, no separator between them, darkening towards
        the machine. VFE never put a line between two rollers - the trough IS the line."""
        depth = abs(face - outer)
        period = self.r(ROLLER_PERIOD)
        start = outer + inward * self.r(7)                 # just under the black lip
        n = int(depth / period) + 1
        for i in range(n):
            for f0, f1, col in ROLLER:
                d0 = start + inward * (i + f0) * period
                d1 = start + inward * (i + f1) * period
                t = min(1.0, (i + f0) * period / max(depth - self.r(7), 1.0))
                col = tuple(v * (1.0 - 0.38 * t) for v in col)
                box = rect(along - bed / 2, along + bed / 2, d0, d1)
                lo_d, hi_d = min(outer, face), max(outer, face)
                if rect is None:
                    continue
                if box[3] - box[1] < 1 or box[2] - box[0] < 1:
                    continue
                # clip to the bay
                if abs(d0 - outer) > depth:
                    continue
                self.fill(box, col)

    def _chevron(self, along, face, inward, vertical, colour, bed, outward=False):
        """A flat triangle on the chassis face, no outline - exactly how VFE mark flow."""
        # On the bay itself, at its inboard end. Their machines have bare chassis around a port
        # to put the chevron on; ours is packed with machinery, so a chevron out on the deck
        # lands on top of a casing.
        h, w = self.r(11), bed * 0.30
        base = face + inward * self.r(BAY_DEPTH - PROTRUDE - 15)
        if outward:                      # an egress arrow starts inboard and points out
            base, h = base + inward * h, -h
        tip = base + inward * h
        pts = ([(along, tip), (along - w / 2, base), (along + w / 2, base)] if vertical
               else [(tip, along), (base, along - w / 2), (base, along + w / 2)])
        m = Image.new('L', (self.W, self.H), 0)
        ImageDraw.Draw(m).polygon(pts, fill=255)
        m = np.asarray(m).astype(np.float32) / 255.0
        self.rgb = self.rgb * (1 - m[..., None]) + np.array(colour, np.float32) * m[..., None]
        self.a = np.maximum(self.a, m)
