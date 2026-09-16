"""Build the two chosen bench layouts in every rotation, with their stuff masks."""
import os
from PIL import Image
import bench_compose as V, bench_east as east, bench_layouts as modern

TEX = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                   '..', '..', 'Textures', 'Things', 'Building', 'Production') + os.sep

PICK = {
 'TableMending_Manual':   dict(psd=V.HAND_PSD, objs=modern.HAND_DENSE['handm_01'], panel=False),
 'TableMending_Electric': dict(psd=V.ELEC_PSD, objs=modern.ELEC_MODERN['elecm_02'], panel=True),
}


def north(spec):
    return modern.build(spec['psd'], spec['objs'], panel=spec['panel'])


def east_of(spec):
    b = east.BenchEast(V.load(spec['psd']))
    b.slab()
    if spec['panel']:
        b.psd_object('Panel', slab=True, stretch_width=True, turn=True)
    # Every object here is drawn looking straight down, so when the bench turns they turn with
    # it. (The old script kept objects upright because the vanilla sewing machine was drawn in
    # side elevation; nothing in this layout is.)
    for name, cx, cy, h in spec['objs']:
        b.obj(modern.p(name), cx, cy, h, turn=True)
    return b.out()


if __name__ == '__main__':
    for base, spec in PICK.items():
        art, mask = north(spec)
        for rot in ('north', 'south'):
            art.save(f'{TEX}{base}_{rot}.png'); mask.save(f'{TEX}{base}_{rot}m.png')
        ea, em = east_of(spec)
        ea.save(f'{TEX}{base}_east.png'); em.save(f'{TEX}{base}_eastm.png')
        print('wrote', base, art.size, ea.size)
