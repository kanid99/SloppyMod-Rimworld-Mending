"""The east rotation, built rather than rotated.

The camera does not turn with the building, so a 90 degree turn of the finished north sprite
would stand the leg tabs on one side and lay every tool on its face. Instead the slab is stood
on its end, fresh leg tabs are stamped along the new bottom edge, and each object is moved to
its quarter-turned position while staying upright.
"""
import numpy as np
from PIL import Image, ImageFilter
import bench_compose as V

TARGET_E = (192, 576)


class BenchEast:
    def __init__(self, layers):
        self.L = layers
        x0, y0, x1, y1 = layers['border'].bbox
        self.SX0, self.SY0, self.SW, self.SH = x0, y0, x1 - x0, y1 - y0
        self.W, self.H = self.SH, self.SH * 3        # 73 x 219
        self.stretch = self.H / self.SW
        self.art = Image.new('RGBA', (self.W, self.H), (0, 0, 0, 0))
        self.mask = Image.new('RGBA', (self.W, self.H), (0, 0, 0, 0))

    def _layer(self, name):
        l = self.L[name]
        return l.composite(viewport=V.CANVAS).crop(l.bbox), l.bbox

    def slab(self):
        border, bb = self._layer('border')
        base, bsb = self._layer('base')

        # Measure the leg tabs rather than assume where they are.
        alpha = np.asarray(border)[..., 3] > 8
        rows = alpha.sum(axis=1)
        full = rows.max()
        leg_top = next(y for y in range(len(rows) - 1, -1, -1) if rows[y] == full) + 1
        self.LEG_H = border.size[1] - leg_top
        cols = alpha[leg_top:, :].sum(axis=0)
        runs, start = [], None
        for x, c in enumerate(cols):
            if c and start is None:
                start = x
            elif not c and start is not None:
                runs.append((start, x)); start = None
        if start is not None:
            runs.append((start, len(cols)))
        tab = border.crop((runs[1][0], leg_top, runs[1][1], border.size[1]))

        body = border.crop((0, 0, border.size[0], leg_top)).transpose(Image.ROTATE_270)
        self._put(body.resize((self.W, self.H - self.LEG_H), Image.LANCZOS), (0, 0), slab=True)
        for x in (0, self.W - tab.size[0]):
            self._put(tab, (x, self.H - self.LEG_H), slab=True)

        self.INSET = bsb[0] - self.SX0
        i = self.INSET
        bb2 = base.crop((0, 0, base.size[0], leg_top - i)).transpose(Image.ROTATE_270)
        self._put(bb2.resize((self.W - 2 * i, self.H - self.LEG_H - 2 * i), Image.LANCZOS),
                  (i, i), slab=True)

    def _put(self, img, at, slab=False):
        self.art.alpha_composite(img, at)
        self.mask.alpha_composite(V.as_mask(img, red=slab), at)

    def _place(self, img, along, across, shadow=True):
        """`along` runs the length of the bench, `across` its depth - the same two numbers the
        north layout uses. A quarter turn swaps the axes; it never turns the object."""
        w, h = img.size
        m = self.INSET + 8
        x = max(m, min(self.W - w - m, round(across - w / 2)))
        y = max(m, min(self.H - self.LEG_H - h - m, round(along - h / 2)))
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
        along = self.H / 2 + (centre - self.SW / 2) * self.stretch
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
        x = max(0, min(self.W - w, round(across - w / 2)))
        y = max(0, min(self.H - h, round(along - h / 2)))
        self._put(img, (x, y), slab=True)

    def obj(self, path, along, across, height, turn=False):
        img = Image.open(path).convert('RGBA')
        s = height / img.height
        img = img.resize((max(1, round(img.width * s)), height), Image.LANCZOS)
        if turn:
            img = img.transpose(Image.ROTATE_270)
        self._place(img, along, across)

    def out(self):
        return (self.art.resize(TARGET_E, Image.LANCZOS),
                self.mask.resize(TARGET_E, Image.NEAREST))
