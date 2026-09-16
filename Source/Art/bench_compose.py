"""Bench design variants, composited onto the REAL vanilla slab.

Two things a Gemini-drawn whole bench cannot give us, and this can:
  * the exact 3:1 footprint - the model draws benches anywhere from 2.7:1 to 5:1, and
    letterboxing a 5:1 body inside a 3:1 cell is what read as "too skinny";
  * a usable stuff mask - the model bakes a wood or stone worktop into the slab, and a
    stuffable building has to take its colour from whatever it was built out of.

So only the OBJECTS come from the model. The slab underneath is the vanilla tailor bench's
own, widened to 3:1 by 3-slice, and it is the part the mask paints red.
"""
import os
import numpy as np
from PIL import Image, ImageFilter
from psd_tools import PSDImage

# The two source PSDs are committed beside this script.
_HERE = os.path.dirname(os.path.abspath(__file__))
HAND_PSD = os.path.join(_HERE, 'TableTailorHand_north.psd')
ELEC_PSD = os.path.join(_HERE, 'MendingBench_electric_north.psd')
CANVAS = (0, 0, 224, 96)
TARGET = (576, 192)


def load(path):
    """Groups as well as leaves: the tailor PSD keeps its scissors and its spools as groups,
    and compositing the group is what carries their own shadow layers with them."""
    psd = PSDImage.open(path)
    layers = {}
    def walk(g):
        for l in g:
            if l.is_group():
                layers.setdefault(l.name, l)
                walk(l)
            else:
                layers.setdefault(l.name, l)
    walk(psd)
    return layers


def hstretch_3slice(img, new_w, margin=10):
    w, h = img.size
    if new_w <= w:
        return img.resize((new_w, h), Image.LANCZOS)
    left, right = img.crop((0, 0, margin, h)), img.crop((w - margin, 0, w, h))
    mid = img.crop((margin, 0, w - margin, h)).resize((new_w - 2 * margin, h), Image.LANCZOS)
    out = Image.new('RGBA', (new_w, h), (0, 0, 0, 0))
    out.paste(left, (0, 0)); out.paste(mid, (margin, 0)); out.paste(right, (new_w - margin, 0))
    return out


class Bench:
    def __init__(self, layers):
        self.L = layers
        x0, y0, x1, y1 = layers['border'].bbox
        self.SX0, self.SY0, self.SW, self.SH = x0, y0, x1 - x0, y1 - y0
        self.W, self.H = self.SH * 3, self.SH
        self.stretch = self.W / self.SW
        self.art = Image.new('RGBA', (self.W, self.H), (0, 0, 0, 0))
        self.mask = Image.new('RGBA', (self.W, self.H), (0, 0, 0, 0))

    def _layer(self, name):
        l = self.L[name]
        return l.composite(viewport=CANVAS).crop(l.bbox), l.bbox

    def slab(self, names=('border', 'base')):
        for name in names:
            img, bbox = self._layer(name)
            il, ir = bbox[0] - self.SX0, self.SX0 + self.SW - bbox[2]
            wide = hstretch_3slice(img, self.W - round(il * self.stretch) - round(ir * self.stretch))
            at = (round(il * self.stretch), bbox[1] - self.SY0)
            self.art.alpha_composite(wide, at)
            self.mask.alpha_composite(as_mask(wide, red=True), at)

    def psd_object(self, name, slab=False, stretch_width=False):
        img, bbox = self._layer(name)
        w = bbox[2] - bbox[0]
        centre = (bbox[0] + bbox[2]) / 2 - self.SX0
        if stretch_width:
            w = round(w * self.stretch)
            img = img.resize((w, img.size[1]), Image.LANCZOS)
        x = round(self.W / 2 + (centre - self.SW / 2) * self.stretch - w / 2)
        y = bbox[1] - self.SY0
        self.art.alpha_composite(img, (x, y))
        self.mask.alpha_composite(as_mask(img, red=slab), (x, y))

    def obj(self, path, cx, cy, h, shadow=True):
        """Place a cut sprite scaled to `h` px tall, centred on (cx, cy) in slab space."""
        img = Image.open(path).convert('RGBA')
        s = h / img.height
        img = img.resize((max(1, round(img.width * s)), h), Image.LANCZOS)
        x, y = round(cx - img.width / 2), round(cy - img.height / 2)
        if shadow:
            a = np.asarray(img)[..., 3]
            z = np.zeros_like(a)
            sh = Image.fromarray(np.dstack([z, z, z, (a * 0.33).astype(np.uint8)]), 'RGBA')
            self.art.alpha_composite(sh.filter(ImageFilter.GaussianBlur(0.8)), (x - 1, y + 2))
        self.art.alpha_composite(img, (x, y))
        self.mask.alpha_composite(as_mask(img, red=False), (x, y))

    def out(self):
        return self.art.resize(TARGET, Image.LANCZOS), self.mask.resize(TARGET, Image.NEAREST)


def as_mask(img, red):
    a = img.split()[3]
    z = Image.new('L', img.size, 0)
    return Image.merge('RGBA', (Image.new('L', img.size, 255 if red else 0), z, z, a))


def tint(art, mask, colour):
    a = np.asarray(art).astype(np.float32)
    m = np.asarray(mask).astype(np.float32)[..., 0] / 255.0
    c = np.array(colour, dtype=np.float32) / 255.0
    rgb = a[..., :3] * (1 - m[..., None]) + a[..., :3] * c * m[..., None]
    return Image.fromarray(np.dstack([np.clip(rgb, 0, 255), a[..., 3]]).astype(np.uint8), 'RGBA')
