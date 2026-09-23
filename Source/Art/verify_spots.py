"""Check the drawn bays land on the cells MenderSpots actually reads, for every rotation.

MenderSpots is reimplemented here from the C# rather than reasoned about, and the check
samples the real texture at each spot's position on the chassis edge.
"""
import sys
import numpy as np
from PIL import Image

PX, CW, CH, MARGIN = 192, 5, 5, 0.5
T = 'Textures/Things/Building/Production/AutomatedMender_'
FACING = {'North': (0, 1), 'East': (1, 0), 'South': (0, -1), 'West': (-1, 0)}
OPP = {'North': 'South', 'South': 'North', 'East': 'West', 'West': 'East'}
RAIL = {'green': (91, 175, 94), 'cyan': (72, 168, 178), 'orange': (175, 120, 65), 'red': (184, 78, 70)}

MIN_X, MAX_X, MIN_Z, MAX_Z = 0, CW - 1, 0, CH - 1
INTAKE_IS_FACING = len(sys.argv) > 1 and sys.argv[1] == 'flipped'


def edge_cells(rot, front):
    dx, dz = FACING[rot] if front else FACING[OPP[rot]]
    if dx != 0:
        x = MAX_X + 1 if dx > 0 else MIN_X - 1
        return [(x, z) for z in range(MIN_Z, MAX_Z + 1)]
    z = MAX_Z + 1 if dz > 0 else MIN_Z - 1
    return [(x, z) for x in range(MIN_X, MAX_X + 1)]


def side_cells(rot):
    back = FACING[rot] if INTAKE_IS_FACING else FACING[OPP[rot]]
    out = []
    if back[0] != 0:
        start, step = (MAX_X, -1) if back[0] > 0 else (MIN_X, 1)
        for k in range(1, CW):
            x = start + step * k
            out += [(x, MIN_Z - 1), (x, MAX_Z + 1)]
    else:
        start, step = (MAX_Z, -1) if back[1] > 0 else (MIN_Z, 1)
        for k in range(1, CH):
            z = start + step * k
            out += [(MIN_X - 1, z), (MAX_X + 1, z)]
    return out


def spots(rot):
    intake = edge_cells(rot, front=INTAKE_IS_FACING)
    mid = len(intake) // 2
    res = []
    if mid - 1 >= 0:
        res.append(intake[mid - 1])
    if mid + 1 < len(intake):
        res.append(intake[mid + 1])
    res += side_cells(rot)[:8 - len(res)]
    out = edge_cells(rot, front=not INTAKE_IS_FACING)
    omid = len(out) // 2
    reject = [out[omid - 1], out[omid + 1]]              # MenderSpots.RejectCells
    return {'cyan': [intake[mid]], 'green': res, 'orange': [out[omid]], 'red': reject}


RAIL_OFFSET = (78 / 2 + 7 / 2) * (PX / 128.0)     # bed half-width plus half a rail


def sample(img, cell, depth=26):
    """The coloured rail of the bay in line with that spot cell.

    Sampling the bay's centre lands on the roller bed, which is grey whatever the port is for -
    the colour lives in the two rails either side of it.
    """
    x, z = cell
    a = np.asarray(img.convert('RGB'))
    w, h = img.size
    on_x_edge = x < MIN_X or x > MAX_X
    if on_x_edge:
        tx = MARGIN * PX + depth if x < MIN_X else w - MARGIN * PX - depth
        along = MARGIN * PX + (MAX_Z - z + 0.5) * PX
        pts = [(int(tx), int(along - RAIL_OFFSET)), (int(tx), int(along + RAIL_OFFSET))]
    else:
        ty = MARGIN * PX + depth if z > MAX_Z else h - MARGIN * PX - depth
        along = MARGIN * PX + (x + 0.5) * PX
        pts = [(int(along - RAIL_OFFSET), int(ty)), (int(along + RAIL_OFFSET), int(ty))]
    return [a[p[1], p[0]] for p in pts]


def nearest(px):
    return min(RAIL, key=lambda k: sum((int(a) - int(b)) ** 2 for a, b in zip(px, RAIL[k])))


def dist(px, name):
    return sum((int(a) - int(b)) ** 2 for a, b in zip(px, RAIL[name])) ** 0.5


ok = True
for rot, tex in (('North', 'north'), ('South', 'south'), ('East', 'east'), ('West', 'east')):
    img = Image.open(T + tex + '.png')
    if rot == 'West':
        img = img.transpose(Image.FLIP_LEFT_RIGHT)      # RimWorld mirrors east for west
    bad = []
    for want, cells in spots(rot).items():
        for cell in cells:
            pxs = sample(img, cell)
            best = min(pxs, key=lambda p: dist(p, want))
            if dist(best, want) > 70:
                bad.append((cell, want, nearest(best), tuple(int(v) for v in best)))
    print(f'{rot:6s} ({tex}): {"OK" if not bad else str(len(bad)) + " MISMATCHED"}')
    for cell, want, got, px in bad[:4]:
        print(f'         cell {cell} wanted {want}, sampled {px} (closest: {got})')
    ok &= not bad
print('\nRESULT:', 'all rotations agree with MenderSpots' if ok else 'MISMATCH')
