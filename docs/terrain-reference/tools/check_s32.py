# -*- coding: utf-8 -*-
import sys, os, glob, struct, math, collections, tempfile
import mpyq
sys.path.insert(0, r"D:\tools\pr6\docs\terrain-reference\tools")
import w3e_diff as W

FIND = r"D:\tools\exp"
cands = sorted(glob.glob(os.path.join(FIND, "**", "*.w3m"), recursive=True) +
               glob.glob(os.path.join(FIND, "**", "*.w3x"), recursive=True),
               key=os.path.getmtime, reverse=True)
print("newest 4:", [os.path.basename(c) for c in cands[:4]])
target = None
for p in cands:
    if "s32" in os.path.basename(p).lower(): target = p; break
if target is None: target = cands[0]
print("target:", target)

a = mpyq.MPQArchive(target, listfile=False)
raw = a.read_file(b"war3map.w3e")
print("\n=== 头部原文 ===")
print("  magic      :", raw[0:4])
print("  version    :", struct.unpack_from("<i", raw, 4)[0])
print("  tileset    :", chr(raw[8]), "(0x%02X)" % raw[8])
print("  custom     :", struct.unpack_from("<i", raw, 9)[0])
print("  groundTiles:", struct.unpack_from("<i", raw, 13)[0])
print("  cliffTiles :", struct.unpack_from("<i", raw, 17)[0])
Wd = struct.unpack_from("<i", raw, 21)[0]
Hd = struct.unpack_from("<i", raw, 25)[0]
print("  W,H        :", Wd, Hd)
print("  file bytes :", len(raw))
for cs in (7, 8):
    print("  若 %d B/角点 => 期望 %d 字节 ; 实际 %d => %s" %
          (cs, 7 + Wd*Hd*cs, len(raw), "MATCH" if 7+Wd*Hd*cs == len(raw) else "no"))

tmp = os.path.join(tempfile.gettempdir(), "S32_w3e.w3e")
open(tmp,"wb").write(raw)
h, c = W.load(tmp)
print("\n  parser 读到的 W,H =", h["w"], h["h"])

BASE_L, BASE_CT = 2, 15
lay=[]; ct=[]; wat=[]; ramp=[]; blgt=[]; allp=set()
for i in range(h["w"]*h["h"]):
    q=c[i]; x,y=i%h["w"], i//h["w"]
    if q.layer!=BASE_L: lay.append((x,y,q.layer)); allp.add((x,y))
    if q.cliffType!=BASE_CT: ct.append((x,y)); allp.add((x,y))
    if q.water: wat.append((x,y)); allp.add((x,y))
    if q.ramp: ramp.append((x,y)); allp.add((x,y))
    if q.blight: blgt.append((x,y)); allp.add((x,y))

def stats(pts,name):
    if not pts: print("  %-8s 0" % name); return None
    xs=[p[0] for p in pts]; ys=[p[1] for p in pts]
    cx=sum(xs)/len(xs); cy=sum(ys)/len(ys)
    rmax=max(math.hypot(x-cx,y-cy) for x,y in zip(xs,ys))
    print("  %-8s %3d pts  bbox=(%d..%d,%d..%d)=%dx%d  center=(%.1f,%.1f) rmax=%.2f" %
          (name,len(pts),min(xs),max(xs),min(ys),max(ys),max(xs)-min(xs)+1,max(ys)-min(ys)+1,cx,cy,rmax))
    return (cx,cy)
print("\n=== 改动统计 ===")
stats(lay,"layerL"); stats(ct,"ct"); stats(wat,"water")
if ramp: stats(ramp,"ramp")
if blgt: stats(blgt,"blight")
print("  L hist:", dict(sorted(collections.Counter(v for _,_,v in lay).items())))
print("  changed union:", len(allp))
print("\n  verdict:")
print("   layerL %d vs 13 -> %s" % (len(lay), "MATCH" if len(lay)==13 else "DIFF %+d" % (len(lay)-13)))
print("   ct     %d vs 37 -> %s" % (len(ct),  "MATCH" if len(ct)==37  else "DIFF %+d" % (len(ct)-37)))
# 边界裁剪检查：改动点是否触边
if allp:
    xs=[p[0] for p in allp]; ys=[p[1] for p in allp]
    print("   是否触边: x_min=%d y_min=%d x_max=%d y_max=%d (W-1=%d H-1=%d)" %
          (min(xs),min(ys),max(xs),max(ys),h["w"]-1,h["h"]-1))
# 检查角落角点是否被写成特殊值
print("\n  四角角点抽样 (L,ct,water,tep):")
for (x,y) in [(0,0),(h["w"]-1,0),(0,h["h"]-1),(h["w"]-1,h["h"]-1)]:
    q=c[y*h["w"]+x]
    print("    (%d,%d) L=%d ct=%d water=%s ramp=%s bnd=%s" % (x,y,q.layer,q.cliffType,q.water,q.ramp,q.boundary))
