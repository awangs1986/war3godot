# -*- coding: utf-8 -*-
import sys, os, glob, struct, math, collections, tempfile
import mpyq
sys.path.insert(0, r"D:\tools\pr6\docs\terrain-reference\tools")
import w3e_diff as W

FIND = r"D:\tools\exp"
cands = sorted(glob.glob(os.path.join(FIND, "**", "*.w3m"), recursive=True) +
               glob.glob(os.path.join(FIND, "**", "*.w3x"), recursive=True),
               key=os.path.getmtime, reverse=True)
target = None
for p in cands:
    if "w8_deep" in os.path.basename(p).lower():
        target = p; break
print("newest 3:", [os.path.basename(c) for c in cands[:3]])
print("target:", target)
if target is None: raise SystemExit("no archive")

a = mpyq.MPQArchive(target, listfile=False)
raw = a.read_file(b"war3map.w3e")
ver = struct.unpack_from("<i", raw, 4)[0]
tmp = os.path.join(tempfile.gettempdir(), "W8_deep.w3e")
open(tmp, "wb").write(raw)
h, c = W.load(tmp)
Wd, Hd = h["w"], h["h"]
print("w3e: %d B  version=%d  %dx%d" % (len(raw), ver, Wd, Hd))

BASE_L, BASE_CT = 2, 15
lay=[]; wat=[]; ct=[]
allpts=set()
for i in range(Wd*Hd):
    q=c[i]; x,y=i%Wd,i//Wd
    if q.layer!=BASE_L: lay.append((x,y,q.layer)); allpts.add((x,y))
    if q.water: wat.append((x,y)); allpts.add((x,y))
    if q.cliffType!=BASE_CT: ct.append((x,y,q.cliffType)); allpts.add((x,y))

def stats(pts,name):
    if not pts: print("  %-8s 0" % name); return
    xs=[p[0] for p in pts]; ys=[p[1] for p in pts]
    cx=sum(xs)/len(xs); cy=sum(ys)/len(ys)
    rmax=max(math.hypot(x-cx,y-cy) for x,y in zip(xs,ys))
    print("  %-8s %3d pts  bbox=(%d..%d,%d..%d) = %dx%d  center=(%.1f,%.1f)  rmax=%.2f rpmax=%s" %
          (name,len(pts),min(xs),max(xs),min(ys),max(ys),max(xs)-min(xs)+1,max(ys)-min(ys)+1,cx,cy,rmax,
           max(max(abs(x-cx),abs(y-cy)) for x,y in zip(xs,ys))))
print("")
stats(lay,"layerL"); stats(wat,"water"); stats(ct,"ct")
print("  L hist:", dict(sorted(collections.Counter(v for _,_,v in lay).items())))
print("  ct hist:", dict(sorted(collections.Counter(v for _,_,v in ct).items())))
print("  changed total(union): %d" % len(allpts))

# 理论
def disc(r): return sum(1 for dx in range(-r,r+1) for dy in range(-r,r+1) if dx*dx+dy*dy<=r*r)
def ring(r):  return sum(1 for dx in range(-r-1,r+2) for dy in range(-r-1,r+2) if dx*dx+dy*dy<=(r+1)*(r+1))
print("\n  theory size8: footprint(r<=7)=%d  disc(r<=8)=%d  union(r<=8)-footprint=%d  sum=%d" %
      (disc(7), disc(8), disc(8)-disc(7), disc(8)))
print("  theory size5: footprint(r<=4)=%d  discard" % disc(4))
print("\n  verdict:")
print("   layerL %d vs 149 -> %s" % (len(lay), "MATCH" if len(lay)==149 else "DIFF %+d" % (len(lay)-149)))
print("   water  %d vs 149 -> %s" % (len(wat), "MATCH" if len(wat)==149 else "DIFF %+d" % (len(wat)-149)))
print("   union  %d vs 213 -> %s" % (len(allpts), "MATCH" if len(allpts)==213 else "DIFF %+d" % (len(allpts)-213)))
