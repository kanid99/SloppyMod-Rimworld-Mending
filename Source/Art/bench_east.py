"""The east rotation, built rather than rotated.

The camera does not turn with the building, so a 90 degree turn of the finished north sprite
would put the front strip and the feet on one side. The slab is vanilla's own east tailor
bench instead (TableTailorHand_east.psd), and each object is moved to its quarter-turned
position on it.
"""
import os
import numpy as np
from PIL import Image, ImageFilter
import bench_compose as V

CANVAS_E = (V.CANVAS_SIZE[1], V.CANVAS_SIZE[0])               # 96 x 224
TARGET_E = (CANVAS_E[0] * V.SCALE, CANVAS_E[1] * V.SCALE)     # 288 x 672
CELL = 64
EAST_PSD = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'TableTailorHand_east.psd')


class BenchEast:
    def __init__(self, layers):
        self.L = layers
        x0, y0, x1, y1 = layers['border'].bbox
        self.SX0, self.SY0, self.SW, self.SH = x0, y0, x1 - x0, y1 - y0
        # The slab is vanilla's own east tailor bench, taken as drawn: one cell wide, its top
        # three cells long, then the front strip and the two feet below the footprint.
        self.E = V.load(EAST_PSD)
        ex0, ey0, ex1, ey1 = self.E['border'].bbox
        self.EX0, self.EY0 = ex0, ey0
        self.W, self.H = ex1 - ex0, ey1 - ey0          # 64 x 201
        self.art = Image.new('RGBA', (self.W, self.H), (0, 0, 0, 0))
        self.mask = Image.new('RGBA', (self.W, self.H), (0, 0, 0, 0))

    def _layer(self, name):
        l = self.L[name]
        return l.composite(viewport=V.CANVAS).crop(l.bbox), l.bbox

    def slab(self):
        base = None
        for name in ('border', 'base'):
            l = self.E[name]
            img = l.composite(viewport=(0, 0) + CANVAS_E).crop(l.bbox)
            self._put(img, (l.bbox[0] - self.EX0, l.bbox[1] - self.EY0), slab=True)
            if name == 'base':
                base, top = img, l.bbox[1] - self.EY0
        self.INSET = self.E['base'].bbox[0] - self.EX0

        # Where the worktop ends and the darker front strip begins, measured down the middle
        # of the base layer. Objects stay on the worktop.
        col = np.asarray(base.convert('L'))[:, base.size[0] // 2].astype(int)
        drop = next(y for y in range(len(col) - 1, 0, -1) if col[y - 1] - col[y] > 30)
        self.FACE = top + drop                       # slab-space y of the worktop's front edge
        self.stretch = 1.0
        self.along = self.FACE / V.LAYOUT_W

    def _put(self, img, at, slab=False):
        self.art.alpha_composite(img, at)
        self.mask.alpha_composite(V.as_mask(img, red=slab), at)

    def _place(self, img, along, across, shadow=True):
        """`along` runs the length of the bench, `across` its depth - the same two numbers the
        north layout uses. A quarter turn swaps the axes; it never turns the object."""
        w, h = img.size
        m = self.INSET + 4
        x = max(m, min(self.W - w - m, round(across - w / 2)))
        y = max(m, min(self.FACE - h - 2, round(along - h / 2)))
        if shadow:
            a = np.asarray(img)[..., 3]
            z = np.zeros_like(a)
            sh = Image.fromarray(np.dstack([z, z, z, (a * 0.33).astype(np.uint8)]), 'RGBA')
            self.art.alpha_composite(sh.filter(ImageFilter.GaussianBlur(0.8)), (x - 1, y + 2))
        self._put(img, (x, y))

    def psd_object(self, name, slab=False, stretch_width=False, turn=False):
        img, bbox = self._layer(name)
        w = bbox[2] - bbox[0]
        centre = (bbox[0] + bbox[2]) / 2 - self.SX0
        along = (self.FACE + self.INSET) / 2 + (centre - self.SW / 2) * self.stretch
        across = (bbox[1] + bbox[3]) / 2 - self.SY0
        if stretch_width:
            img = img.resize((round(w * self.stretch), img.size[1]), Image.LANCZOS)
        if turn:                       # a plain rectangle has no "up", so it turns with the bench
            img = img.transpose(Image.ROTATE_270)
        if slab:
            self._place_slab(img, along, across)
        else:
            self._place(img, along, across, shadow=False)

    def _place_slab(self, img, along, across):
        w, h = img.size
        x = max(self.INSET, min(self.W - self.INSET - w, round(across - w / 2)))
        y = max(self.INSET, min(self.FACE - h, round(along - h / 2)))
        self._put(img, (x, y), slab=True)

    def obj(self, path, along, across, height, turn=False):
        img = Image.open(path).convert('RGBA')
        height = max(1, round(height * V.OBJ_SCALE))
        s = height / img.height
        img = img.resize((max(1, round(img.width * s)), height), Image.LANCZOS)
        if turn:
            img = img.transpose(Image.ROTATE_270)
        self._place(img, along * self.along, across)

    def out(self):
        return V.on_canvas(self.art, self.mask, CANVAS_E, (self.EX0, self.EY0), TARGET_E)
