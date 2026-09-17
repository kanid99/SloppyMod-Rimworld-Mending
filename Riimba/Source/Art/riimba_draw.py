"""Shared drawing primitives for the Riimba sprites.

Everything here draws at SS times the final size and downsamples with LANCZOS at the
end. PIL's ellipse and polygon are hard-aliased, and a 96px disc drawn directly has a
visible staircase on its rim at RimWorld's default zoom; the same disc drawn at 4x and
reduced does not. It costs nothing but memory and it is the only reason the unit reads
as round rather than as a cut gem.
"""

from PIL import Image, ImageDraw

# Supersampling factor. 4 is the point where further increases stop being visible
# after the downsample.
SS = 4

# Palette.
#
# Deliberately cool and desaturated so a Riimba reads as machinery next to RimWorld's
# warm-toned colonists rather than competing with them, with exactly one saturated
# accent - the teal - carrying the "this thing is powered and thinking" signal. That is
# the same division the game's own mech sprites use: a grey chassis and one coloured
# eye, so the eye is what the player's attention lands on.
SHELL_DARK = (44, 48, 56)
SHELL = (58, 62, 70)
SHELL_LIT = (86, 92, 102)
PANEL = (72, 78, 88)
BUMPER = (150, 156, 166)
BUMPER_DARK = (104, 110, 120)
TEAL = (60, 168, 178)
TEAL_DARK = (30, 92, 102)
BRUSH = (196, 186, 150)
BRUSH_DARK = (140, 130, 100)
OUTLINE = (18, 20, 24)

# Station-only tones.
CASE = (104, 100, 98)
CASE_DARK = (74, 71, 70)
CASE_LIT = (138, 134, 132)
BAY_FLOOR = (52, 55, 60)
CONTACT = (188, 170, 96)


def new_canvas(width, height):
    """A transparent canvas at supersampled size, plus its draw handle."""
    image = Image.new("RGBA", (width * SS, height * SS), (0, 0, 0, 0))
    return image, ImageDraw.Draw(image)


def finish(image, width, height):
    return image.resize((width, height), Image.LANCZOS)


def s(value):
    """Scale a final-size coordinate into supersampled space."""
    return int(round(value * SS))


def ellipse(draw, box, fill=None, outline=None, width=1):
    x0, y0, x1, y1 = box
    draw.ellipse((s(x0), s(y0), s(x1), s(y1)), fill=fill, outline=outline,
                 width=max(1, s(width)))


def rect(draw, box, fill=None, outline=None, width=1, radius=None):
    x0, y0, x1, y1 = box
    scaled = (s(x0), s(y0), s(x1), s(y1))

    if radius:
        draw.rounded_rectangle(scaled, radius=s(radius), fill=fill, outline=outline,
                               width=max(1, s(width)))
    else:
        draw.rectangle(scaled, fill=fill, outline=outline, width=max(1, s(width)))


def polygon(draw, points, fill=None, outline=None):
    draw.polygon([(s(x), s(y)) for x, y in points], fill=fill, outline=outline)


def line(draw, points, fill, width=1):
    draw.line([(s(x), s(y)) for x, y in points], fill=fill, width=max(1, s(width)))


def arc(draw, box, start, end, fill, width=1):
    x0, y0, x1, y1 = box
    draw.arc((s(x0), s(y0), s(x1), s(y1)), start, end, fill=fill, width=max(1, s(width)))


def pieslice(draw, box, start, end, fill=None, outline=None, width=1):
    x0, y0, x1, y1 = box
    draw.pieslice((s(x0), s(y0), s(x1), s(y1)), start, end, fill=fill, outline=outline,
                  width=max(1, s(width)))


def vertical_ramp(draw, box, top_colour, bottom_colour):
    """A flat tonal ramp, which is how the vanilla-expanded machines fake depth.

    Drawn as one-pixel bands in supersampled space rather than with a gradient image,
    because the shapes it fills are never rectangles - the caller masks it.
    """
    x0, y0, x1, y1 = box
    height = max(1, s(y1) - s(y0))

    for i in range(height):
        t = i / height
        colour = tuple(int(round(top_colour[c] + (bottom_colour[c] - top_colour[c]) * t))
                       for c in range(3))
        draw.rectangle((s(x0), s(y0) + i, s(x1), s(y0) + i + 1), fill=colour)
