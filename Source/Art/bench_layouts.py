"""A modern repair workbench, drawn with our own sewing machine and thread spools.

elec_01 and elec_04 were the two the layout is built from: the press tool from 04, plus a
wrench, a drill and a sewing machine. The vanilla PSD's sewing machine and spool cluster are
dropped - these are purpose-drawn - but the slab and its inset work panel stay, because they
are what carries the stuff tint.
"""
import os
import bench_compose as V

# The cut sprites are committed beside this script, so the benches rebuild without
# re-running the generator.
OBJ = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'objects')
def p(n): return os.path.join(OBJ, f'{n}.png')

SEWING, DRILL, PRESS      = 'modern_a_0', 'modern_a_1', 'modern_a_2'
SPOOLRACK, CONE, WRENCH2, CALLIPER = 'modern_b_0', 'modern_b_1', 'modern_b_2', 'modern_b_3'
CADDY, VICE2, HEATPRESS, CABLE2    = 'modern_c_0', 'modern_c_1', 'modern_c_2', 'modern_c_3'
RIVETS, PLATES, SCREWS    = 'rivets', 'metaltools_b_3', 'parts_a_4'
BOARD, COMPONENT, SHEET   = 'elec_d_1', 'parts_a_0', 'parts_a_3'
GOGGLES, TRAY, OFFCUTS    = 'elec_f_2', 'elec_g_1', 'electools_b_3'
FABRIC, PATCH, NEEDLES    = 'sew_b_3', 'sew_a_3', 'sew_a_1'

# (sprite, centre x, centre y, height) in slab space - the slab is 219 x 73 and its interior
# runs y 4..66, so nothing is placed with its half-height past that.
ELEC_MODERN = {
'elecm_01': [(SEWING, 178, 34, 44), (SPOOLRACK, 32, 14, 15), (PRESS, 34, 46, 30),
             (DRILL, 80, 22, 24), (WRENCH2, 96, 54, 12), (CONE, 130, 22, 20),
             (CALLIPER, 128, 54, 12)],
'elecm_02': [(SEWING, 176, 34, 44), (SPOOLRACK, 30, 13, 15), (VICE2, 36, 46, 28),
             (DRILL, 84, 46, 24), (WRENCH2, 88, 16, 12), (TRAY, 130, 24, 18),
             (RIVETS, 132, 52, 13)],
'elecm_03': [(SEWING, 40, 34, 44), (PRESS, 186, 40, 30), (SPOOLRACK, 186, 13, 15),
             (DRILL, 92, 20, 24), (WRENCH2, 100, 52, 12), (CALLIPER, 140, 20, 12),
             (FABRIC, 138, 50, 16)],
'elecm_04': [(SEWING, 178, 34, 44), (HEATPRESS, 36, 40, 30), (SPOOLRACK, 34, 12, 14),
             (DRILL, 86, 20, 24), (PRESS, 92, 50, 22), (WRENCH2, 134, 20, 12),
             (PATCH, 134, 52, 16)],
'elecm_05': [(SEWING, 176, 36, 42), (CADDY, 36, 48, 20), (SPOOLRACK, 34, 16, 15),
             (PRESS, 88, 24, 26), (DRILL, 92, 54, 20), (WRENCH2, 136, 22, 12),
             (CABLE2, 136, 52, 18)],
'elecm_06': [(SEWING, 178, 34, 44), (PRESS, 36, 42, 30), (CONE, 34, 14, 16),
             (DRILL, 84, 20, 24), (WRENCH2, 90, 52, 12), (SPOOLRACK, 132, 18, 14),
             (GOGGLES, 132, 50, 14)],
}

# "More electric tools and bits and bobs" on the hand benches. The vanilla scissors and thread
# spools are dropped here so there is room for them.
HAND_DENSE = {
'handm_01': [(PRESS, 34, 40, 28), (SPOOLRACK, 34, 13, 14), ('metaltools_a_0', 84, 22, 26),
             ('metaltools_a_2', 88, 52, 24), (CALLIPER, 130, 18, 12), (RIVETS, 132, 50, 14),
             (PLATES, 180, 22, 16), ('metaltools_b_1', 184, 50, 28)],
'handm_02': [(VICE2, 34, 40, 28), (CONE, 34, 14, 16), ('metaltools_a_1', 84, 34, 32),
             (WRENCH2, 122, 18, 12), (SCREWS, 122, 50, 16), (CADDY, 178, 24, 18),
             ('sew_b_2', 180, 52, 22)],
'handm_03': [(SEWING, 40, 34, 40), (CADDY, 100, 20, 18), ('metaltools_a_0', 104, 52, 24),
             (SPOOLRACK, 150, 16, 14), (NEEDLES, 152, 46, 16), (PATCH, 192, 24, 18),
             (RIVETS, 192, 52, 14)],
'handm_04': [(PRESS, 36, 42, 28), (CALLIPER, 34, 14, 12), (DRILL, 86, 22, 24),
             ('metaltools_b_1', 90, 52, 28), (BOARD, 134, 20, 18), (OFFCUTS, 134, 50, 18),
             (CADDY, 182, 34, 20)],
'handm_05': [(CADDY, 34, 22, 20), (SPOOLRACK, 36, 52, 14), ('smith_a_1', 88, 22, 20),
             ('metaltools_a_0', 90, 52, 24), (COMPONENT, 132, 20, 16), (SHEET, 134, 50, 15),
             (VICE2, 182, 36, 28)],
'handm_06': [(SEWING, 176, 34, 40), (PRESS, 34, 42, 28), (SPOOLRACK, 34, 13, 14),
             ('metaltools_a_2', 84, 22, 24), (NEEDLES, 88, 52, 16), (FABRIC, 130, 20, 16),
             (WRENCH2, 130, 52, 12)],
}


def build(psd, objs, psd_objs=(), panel=False):
    b = V.Bench(V.load(psd))
    b.slab()
    if panel:
        b.psd_object('Panel', slab=True, stretch_width=True)
    for n in psd_objs:
        b.psd_object(n)
    for name, cx, cy, h in objs:
        b.obj(p(name), cx, cy, h)
    return b.out()


if __name__ == '__main__':
    os.makedirs('built', exist_ok=True)
    for n, objs in ELEC_MODERN.items():
        a, m = build(V.ELEC_PSD, objs, panel=True)
        a.save(f'built/{n}.png'); m.save(f'built/{n}m.png')
    for n, objs in HAND_DENSE.items():
        a, m = build(V.HAND_PSD, objs)
        a.save(f'built/{n}.png'); m.save(f'built/{n}m.png')
    print('built', len(ELEC_MODERN) + len(HAND_DENSE))
