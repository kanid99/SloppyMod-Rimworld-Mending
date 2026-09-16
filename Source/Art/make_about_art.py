"""Builds About/Preview.png and About/ModIcon.png from the mod's own in-game textures.

Composed from the real building sprites rather than drawn separately, so the store page
always shows what is actually in the game - re-run it after any texture change.
"""
from PIL import Image, ImageDraw, ImageFont, ImageFilter

TEX = 'Textures/Things/Building/Production/'
BOLD = '/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf'
W, H = 640, 360     # ModMetaData.PreviewImagePath; 16:9 matches the Workshop banner

card = Image.new('RGBA', (W, H), (28, 26, 24))
d = ImageDraw.Draw(card)
for y in range(H):
    t = y / (H - 1)
    d.line([(0, y), (W, y)],
           fill=tuple(round(a + (b - a) * t) for a, b in zip((48, 44, 40), (26, 24, 22))))


def fit(path, width):
    im = Image.open(TEX + path).convert('RGBA')
    im = im.crop(im.getbbox())
    return im.resize((width, round(im.height * width / im.width)), Image.LANCZOS)


def shadow(img, blur=7, alpha=130, off=(3, 5)):
    pad = blur * 3
    sh = Image.new('RGBA', (img.width + pad * 2, img.height + pad * 2), (0, 0, 0, 0))
    sh.paste(Image.new('RGBA', img.size, (0, 0, 0, alpha)),
             (pad + off[0], pad + off[1]), img.split()[3])
    return sh.filter(ImageFilter.GaussianBlur(blur)), pad


# The repair centre is square now (5x5 drawn at 6x6), not the old 5x3 letterbox, so it is
# sized off the card's height rather than its width.
auto = fit('AutomatedMender_north.png', 280)
hand = fit('TableMending_Manual_north.png', 236)
elec = fit('TableMending_Electric_north.png', 236)
placements = ((auto, (340, 40)), (hand, (30, 190)), (elec, (30, 274)))

for img, pos in placements:
    assert pos[0] >= 0 and pos[1] >= 0
    assert pos[0] + img.width <= W and pos[1] + img.height <= H, (pos, img.size)
    sh, pad = shadow(img)
    card.alpha_composite(sh, (pos[0] - pad, pos[1] - pad))
    card.alpha_composite(img, pos)

accent = (61, 201, 217, 255)     # the repair centre's own progress-bar cyan
d.text((30, 36), "SLOPPYMODS", font=ImageFont.truetype(BOLD, 18), fill=(146, 142, 136, 255))
d.text((30, 60), "MENDING", font=ImageFont.truetype(BOLD, 44), fill=(238, 234, 228, 255))
d.text((30, 104), "SOLUTIONS", font=ImageFont.truetype(BOLD, 44), fill=accent)
d.line([(32, 158), (190, 158)], fill=accent, width=3)
d.text((30, 168), "Hand, electric and automated repair",
       font=ImageFont.truetype(BOLD, 15), fill=(168, 164, 158, 255))

card.convert('RGB').save('About/Preview.png')

# ModMetaData.ModIconImagePath - shown small and square in the mod list.
ICON = 256
icon = Image.new('RGBA', (ICON, ICON), (0, 0, 0, 0))
src = Image.open(TEX + 'AutomatedMender_north.png').convert('RGBA')
src = src.crop(src.getbbox())
scale = min(ICON / src.width, ICON / src.height) * 0.98
small = src.resize((round(src.width * scale), round(src.height * scale)), Image.LANCZOS)
icon.alpha_composite(small, ((ICON - small.width) // 2, (ICON - small.height) // 2))
icon.save('About/ModIcon.png')

print('wrote About/Preview.png and About/ModIcon.png')
