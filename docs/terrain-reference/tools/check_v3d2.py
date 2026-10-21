# -*- coding: utf-8 -*-
import sys, os, struct, math, collections, tempfile, glob
import mpyq
sys.path.insert(0, r"D:\tools\pr6\docs\terrain-reference\tools")
import w3e_diff as W

def analyze(path, label):
    a = mpyq.MPQArchive(path, listfile=False)
    raw = a.read_file(b"war3map.w3e")
    tmp = os.path.join(tempfile.gettempdir(), "t.w3e"); open(tmp,"wb").write(raw)
    h, c = W.load(tmp); Wd, Hd = h["w"], h["h"]
    BL, BC = 2, 15
    lay=[];ct=[];wat=[];ramp=[];blgt=[];tex=[];bnd=[]
    for i in range(Wd*Hd):
        q=c[i]; x,y=i%Wd,i//Wd
        if q.layer!=BL: lay.append((x,y,q.layer))
        if q.cliffType!=BC: ct.append((x,y,q.cliffType))
        if q.water: wat.append((x,y))
        if q.ramp: ramp.append((x,y))
        if q.blight: blgt.append((x,y))
        if q.tex!=0: tex.append((x,y,q.tex))
        if q.boundary: bnd.append((x,y))
    print("\n### %s   (%s, %d B)" % (label, os.path.basename(path), os.path.getsize(path)))
    print("    w3e=%d B ver=%d grid=%dx%d (cells %dx%d)" %
          (len(raw), struct.unpack_from("<I",raw,4)[0], Wd,Hd,Wd-1,Hd-1))
    for nm,pts in (("L",lay),("ct",ct),("water",wat),("ramp",ramp),("blight",blgt),("tex",tex),("bnd",bnd)):
        if not pts: print("    %-7s 0" % nm); continue
        xs=[p[0] for p in pts]; ys=[p[1] for p in pts]
        cx=sum(xs)/len(xs); cy=sum(ys)/len(ys)
        rmax=max(math.hypot(x-cx,y-cy) for x,y in zip(xs,ys))
        print("    %-7s %3d  bbox=(%d..%d,%d..%d)=%dx%d c=(%.1f,%.1f) rmax=%.2f" %
              (nm,len(pts),min(xs),max(xs),min(ys),max(ys),max(xs)-min(xs)+1,max(ys)-min(ys)+1,cx,cy,rmax))
    if lay: print("    L hist:", dict(sorted(collections.Counter(v for _,_,v in lay).items())))
    if ct:  print("    ct hist:", dict(sorted(collections.Counter(v for _,_,v in ct).items())))
    if ramp:
        print("    ramp 坐标:", sorted(ramp))
        if lay:
            cx=sum(p[0] for p in lay)/len(lay); cy=sum(p[1] for p in lay)/len(lay)
            rel=sorted((x-cx, y-cy) for x,y in ramp)
            print("    相对层变化中心(%.1f,%.1f)的偏移: %s" % (cx,cy,rel))
    return ramp, lay, ct

# 找最新的 V3d_ramp2
cands = sorted(glob.glob(r"D:\tools\exp\*.w3m"), key=os.path.getmtime, reverse=True)
print("最近 4 个:", [(os.path.basename(p), os.path.getsize(p)) for p in cands[:4]])
target = None
for p in cands:
    if "v3d_ramp2" in os.path.basename(p).lower(): target = p; break
if not target: target = cands[0]
r2, l2, c2 = analyze(target, "3D 视角斜坡")

# 对照 2D 参照 RampSweep
ref = r"D:\tools\pr6\docs\terrain-reference\fixtures\RampSweep.w3e"
if os.path.exists(ref):
    h, c = W.load(ref); Wd=h["w"]
    ramp=[(i%Wd,i//Wd) for i in range(Wd*h["h"]) if c[i].ramp]
    lay =[(i%Wd,i//Wd) for i in range(Wd*h["h"]) if c[i].layer!=2]
    ct  =[(i%Wd,i//Wd) for i in range(Wd*h["h"]) if c[i].cliffType!=15]
    print("\n### 2D 参照 RampSweep")
    print("    ramp=%d  L=%d  ct=%d" % (len(ramp),len(lay),len(ct)))

print("\n=== 判定 ===")
print("  3D ramp 点数 = %d  -> %s" % (len(r2), "9 点 与 2D 一致 ✅" if len(r2)==9 else "不是 9（%d）" % len(r2)))
