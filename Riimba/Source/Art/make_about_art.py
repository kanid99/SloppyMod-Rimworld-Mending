#!/usr/bin/env python3
"""Builds About/Preview.png and About/ModIcon.png from the real sprites.

Composited from the shipped textures rather than drawn separately, so the store page
cannot drift from what is actually in the game. Re-run it after any texture change.

RimWorld looks for both at those exact filenames under About/ - see
ModMetaData.PreviewImagePath and ModIconImagePath - so no path entry is needed in
About.xml.

    python3 Riimba/Source/Art/make_about_art.py      # from the repo root
"""

import os
import sys

from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from riimba_draw import TEAL  # noqa: E402

PREVIEW = (640, 360)
ICON = (256, 256)

UNIT_DIR = os.path.join("Riimba", "Textures", "Things", "Pawn", "Riimba")
STATION_DIR = os.path.join("Riimba", "Textures", "Things", "Building", "Riimba")
ABOUT_DIR = os.path.join("Riimba", "About")

# A mid-grey floor with a slight green cast, which is roughly what RimWorld's sterile
# tile reads as under the default lighting. The art has to survive being seen on that,
# not on white.
FLOOR = (86, 92, 88)
FLOOR_DARK = (66, 72, 69)

FONT_CANDIDATES = [
    "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
    "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
    "/System/Library/Fonts/Supplemental/Arial Bold.ttf",
    "C:\\Windows\\Fonts\\arialbd.ttf",
]


def load_font(size):
    for path in FONT_CANDIDATES:
        if os.path.exists(path):
            try:
                return ImageFont.truetype(path, size)
            except OSError:
                continue

    # Better a preview with no title than no preview at all.
    return None


def tiled_floor(size):
    """A checkered floor, so the sprites are seen against something with the same
    spatial frequency as the game's own tiles rather than against flat colour."""
    image = Image.new("RGBA", size, FLOOR + (255,))
    draw = ImageDraw.Draw(image)
    tile = 40

    for y in range(0, size[1], tile):
        for x in range(0, size[0], tile):
            if (x // tile + y // tile) % 2 == 0:
                draw.rectangle((x, y, x + tile - 1, y + tile - 1), fill=FLOOR_DARK + (255,))

    # Grid lines, faint, to finish the tile read.
    for x in range(0, size[0], tile):
        draw.line((x, 0, x, size[1]), fill=(58, 63, 60, 255))
    for y in range(0, size[1], tile):
        draw.line((0, y, size[0], y), fill=(58, 63, 60, 255))

    return image


def scaled(path, height=None, width=None):
    image = Image.open(path).convert("RGBA")

    if height:
        scale = height / image.height
    else:
        scale = width / image.width

    return image.resize((max(1, int(image.width * scale)), max(1, int(image.height * scale))),
                        Image.LANCZOS)


def build_preview():
    canvas = tiled_floor(PREVIEW)

    # The station across the middle, at three tiles wide against the 40px tile above.
    station = scaled(os.path.join(STATION_DIR, "RiimbaStation_south.png"), width=300)
    station_pos = (int(PREVIEW[0] / 2 - station.width / 2), 150)
    canvas.alpha_composite(station, station_pos)

    # One unit on a bay, two out working - which is the mod in one picture.
    unit_south = scaled(os.path.join(UNIT_DIR, "Riimba_south.png"), width=86)
    unit_east = scaled(os.path.join(UNIT_DIR, "Riimba_east.png"), width=86)
    unit_north = scaled(os.path.join(UNIT_DIR, "Riimba_north.png"), width=86)

    docked_x = station_pos[0] + station.width // 2 - unit_south.width // 2
    canvas.alpha_composite(unit_south, (docked_x, station_pos[1] + station.height - 24))

    # Both working units sit clear of the title band. The north one used to be at y=60
    # and the band cut its top off.
    canvas.alpha_composite(unit_east, (74, 236))
    canvas.alpha_composite(unit_north, (PREVIEW[0] - 74 - unit_north.width, 140))

    # A couple of messes for the working units to be heading towards.
    draw = ImageDraw.Draw(canvas)
    for cx, cy, r, colour in ((150, 300, 15, (96, 66, 52, 190)),
                              (505, 206, 13, (120, 46, 46, 190)),
                              (492, 232, 9, (120, 46, 46, 160))):
        draw.ellipse((cx - r, cy - r, cx + r, cy + r), fill=colour)

    title = load_font(44)
    subtitle = load_font(21)

    if title and subtitle:
        # A dark band behind the text. Type straight onto a busy tiled floor is
        # unreadable at the thumbnail size the workshop actually shows.
        band = Image.new("RGBA", (PREVIEW[0], 108), (18, 20, 24, 208))
        canvas.alpha_composite(band, (0, 10))

        draw = ImageDraw.Draw(canvas)
        draw.text((32, 26), "RIIMBA", font=title, fill=(238, 242, 245, 255))
        draw.text((32, 78), "semi-autonomous cleaning drones", font=subtitle,
                  fill=TEAL + (255,))

    return canvas.convert("RGB")


def build_icon():
    """The unit alone, filling the frame. An icon is shown at about 32px in the mod
    list, where a station with three bays on it is an indistinct grey bar - the disc
    with one teal eye still reads."""
    canvas = Image.new("RGBA", ICON, (0, 0, 0, 0))

    unit = scaled(os.path.join(UNIT_DIR, "Riimba_south.png"), width=ICON[0])
    canvas.alpha_composite(unit, (0, (ICON[1] - unit.height) // 2))

    return canvas


def main():
    os.makedirs(ABOUT_DIR, exist_ok=True)

    preview_path = os.path.join(ABOUT_DIR, "Preview.png")
    build_preview().save(preview_path)
    print(f"wrote {preview_path}")

    icon_path = os.path.join(ABOUT_DIR, "ModIcon.png")
    build_icon().save(icon_path)
    print(f"wrote {icon_path}")


if __name__ == "__main__":
    main()
