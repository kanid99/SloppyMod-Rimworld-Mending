"""Draws the three spoiled-food stack sprites.

Why this is generated rather than painted: the only thing the sprite has to communicate is
"a heap of mush, more of it in a bigger stack", and a heap of mush is exactly the shape a
metaball field makes for free. Three hand-painted piles would also have to stay consistent
with each other as the palette is tuned; three parameter sets cannot drift apart.

Graphic_StackCount wants a FOLDER of _a/_b/_c variants (see Vanilla Recycling Expanded's own
item art), smallest stack first. Output is 128x128 RGBA to match theirs.

No third-party imaging library on purpose - PNG is a short enough format to write directly,
and this keeps the art rebuildable in any Python 3.
"""
import math
import os
import struct
import zlib

SIZE = 128
SS = 3  # supersampling factor, for clean edges without a blur pass

# Muted and desaturated, to sit beside RimWorld's item icons rather than shout over them.
BASE = (94, 99, 64)         # olive grey mush
DARK = (46, 48, 32)         # shadow / outline
MOULD_DARK = (68, 88, 62)   # blue-green mould patches
MOULD_PALE = (158, 156, 124)  # dried pale crust

# (cx, cy, radius) in 0..1 of the canvas, drawn back to front. Radii are deliberately small
# relative to the spread: at the ~64px an item actually renders at, a few clearly separate
# lumps read as "a heap of mush" while one merged mass reads as "a green coin".
VARIANTS = {
    "a": [(0.53, 0.43, 0.095), (0.42, 0.55, 0.150), (0.59, 0.61, 0.088)],
    "b": [(0.47, 0.34, 0.088), (0.65, 0.45, 0.125), (0.33, 0.47, 0.105),
          (0.53, 0.56, 0.155), (0.39, 0.65, 0.120), (0.67, 0.67, 0.082)],
    "c": [(0.45, 0.25, 0.080), (0.63, 0.32, 0.112), (0.29, 0.36, 0.098),
          (0.51, 0.43, 0.140), (0.73, 0.48, 0.090), (0.36, 0.55, 0.132),
          (0.61, 0.61, 0.148), (0.25, 0.65, 0.085), (0.46, 0.73, 0.118),
          (0.72, 0.72, 0.080)],
}


def smoothstep(edge0, edge1, x):
    if edge1 <= edge0:
        return 0.0 if x < edge0 else 1.0
    t = min(1.0, max(0.0, (x - edge0) / (edge1 - edge0)))
    return t * t * (3.0 - 2.0 * t)


def _lattice(ix, iy, seed):
    n = (ix * 374761393 + iy * 668265263 + seed * 1013904223) & 0xFFFFFFFF
    n = (n ^ (n >> 13)) * 1274126177 & 0xFFFFFFFF
    return ((n ^ (n >> 16)) & 0xFFFF) / 65535.0


def noise(x, y, seed):
    """Bilinearly interpolated value noise. The first attempt sampled a lattice with int()
    and no interpolation, which drew the mould as axis-aligned squares."""
    ix, iy = math.floor(x), math.floor(y)
    fx, fy = x - ix, y - iy
    fx = fx * fx * (3.0 - 2.0 * fx)
    fy = fy * fy * (3.0 - 2.0 * fy)
    n00 = _lattice(ix, iy, seed)
    n10 = _lattice(ix + 1, iy, seed)
    n01 = _lattice(ix, iy + 1, seed)
    n11 = _lattice(ix + 1, iy + 1, seed)
    return (n00 * (1 - fx) + n10 * fx) * (1 - fy) + (n01 * (1 - fx) + n11 * fx) * fy


def mix(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def shade_lump(blob, index, x, y):
    """Colour and coverage of one lump at one sample point, or None if the point misses it.

    Each lump is shaded as its own soft heap rather than as part of a shared metaball field.
    A shared field flattens every seam into one silhouette, and a silhouette is the only thing
    a 64px icon really shows.

    The radius is wobbled by angle. Perfectly round lumps read as a bowl of peas; food waste
    has to look like it was scraped out of something."""
    cx, cy, r = blob
    dx = x - cx
    dy = y - cy
    dist = math.sqrt(dx * dx + dy * dy)
    if dist <= 1e-6:
        dist = 1e-6

    angle = math.atan2(dy, dx)
    # Two turns of noise around the circle, so the outline has both broad dents and small nicks.
    wobble = (noise(math.cos(angle) * 2.0 + index * 4.3, math.sin(angle) * 2.0 - index * 1.9, 31) - 0.5)
    wobble += (noise(math.cos(angle) * 6.0 - index * 2.7, math.sin(angle) * 6.0 + index * 3.1, 53) - 0.5) * 0.5
    effective_r = r * (1.0 + 0.34 * wobble)

    d = dist / effective_r
    if d >= 1.0:
        return None

    alpha = 1.0 - smoothstep(0.80, 1.0, d)
    if alpha <= 0.0:
        return None

    # Heap normal, heavily flattened in z: these are wet mounds, not marbles.
    nx, ny = dx / effective_r, dy / effective_r
    nz = math.sqrt(max(0.0, 1.0 - d * d)) * 0.70 + 0.55
    nl = math.sqrt(nx * nx + ny * ny + nz * nz)
    nx, ny, nz = nx / nl, ny / nl, nz / nl

    lx, ly, lz = -0.42, -0.42, 0.80
    ll = math.sqrt(lx * lx + ly * ly + lz * lz)
    diffuse = max(0.0, (nx * lx + ny * ly + nz * lz) / ll)

    # Per-lump tint, so no two heaps are quite the same colour.
    tint = noise(index * 7.3, index * 2.9, 71)
    colour = mix(BASE, MOULD_DARK if tint > 0.5 else MOULD_PALE, abs(tint - 0.5) * 0.5)

    # Mould, at two scales. Offset per lump so neighbours do not share a pattern.
    patch = noise(x * 16.0 + index * 3.7, y * 16.0 - index * 2.3, 7)
    if patch > 0.58:
        colour = mix(colour, MOULD_DARK, min(1.0, (patch - 0.58) * 2.6))
    elif patch < 0.34:
        colour = mix(colour, MOULD_PALE, (0.34 - patch) * 1.25)

    speck = noise(x * 52.0 + index * 5.1, y * 52.0, 19)
    if speck > 0.74:
        colour = mix(colour, DARK, (speck - 0.74) * 1.5)

    # Ambient floor is high and the diffuse term modest: flat, matte, no highlight.
    colour = tuple(c * (0.50 + 0.62 * diffuse) for c in colour)

    # Soft dark rim that starts well inside the edge, which is what gives the lumps their
    # separation once they overlap.
    colour = mix(colour, DARK, smoothstep(0.45, 1.0, d) * 0.70)

    return colour, alpha


def render(blobs):
    rows = []
    step = 1.0 / (SIZE * SS)

    for py in range(SIZE):
        row = bytearray()
        for px in range(SIZE):
            acc_r = acc_g = acc_b = acc_a = 0.0

            for sy in range(SS):
                for sx in range(SS):
                    x = (px * SS + sx + 0.5) * step
                    y = (py * SS + sy + 0.5) * step

                    # Painter's algorithm: later lumps in the list sit in front.
                    col = None
                    cov = 0.0
                    for index, blob in enumerate(blobs):
                        hit = shade_lump(blob, index, x, y)
                        if hit is None:
                            continue
                        c, a = hit
                        col = c if col is None else mix(col, c, a)
                        cov = cov + a * (1.0 - cov)

                    if col is None or cov <= 0.0:
                        continue

                    acc_r += col[0] * cov
                    acc_g += col[1] * cov
                    acc_b += col[2] * cov
                    acc_a += cov

            n = SS * SS
            if acc_a <= 0.0:
                row += b"\x00\x00\x00\x00"
                continue

            r = min(255, max(0, int(acc_r / acc_a + 0.5)))
            g = min(255, max(0, int(acc_g / acc_a + 0.5)))
            b = min(255, max(0, int(acc_b / acc_a + 0.5)))
            a = min(255, max(0, int(acc_a / n * 255.0 + 0.5)))
            row += bytes((r, g, b, a))

        rows.append(bytes(row))
    return rows


def write_png(path, rows):
    raw = b"".join(b"\x00" + r for r in rows)

    def chunk(tag, data):
        c = tag + data
        return struct.pack(">I", len(data)) + c + struct.pack(">I", zlib.crc32(c) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9))
    png += chunk(b"IEND", b"")

    with open(path, "wb") as fh:
        fh.write(png)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    out = os.path.join(here, "..", "..", "Textures", "Things", "Item", "Resource", "SpoiledFood")
    out = os.path.normpath(out)
    os.makedirs(out, exist_ok=True)

    for suffix, blobs in VARIANTS.items():
        path = os.path.join(out, f"SpoiledFood_{suffix}.png")
        write_png(path, render(blobs))
        print(f"wrote {path}")


if __name__ == "__main__":
    main()
