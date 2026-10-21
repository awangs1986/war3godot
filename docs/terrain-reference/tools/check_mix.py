# -*- coding: utf-8 -*-
import sys, os, struct, math, collections, tempfile, glob
import mpyq
sys.path.insert(0, r"D:\tools\pr6\docs\terrain-reference\tools")
import w3e_diff as W

cands = sorted(glob.glob(r"D:\tools\exp\*.w3m"), key=os.path.getmtime, reverse=True)
print("recent:", [(os.path.basename(p), os.path.getsize(p)) for p in cands[:4]])
t = None
for p in cands:
    if "mix_water_cliff" in os.path.basename(p).lower(): t = p; break
if not t: t = cands[0]
print("target:", t)

a = mpyq.MPQArchive(t, listfile=False)
raw = a.read_file(b"war3map.w3e")
tmp = os.path.join(tempfile.gettempdir(), "mix.w3e"); open(tmp,"wb").write(raw)
h, c = W.load(tmp); Wd, Hd = h["w"], h["h"]
print("\nw3e=%d B ver=%d grid=%dx%d" % (len(raw), struct.unpack_from("<I",raw,4)[0], Wd, Hd))

BL, BC = 2, 15
lay=[];ct=[];wat=[];ramp=[];tex=[];blgt=[]
for i in range(Wd*Hd):
    q=c[i]; x,y=i%Wd,i//Wd
    if q.layer!=BL: lay.append((x,y,q.layer))
    if q.cliffType!=BC: ct.append((x,y,q.cliffType))
    if q.water: wat.append((x,y))
    if q.ramp: ramp.append((x,y))
    if q.tex!=0: tex.append((x,y,q.tex))
    if q.blight: blgt.append((x,y))
def rep(nm,pts):
    if not pts: print("  %-6s 0" % nm); return None
    xs=[p[0] for p in pts]; ys=[p[1] for p in pts]
    cx=sum(xs)/len(xs); cy=sum(ys)/len(ys)
    rmax=max(math.hypot(x-cx,y-cy) for x,y in zip(xs,ys))
    print("  %-6s %3d  bbox=(%d..%d,%d..%d)=%dx%d c=(%.1f,%.1f) rmax=%.2f" %
          (nm,len(pts),min(xs),max(xs),min(ys),max(ys),max(xs)-min(xs)+1,max(ys)-min(ys)+1,cx,cy,rmax))
    return (cx,cy)
cxcy = rep("L",lay); rep("ct",ct); rep("water",wat); rep("ramp",ramp); rep("tex",tex); rep("blgt",blgt)
if lay: print("  L hist:", dict(sorted(collections.Counter(v for _,_,v in lay).items())))
if ct:  print("  ct hist:", dict(sorted(collections.Counter(v for _,_,v in ct).items())))

print("\n=== 关键：water 与 L 的交集/差集 ===")
sw = set((x,y) for x,y in wat); sl = set((x,y) for x,y,_ in lay)
print("  water=%d  L=%d  交集=%d  仅water=%d  仅L=%d" % (len(sw), len(sl), len(sw&sl), len(sw-sl), len(sl-sw)))
if sw & sl:
    print("  交集内 L 值分布:", dict(sorted(collections.Counter(c[y*Wd+x].layer for x,y in (sw&sl)).items())))
if sw - sl:
    print("  仅 water 的 L 值分布:", dict(sorted(collections.Counter(c[y*Wd+x].layer for x,y in (sw-sl)).items())))
if sl - sw:
    print("  仅 L 的 L 值分布:", dict(sorted(collections.Counter(c[y*Wd+x].layer for x,y in (sl-sw)).items())))

print("\n=== 逐点明细（并集，按 x,y 排序）===")
allp = sorted(sw | sl)
for (x,y) in allp[:45]:
    q = c[y*Wd+x]
    print("    (%2d,%2d)  L=%d  ct=%2d  water=%s  ramp=%s" % (x,y,q.layer,q.cliffType,q.water,q.ramp))
