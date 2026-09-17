#!/usr/bin/env python3
"""Draws the Riimba drone's three rotations.

RimWorld's camera never rotates, so the three sprites are three VIEWS of the same
object, not three rotations of one image. Turning the south sprite 180 degrees would
put the sensor on the far side of the disc but also put its highlight on the bottom,
lit from below, which reads as a hole rather than a bump.

So each view is drawn: south shows the face (sensor and bumper towards the camera),
north shows the back (vent grille and lifting handle), east shows the profile with the
face to the right. West is not drawn at all - Graphic_Multi mirrors east for west, and
a disc is symmetric about that axis.

    python3 Riimba/Source/Art/riimba_unit.py      # from the repo root
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from riimba_draw import (  # noqa: E402
    BRUSH, BRUSH_DARK, BUMPER, BUMPER_DARK, OUTLINE, PANEL, SHELL, SHELL_DARK,
    SHELL_LIT, TEAL, TEAL_DARK, arc, ellipse, finish, line, new_canvas, pieslice,
    polygon, rect,
)

SIZE = 256

# The disc, inset from the canvas edge so the brush and bumper have somewhere to sit
# without touching the texture border - a sprite that runs to its own edge gets clipped
# by the mesh at some zoom levels.
CX = CY = SIZE / 2
R = SIZE * 0.36


def disc_box(radius):
    return (CX - radius, CY - radius, CX + radius, CY + radius)


def draw_body(draw):
    """The shell: a dark rim, a lit upper face, and a recessed top panel.

    Depth is tone, not outline. The rim is a full disc in the dark tone and the lit
    face is a slightly smaller disc offset UP by a few pixels, which leaves a thicker
    dark crescent along the bottom - the whole of the shading, done with two ellipses.
    """
    ellipse(draw, disc_box(R), fill=OUTLINE)
    ellipse(draw, disc_box(R - 2), fill=SHELL_DARK)

    lit = R - 5
    ellipse(draw, (CX - lit, CY - lit - 3, CX + lit, CY + lit - 3), fill=SHELL)

    # Top face, brighter still, offset further up. Three discs is enough: any more and
    # it starts to look like a rendered sphere, which is not the register the rest of
    # the game's top-down art is in.
    face = R - 13
    ellipse(draw, (CX - face, CY - face - 5, CX + face, CY + face - 5), fill=SHELL_LIT)

    # Recessed centre panel - the lid over the bin.
    panel = R * 0.48
    ellipse(draw, (CX - panel, CY - panel - 4, CX + panel, CY + panel - 4),
            fill=SHELL_DARK)
    ellipse(draw, (CX - panel + 2, CY - panel - 2, CX + panel - 2, CY + panel - 6),
            fill=PANEL)

    # A highlight along the lid's upper rim only. One arc, the same trick as the shell:
    # light from above, so the top edge catches and the bottom edge does not.
    arc(draw, (CX - panel, CY - panel - 4, CX + panel, CY + panel - 4),
        200, 340, fill=SHELL_LIT, width=2)


def draw_wheels(draw, horizontal=True):
    """Drive wheels, peeking out either side of the shell.

    Only ever a sliver: they are underneath a disc that sits on the floor, so almost
    all of each wheel is hidden. Drawing them proud of the shell is what stops the unit
    reading as a floating puck.
    """
    # Centred just OUTSIDE the rim, not under it. An earlier version had these at 0.80R
    # with the shell drawn over the top, which hid them completely and left the unit
    # reading as a floating puck.
    inset = R * 0.97
    length = R * 0.42
    thickness = R * 0.20

    if horizontal:
        for side in (-1, 1):
            x = CX + side * inset
            rect(draw, (x - thickness / 2, CY - length / 2, x + thickness / 2, CY + length / 2),
                 fill=SHELL_DARK, radius=thickness * 0.4)
    else:
        for side in (-1, 1):
            y = CY + side * inset
            rect(draw, (CX - length / 2, y - thickness / 2, CX + length / 2, y + thickness / 2),
                 fill=SHELL_DARK, radius=thickness * 0.4)


def draw_bumper(draw, facing):
    """The sprung bumper arc across the leading edge.

    `facing` is the compass direction the FRONT of the unit points on screen: "down"
    for the south view, "up" for north, "right" for east.
    """
    span = {"down": (20, 160), "up": (200, 340), "right": (290, 70)}[facing]
    box = disc_box(R - 1)

    arc(draw, box, span[0], span[1], fill=BUMPER_DARK, width=6)
    arc(draw, disc_box(R - 3), span[0], span[1], fill=BUMPER, width=3.5)


def draw_sensor(draw, facing):
    """The sensor ring: the one saturated thing on the model, and the unit's 'front'.

    A raised turret rather than a flat decal - the dark collar under the teal is what
    gives it height, and it is the only part of the sprite with a hard outline.
    """
    offset = R * 0.52
    dx, dy = {"down": (0, 1), "up": (0, -1), "right": (1, 0)}[facing]
    x = CX + dx * offset
    y = CY + dy * offset - 4

    collar = R * 0.22
    ellipse(draw, (x - collar, y - collar, x + collar, y + collar),
            fill=SHELL_DARK, outline=OUTLINE, width=1.5)

    lens = collar * 0.62
    ellipse(draw, (x - lens, y - lens, x + lens, y + lens), fill=TEAL_DARK)

    inner = lens * 0.62
    ellipse(draw, (x - inner, y - inner, x + inner, y + inner), fill=TEAL)


def draw_vents(draw, facing):
    """Exhaust slats on the back, so the north view is not just the south view
    with the eye rubbed out."""
    offset = R * 0.52
    dx, dy = {"up": (0, 1), "down": (0, -1), "right": (-1, 0)}[facing]

    for i in (-1, 0, 1):
        if dx:
            x = CX + dx * offset
            y = CY - 4 + i * (R * 0.18)
            line(draw, [(x - R * 0.16, y), (x + R * 0.16, y)], fill=SHELL_DARK, width=2.5)
        else:
            x = CX + i * (R * 0.18)
            y = CY + dy * offset - 4
            line(draw, [(x, y - R * 0.16), (x, y + R * 0.16)], fill=SHELL_DARK, width=2.5)


def draw_handle(draw):
    """A lifting handle across the lid, visible from behind. Purely so the north view
    has something of its own to say."""
    width = R * 0.46
    rect(draw, (CX - width, CY - 11, CX + width, CY - 3),
         fill=SHELL, outline=OUTLINE, width=1.5, radius=4)
    line(draw, [(CX - width + 3, CY - 8.5), (CX + width - 3, CY - 8.5)],
         fill=SHELL_LIT, width=1.5)


def draw_brush(draw, facing):
    """The side brush: a bristle spinner on a short arm, clear of the shell.

    Sited so the WHOLE spinner is outside the silhouette. An earlier version put the
    hub at 0.88R and the shell covered most of it, leaving three bristles poking out
    that read as twigs caught under the machine rather than as a part of it.

    Offset to one side rather than centred on the front, because that is where it is on
    the real thing, and because a centred spinner on a symmetric disc reads as a
    propeller.
    """
    import math

    along = R * 0.50
    # Just far enough out that the whole spinner clears the shell, close enough that
    # its hub still touches the rim - any further and it reads as debris lying beside
    # the unit rather than as part of it.
    out = R * 0.97

    if facing == "down":
        x, y = CX - along, CY + out
    elif facing == "up":
        x, y = CX + along, CY - out
    else:
        x, y = CX + out, CY + along

    spin = R * 0.26

    # Bristles first, so the hub caps them. Six, not three: three reads as a hazard
    # symbol, six reads as a brush.
    for i in range(6):
        angle = i * 60 + 15
        ax = x + spin * math.cos(math.radians(angle))
        ay = y + spin * math.sin(math.radians(angle))
        line(draw, [(x, y), (ax, ay)], fill=BRUSH_DARK, width=5)
        line(draw, [(x, y), (ax, ay)], fill=BRUSH, width=2.5)

    hub = R * 0.12
    ellipse(draw, (x - hub, y - hub, x + hub, y + hub),
            fill=SHELL_DARK, outline=OUTLINE, width=1)
    ellipse(draw, (x - hub * 0.45, y - hub * 0.45, x + hub * 0.45, y + hub * 0.45),
            fill=SHELL_LIT)


def draw_status_led(draw):
    """The charge light on the lid, set BEHIND the sensor rather than at a fixed point.

    Fixed at top centre it drifted off the lid on the east view and looked like a
    sticker someone had put on at random."""
    dx, dy = {"down": (0, -1), "up": (0, 1), "right": (-1, 0)}["down"]
    _place_led(draw, dx, dy)


def _place_led(draw, dx, dy):
    # Half the sensor's size, and no dark collar. At parity with the sensor the two
    # teal dots read as a pair of eyes; the point of this one is that it is a pilot
    # lamp, subordinate to the thing that actually looks where the unit is going.
    offset = R * 0.30
    led = R * 0.055
    x = CX + dx * offset
    y = CY + dy * offset - 4

    ellipse(draw, (x - led, y - led, x + led, y + led), fill=TEAL)


def view_south():
    image, draw = new_canvas(SIZE, SIZE)
    draw_wheels(draw, horizontal=True)
    draw_brush(draw, "down")
    draw_body(draw)
    draw_bumper(draw, "down")
    draw_sensor(draw, "down")
    draw_status_led(draw)
    return finish(image, SIZE, SIZE)


def view_north():
    image, draw = new_canvas(SIZE, SIZE)
    draw_wheels(draw, horizontal=True)
    draw_brush(draw, "up")
    draw_body(draw)
    draw_bumper(draw, "up")
    draw_vents(draw, "down")
    draw_handle(draw)
    return finish(image, SIZE, SIZE)


def view_east():
    image, draw = new_canvas(SIZE, SIZE)
    draw_wheels(draw, horizontal=False)
    draw_brush(draw, "right")
    draw_body(draw)
    draw_bumper(draw, "right")
    draw_sensor(draw, "right")
    draw_vents(draw, "right")
    _place_led(draw, -1, 0)
    return finish(image, SIZE, SIZE)


def main():
    out_dir = os.path.join("Riimba", "Textures", "Things", "Pawn", "Riimba")
    os.makedirs(out_dir, exist_ok=True)

    for name, view in (("north", view_north), ("east", view_east), ("south", view_south)):
        path = os.path.join(out_dir, f"Riimba_{name}.png")
        view().save(path)
        print(f"wrote {path}")


if __name__ == "__main__":
    main()
