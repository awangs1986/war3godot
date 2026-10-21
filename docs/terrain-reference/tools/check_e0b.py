# -*- coding: utf-8 -*-
import sys, os, struct, tempfile, collections
import mpyq
sys.path.insert(0, r'D:\tools\pr6\docs\terrain-reference\tools')
import w3e_diff as W
p = r'D:\tools\exp\E0b_cliff.w3m'
a = mpyq.MPQArchive(p, listfile=False)
raw = a.read_file(b'war3map.w3e')
tmp = os.path.join(tempfile.gettempdir(), 'e0b.w3e'); open(tmp,'wb').write(raw)
h, c = W.load(tmp); Wd, Hd = h['w'], h['h']
lay=[(i%Wd,i//Wd,c[i].layer) for i in range(Wd*Hd) if c[i].layer!=2]
ct =[(i%Wd,i//Wd) for i in range(Wd*Hd) if c[i].cliffType!=15]
print('  L  =', len(lay), 'bbox=', (min(x for x,_,_ in lay), max(x for x,_,_ in lay), min(y for _,y,_ in lay), max(y for _,y,_ in lay)) if lay else None)
print('  ct =', len(ct))
if lay: print('  L hist:', dict(sorted(collections.Counter(v for _,_,v in lay).items())))
if lay:
    xs=[x for x,_,_ in lay]; ys=[y for _,y,_ in lay]
    print('  是否触边: x0=%d y0=%d xmax=%d ymax=%d (上界 %d)' % (min(xs),min(ys),max(xs),max(ys),Wd-1))
