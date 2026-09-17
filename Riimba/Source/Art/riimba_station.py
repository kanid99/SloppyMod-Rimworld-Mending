#!/usr/bin/env python3
"""Draws the Riimba control station's three rotations.

The station is 3x1 and its three docking bays sit one per cell along the edge it
FACES. That is not a decoration: RiimbaSpots.BayCells takes the row of cells outside
rot.FacingCell, and Building_RiimbaStation sends each unit to the bay matching its
roster position. So the art has to put a bay where the C# puts one, for every rotation,
or a unit drives to a blank stretch of casing to charge.

Which means the rotations cannot be made by rotating one image:

    north   the station faces north, so the bays are along the TOP edge
    south   faces south, bays along the BOTTOM edge
    east    faces east, bays along the RIGHT edge, on a 192x576 canvas
    west    not drawn - Graphic_Multi mirrors east, which correctly moves the bays
            to the left edge, and west's FacingCell is (-1, 0, 0)

Light always comes from the top of the screen whatever the rotation, which is why the
south view is drawn rather than flipped: flipping would light the bays from below.

    python3 Riimba/Source/Art/riimba_station.py      # from the repo root

Run verify_bays.py afterwards - it samples the real textures at the cells the C#
computes and checks a bay is actually there.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from riimba_draw import (  # noqa: E402
    BAY_FLOOR, CASE, CASE_DARK, CASE_LIT, CONTACT, OUTLINE, SHELL_DARK, TEAL,
    TEAL_DARK, ellipse, finish, line, new_canvas, polygon, rect,
)

CELL = 192
CELLS = 3

LONG = CELL * CELLS      # 576
SHORT = CELL             # 192

# How far a bay is cut into the chassis. Kept under a third of the short side: a bay
# deep enough to swallow the casing would leave the station reading as three separate
# docks with a bar behind them rather than as one machine.
BAY_DEPTH = 54
BAY_WIDTH = 116

MARGIN = 6


def chassis(draw, width, height):
    """The case: a rounded slab with a tonal ramp, top lit.

    Flat bands rather than an outline-everything approach - the same grammar the
    vanilla-expanded factory machines use, and what the mending mod's repair centre was
    matched against. Pure black appears only on the silhouette.
    """
    rect(draw, (MARGIN, MARGIN, width - MARGIN, height - MARGIN),
         fill=OUTLINE, radius=14)
    rect(draw, (MARGIN + 3, MARGIN + 3, width - MARGIN - 3, height - MARGIN - 3),
         fill=CASE_DARK, radius=12)

    # The lit face, inset from the top and sides and stopping short of the bottom, so
    # the dark band left underneath is the shadow. One shape, no gradient.
    rect(draw, (MARGIN + 6, MARGIN + 6, width - MARGIN - 6, height - MARGIN - 14),
         fill=CASE, radius=10)
    lit_depth = min(width, height) * 0.42
    rect(draw, (MARGIN + 9, MARGIN + 9, width - MARGIN - 9, MARGIN + 9 + lit_depth),
         fill=CASE_LIT, radius=8)


def bay(draw, cx, cy, horizontal, chevron_towards):
    """One docking bay: a recess, two charging contacts and a chevron.

    The chevron points the way a unit DRIVES IN, which is inwards from the bay towards
    the machine - the opposite of the repair centre's output arrow, and for the same
    reason: an arrow that means something has to follow the thing that moves.
    """
    if horizontal:
        half_w, half_d = BAY_WIDTH / 2, BAY_DEPTH / 2
    else:
        half_w, half_d = BAY_DEPTH / 2, BAY_WIDTH / 2

    rect(draw, (cx - half_w, cy - half_d, cx + half_w, cy + half_d),
         fill=SHELL_DARK, radius=6)
    rect(draw, (cx - half_w + 4, cy - half_d + 4, cx + half_w - 4, cy + half_d - 4),
         fill=BAY_FLOOR, radius=4)

    # Charging contacts, a pair, set across the bay's width.
    spread = (BAY_WIDTH if horizontal else BAY_DEPTH) * 0.22
    for side in (-1, 1):
        if horizontal:
            x, y = cx + side * spread, cy
        else:
            x, y = cx, cy + side * spread
        rect(draw, (x - 7, y - 7, x + 7, y + 7), fill=CONTACT, radius=2)

    # Chevron, pointing chevron_towards: one of "up", "down", "left".
    size = 16
    if chevron_towards == "up":
        pts = [(cx - size, cy + size * 0.7), (cx, cy - size * 0.5), (cx + size, cy + size * 0.7)]
    elif chevron_towards == "down":
        pts = [(cx - size, cy - size * 0.7), (cx, cy + size * 0.5), (cx + size, cy - size * 0.7)]
    else:
        pts = [(cx + size * 0.7, cy - size), (cx - size * 0.5, cy), (cx + size * 0.7, cy + size)]

    polygon(draw, pts, fill=CASE_LIT)


def subcore_window(draw, cx, cy, width, height):
    """The subcore behind armoured glass - the only lit thing on the building, and the
    whole reason the station costs one."""
    rect(draw, (cx - width / 2, cy - height / 2, cx + width / 2, cy + height / 2),
         fill=OUTLINE, radius=8)
    rect(draw, (cx - width / 2 + 3, cy - height / 2 + 3, cx + width / 2 - 3, cy + height / 2 - 3),
         fill=TEAL_DARK, radius=6)

    # The core itself: a bright lozenge with a couple of dark seams across it, which is
    # cheaper and reads better than trying to paint a glow.
    inset_w, inset_h = width * 0.33, height * 0.30
    rect(draw, (cx - inset_w, cy - inset_h, cx + inset_w, cy + inset_h),
         fill=TEAL, radius=5)

    # Seams across the short axis of the core, so they read as banding on a solid
    # block rather than as legs coming off a body.
    if width >= height:
        for t in (-0.45, 0.0, 0.45):
            x = cx + inset_w * t * 1.7
            line(draw, [(x, cy - inset_h * 0.8), (x, cy + inset_h * 0.8)],
                 fill=TEAL_DARK, width=3)
    else:
        for t in (-0.45, 0.0, 0.45):
            y = cy + inset_h * t * 1.7
            line(draw, [(cx - inset_w * 0.8, y), (cx + inset_w * 0.8, y)],
                 fill=TEAL_DARK, width=3)


def vent_block(draw, cx, cy, horizontal=True):
    """Cooling slats. Detail as a row of identical marks, which is the only kind of
    fine detail the factory machines carry."""
    span, count = 46, 5
    for i in range(count):
        t = (i - (count - 1) / 2) * (span * 2 / count)
        if horizontal:
            line(draw, [(cx - span, cy + t), (cx + span, cy + t)], fill=CASE_DARK, width=4)
        else:
            line(draw, [(cx + t, cy - span), (cx + t, cy + span)], fill=CASE_DARK, width=4)


def bay_lamp(draw, cx, cy):
    """One lamp per bay, on the casing beside it. Three lamps in a row across the
    building is the casing itself saying how many units it drives - which beats a
    cluster of three lights somewhere else that happens to mean the same thing."""
    ellipse(draw, (cx - 9, cy - 9, cx + 9, cy + 9), fill=CASE_DARK)
    ellipse(draw, (cx - 5, cy - 5, cx + 5, cy + 5), fill=TEAL)


def draw_horizontal(bays_at_top):
    image, draw = new_canvas(LONG, SHORT)
    chassis(draw, LONG, SHORT)

    bay_y = MARGIN + 3 + BAY_DEPTH / 2 if bays_at_top else SHORT - MARGIN - 3 - BAY_DEPTH / 2
    chevron = "down" if bays_at_top else "up"

    for i in range(CELLS):
        bay(draw, CELL * (i + 0.5), bay_y, horizontal=True, chevron_towards=chevron)

    # Everything else goes on the half of the slab the bays did not take.
    body_y = SHORT * 0.68 if bays_at_top else SHORT * 0.36

    subcore_window(draw, LONG / 2, body_y, 150, 58)
    vent_block(draw, CELL * 0.5, body_y, horizontal=True)
    vent_block(draw, CELL * 2.5, body_y, horizontal=True)

    # Lamps sit between each bay and the body, on the strip the bay recess leaves.
    lamp_y = bay_y + (BAY_DEPTH / 2 + 14) * (1 if bays_at_top else -1)
    for i in range(CELLS):
        bay_lamp(draw, CELL * (i + 0.5), lamp_y)

    return finish(image, LONG, SHORT)


def draw_east():
    image, draw = new_canvas(SHORT, LONG)
    chassis(draw, SHORT, LONG)

    bay_x = SHORT - MARGIN - 3 - BAY_DEPTH / 2

    for i in range(CELLS):
        bay(draw, bay_x, CELL * (i + 0.5), horizontal=False, chevron_towards="left")

    body_x = (MARGIN + (bay_x - BAY_DEPTH / 2)) / 2

    subcore_window(draw, body_x, LONG / 2, 58, 150)
    vent_block(draw, body_x, CELL * 0.5, horizontal=False)
    vent_block(draw, body_x, CELL * 2.5, horizontal=False)

    lamp_x = bay_x - BAY_DEPTH / 2 - 14
    for i in range(CELLS):
        bay_lamp(draw, lamp_x, CELL * (i + 0.5))

    return finish(image, SHORT, LONG)


def main():
    out_dir = os.path.join("Riimba", "Textures", "Things", "Building", "Riimba")
    os.makedirs(out_dir, exist_ok=True)

    views = {
        "north": lambda: draw_horizontal(bays_at_top=True),
        "south": lambda: draw_horizontal(bays_at_top=False),
        "east": draw_east,
    }

    for name, view in views.items():
        path = os.path.join(out_dir, f"RiimbaStation_{name}.png")
        view().save(path)
        print(f"wrote {path}")


if __name__ == "__main__":
    main()
