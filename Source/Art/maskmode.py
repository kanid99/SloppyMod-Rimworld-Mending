import os
from PIL import Image

# Set MENDING_MASK=1 to make the build scripts emit CutoutComplex masks instead of
# artwork. RimWorld reads the mask's RED channel as "tint this with the thing's
# colour" - the stuff colour, for a stuffable building - and leaves BLACK areas
# with the main texture's own colours. So the slab is red and everything standing
# on it is black: a wooden bench keeps steel tools, a plasteel bench keeps them too.
#
# The mask file sits beside the texture with an "m" appended and NO separating
# underscore - Graphic_Multi.Init builds the path as `path + "_north" + "m"`.
MASK = os.environ.get('MENDING_MASK') == '1'


def prep(img, slab=False):
    """Flatten a layer to its mask colour when building a mask; otherwise pass it through."""
    if not MASK:
        return img
    alpha = img.split()[3]
    zero = Image.new('L', img.size, 0)
    return Image.merge('RGBA', (Image.new('L', img.size, 255 if slab else 0), zero, zero, alpha))


def out_name(base, rot):
    return f'{base}_{rot}m.png' if MASK else f'{base}_{rot}.png'
