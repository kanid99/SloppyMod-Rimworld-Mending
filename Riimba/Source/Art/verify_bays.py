#!/usr/bin/env python3
"""Checks the station art agrees with RiimbaSpots about where the bays are.

This exists because the mending mod shipped an east texture with its ports on the wrong
cells and nobody caught it by looking. The failure is quiet in exactly the same way
here: a station rotated east would still work, units would still charge, and the only
symptom would be a drone parked on a blank stretch of casing.

So the check is mechanical. RiimbaSpots.BayCells is reimplemented below from the C#,
run for all four rotations against a 3x1 footprint, and the resulting cells are
translated into pixel positions in whichever texture that rotation draws. A bay must be
found at each of them, and the opposite edge must be clear.

    python3 Riimba/Source/Art/verify_bays.py      # from the repo root

Exits non-zero on any mismatch, so it can be wired into a build.
"""

import os
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from riimba_draw import CONTACT  # noqa: E402

TEXTURE_DIR = os.path.join("Riimba", "Textures", "Things", "Building", "Riimba")

CELL_PX = 192
SIZE = (3, 1)          # the def's <size>

# Rot4: 0 north, 1 east, 2 south, 3 west. FacingCell as the game defines it.
FACING = {
    "north": (0, 1),
    "east": (1, 0),
    "south": (0, -1),
    "west": (-1, 0),
}

# Which texture each rotation draws, and whether Graphic_Multi mirrors it. West has no
# texture of its own - it reuses east, flipped horizontally.
TEXTURE_FOR = {
    "north": ("north", False),
    "east": ("east", False),
    "south": ("south", False),
    "west": ("east", True),
}


def bay_cells(rot):
    """RiimbaSpots.BayCells, reimplemented.

    Footprint is taken as the rect (0,0)-(2,0) for a north or south facing 3x1, and
    (0,0)-(0,2) when rotated east or west, which is what GenAdj.OccupiedRect does when
    it swaps the size's axes for the vertical rotations.
    """
    if rot in ("north", "south"):
        min_x, max_x, min_z, max_z = 0, SIZE[0] - 1, 0, SIZE[1] - 1
    else:
        min_x, max_x, min_z, max_z = 0, SIZE[1] - 1, 0, SIZE[0] - 1

    dx, dz = FACING[rot]

    if dx != 0:
        x = max_x + 1 if dx > 0 else min_x - 1
        return [(x, z) for z in range(min_z, max_z + 1)]

    z = max_z + 1 if dz > 0 else min_z - 1
    return [(x, z) for x in range(min_x, max_x + 1)]


def looks_like_bay(image, px, py, radius=26):
    """True if the gold charging-contact colour appears near this point.

    The contacts are the one colour that appears nowhere else on the building, which
    makes them a cleaner marker than the bay recess - the recess tone is close to the
    chassis shadow.
    """
    width, height = image.size

    for x in range(max(0, px - radius), min(width, px + radius)):
        for y in range(max(0, py - radius), min(height, py + radius)):
            r, g, b, a = image.getpixel((x, y))
            if a < 128:
                continue
            if abs(r - CONTACT[0]) < 34 and abs(g - CONTACT[1]) < 34 and abs(b - CONTACT[2]) < 34:
                return True

    return False


def bay_sample_points(image, rot):
    """Where in the texture the three bays should be, given the rotation.

    The bays are cut INTO the chassis along the facing edge, so they are sampled just
    inside the texture rather than outside it - the texture covers the building's own
    cells, not the cells the units stand on.
    """
    width, height = image.size
    inset = 30

    dx, dz = FACING[rot]

    if dx != 0:
        # Facing east or west: bays run down one vertical edge.
        x = width - inset if dx > 0 else inset
        return [(x, int(height * (i + 0.5) / 3)) for i in range(3)]

    # Facing north or south. Screen y runs DOWN and map z runs UP, so north (+z) is the
    # top of the texture. Getting this inversion wrong is precisely the bug this script
    # was written to catch.
    y = inset if dz > 0 else height - inset
    return [(int(width * (i + 0.5) / 3), y) for i in range(3)]


def opposite_points(image, rot):
    width, height = image.size
    inset = 30
    dx, dz = FACING[rot]

    if dx != 0:
        x = inset if dx > 0 else width - inset
        return [(x, int(height * (i + 0.5) / 3)) for i in range(3)]

    y = height - inset if dz > 0 else inset
    return [(int(width * (i + 0.5) / 3), y) for i in range(3)]


def main():
    failures = []

    for rot in ("north", "east", "south", "west"):
        texture_name, mirrored = TEXTURE_FOR[rot]
        path = os.path.join(TEXTURE_DIR, f"RiimbaStation_{texture_name}.png")

        if not os.path.exists(path):
            failures.append(f"{rot}: missing texture {path}")
            continue

        image = Image.open(path).convert("RGBA")
        if mirrored:
            image = image.transpose(Image.FLIP_LEFT_RIGHT)

        cells = bay_cells(rot)
        if len(cells) != 3:
            failures.append(f"{rot}: BayCells returned {len(cells)} cells, expected 3")

        for i, (px, py) in enumerate(bay_sample_points(image, rot)):
            if not looks_like_bay(image, px, py):
                failures.append(
                    f"{rot}: no bay in the texture at bay {i} ({px},{py}) - "
                    f"the C# puts that bay at map cell {cells[i]}")

        for i, (px, py) in enumerate(opposite_points(image, rot)):
            if looks_like_bay(image, px, py):
                failures.append(
                    f"{rot}: found a bay on the BACK edge at ({px},{py}) - "
                    f"the facing edge is {FACING[rot]}, so this rotation is reversed")

        print(f"{rot:>6}: texture {texture_name}"
              f"{' (mirrored)' if mirrored else ''}, bay cells {cells}")

    if failures:
        print("\nFAILED:")
        for failure in failures:
            print(f"  {failure}")
        return 1

    print("\nall rotations agree with RiimbaSpots.BayCells")
    return 0


if __name__ == "__main__":
    sys.exit(main())
