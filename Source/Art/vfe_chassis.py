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
    def base(self):
        o = int(self.r(7))                       # outer black, measured 6-7px at REF
        rad = int(self.r(14))
        self.fill([self.x0 - o, self.y0 - o, self.x1 + o, self.y1 + o], BLACK, radius=rad + o)
        # The face: one smooth ramp across the whole deck, no bands, no outlines.
        self.vgrad([self.x0, self.y0, self.x1, self.y1], FACE_HI, FACE_LO, radius=rad)
        inset = int(self.r(16))
        self.vgrad([self.x0 + inset, self.y0 + inset, self.x1 - inset, self.y1 - inset],
                   tuple(min(255, c + 8) for c in DECK), DECK, radius=int(self.r(9)))
        for i in range(1, self.cw):              # deck seams on the cell lines
            x = self.x0 + i * self.px
            self.seam((x, self.y0 + inset + int(self.r(6))), (x, self.y1 - inset - int(self.r(6))))

    def deck_plates(self, d0, d1, n=5):
        """A row of shallow raised plates across the front of the deck, modelled by tone alone -
        a soft light-to-dark ramp per plate and a thin grey line between them, no outlines."""
        inset = int(self.r(16))
        x0, x1 = self.x0 + inset + int(self.r(10)), self.x1 - inset - int(self.r(10))
        gap = self.r(6)
        w = ((x1 - x0) - gap * (n - 1)) / n
        for i in range(n):
            a = x0 + i * (w + gap)
            self.vgrad([a, d0, a + w, d1], (112, 106, 99), (78, 73, 68), radius=int(self.r(6)))
            self.vgrad([a + self.r(5), d0 + self.r(4), a + w - self.r(5), d0 + (d1 - d0) * 0.34],
                       (126, 120, 112), (110, 104, 97), radius=int(self.r(5)))

    # -- one ingress/egress port --------------------------------------------
    def port(self, along, side, rail, chevron=True):
        """`along` is the port's centre on the edge's own axis, in canvas pixels.

        Widths, in REF pixels and therefore in VFE's own proportions:
          seam 3 | cap 12 | rail 7 | bed 78 | rail 7 | cap 12 | seam 3   = 122 of a 128 pitch
        The bay runs the full half-cell overhang, from the canvas edge to the chassis face.
        """
        # Proportions of one cell, measured: bed .609, rail .055, cap .094, and the rest is the
        # divider. half is exactly half a cell so neighbouring ports sit flush, the way VFE's do -
        # their adjacent bays share one 6px divider rather than leaving a gap between them.
        bed, rail_w, cap_w = self.r(78), self.r(7), self.r(12)
        half = self.px / 2.0
        seam_w = half - (bed / 2 + rail_w + cap_w)
        lo, hi = RAILS[rail]

        vertical = side in ('top', 'bottom')
        outer = {'top': self.y0 - self.margin * self.px, 'bottom': self.y1 + self.margin * self.px,
                 'left': self.x0 - self.margin * self.px, 'right': self.x1 + self.margin * self.px}[side]
        face = {'top': self.y0, 'bottom': self.y1, 'left': self.x0, 'right': self.x1}[side]
        inward = 1.0 if face > outer else -1.0        # from the canvas edge towards the deck

        def rect(a0, a1, d0, d1):
            """across (a) and depth (d) -> a canvas box, whichever edge this port is on."""
            box = [a0, d0, a1, d1] if vertical else [d0, a0, d1, a1]
            return [min(box[0], box[2]), min(box[1], box[3]), max(box[0], box[2]), max(box[1], box[3])]

        self.fill(rect(along - half, along + half, outer, face), SEAM)
        self.fill(rect(along - half + seam_w, along + half - seam_w, outer, face), CAP)
        self.fill(rect(along - bed / 2 - rail_w, along + bed / 2 + rail_w, outer, face), lo)
        self._bed(along, bed, outer, face, inward, rect)

        # The bay's outer lip is part of the machine's silhouette, so it - and only it - is black.
        lip = self.r(7)
        self.fill(rect(along - half, along + half, outer, outer + inward * lip), BLACK)

        if chevron:
            self._chevron(along, face, inward, vertical, lo, bed)

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

    def _chevron(self, along, face, inward, vertical, colour, bed):
        """A flat triangle on the chassis face, no outline - exactly how VFE mark flow."""
        gap, h, w = self.r(9), self.r(13), bed * 0.34
        base = face + inward * gap
        tip = base + inward * h
        pts = ([(along, tip), (along - w / 2, base), (along + w / 2, base)] if vertical
               else [(tip, along), (base, along - w / 2), (base, along + w / 2)])
        m = Image.new('L', (self.W, self.H), 0)
        ImageDraw.Draw(m).polygon(pts, fill=255)
        m = np.asarray(m).astype(np.float32) / 255.0
        self.rgb = self.rgb * (1 - m[..., None]) + np.array(colour, np.float32) * m[..., None]
        self.a = np.maximum(self.a, m)
