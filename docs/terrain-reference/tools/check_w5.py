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
    if "w5_deep" in os.path.basename(p).lower():
        target = p; break
if target is None and cands:
    target = cands[0]
print("目标文件:", target)
if target is None:
    raise SystemExit("没找到存档")

a = mpyq.MPQArchive(target, listfile=False)
raw = a.read_file(b"war3map.w3e")
ver = struct.unpack_from("<i", raw, 4)[0]
tmp = os.path.join(tempfile.gettempdir(), "W5_deep.w3e")
open(tmp, "wb").write(raw)
h, c = W.load(tmp)
Wd, Hd = h["w"], h["h"]
print("w3e: %d B  version=%d  %dx%d" % (len(raw), ver, Wd, Hd))

BASE_L, BASE_CT = 2, 15
lay = []; wat = []; ct = []; tex = []; ramp = []; blgt = []; bnd = []
for i in range(Wd*Hd):
    q = c[i]; x, y = i % Wd, i // Wd
    if q.layer != BASE_L: lay.append((x,y,q.layer))
    if q.water: wat.append((x,y))
    if q.cliffType != BASE_CT: ct.append((x,y,q.cliffType))
    if q.tex != 0: tex.append((x,y))
    if q.ramp: ramp.append((x,y))
    if q.blight: blgt.append((x,y))
    if q.boundary: bnd.append((x,y))

def rep(name, pts):
    if not pts: print("  %-10s 0" % name); return None
    xs=[p[0] for p in pts]; ys=[p[1] for p in pts]
    cx=sum(xs)/len(xs); cy=sum(ys)/len(ys)
    rmax=max(math.hypot(x-cx,y-cy) for x,y in zip(xs,ys))
    print("  %-10s %3d 点  bbox=(%d..%d,%d..%d)  中心(%.1f,%.1f)  rmax=%.2f" %
          (name, len(pts), min(xs),max(xs),min(ys),max(ys), cx,cy, rmax))
    return (cx,cy)

print("\n=== 实测 ===")
cx,cy = rep("改层 L", lay) or (0,0)
rep("water", wat)
rep("ct", ct)
if tex: rep("tex", tex)
if ramp: rep("ramp", ramp)
if blgt: rep("blight", blgt)
if bnd: rep("bnd", bnd)

print("\n  L 直方图:", dict(sorted(collections.Counter(v for _,_,v in lay).items())))
print("  ct 直方图:", dict(sorted(collections.Counter(v for _,_,v in ct).items())))

# 所有改动点的合集
allsets = {}
for nm, pts in (("L",lay),("water",wat),("ct",ct),("tex",tex),("ramp",ramp),("blight",blgt),("bnd",bnd)):
    for p in pts: allsets.setdefault((p[0],p[1]), set()).add(nm)
print("  改动点合计(去重): %d" % len(allsets))

print("\n=== 预期（size 5 圆 · 欧氏 r<=4）===")
exp_fp = sum(1 for dx in range(-4,5) for dy in range(-4,5) if dx*dx+dy*dy <= 16)
exp_dil = sum(1 for dx in range(-5,6) for dy in range(-5,6) if dx*dx+dy*dy <= 25)
print("  足印(改层) = %d    3x3 膨胀后 = %d    合计 = %d" % (exp_fp, exp_dil, exp_fp+exp_dil))

print("\n=== 判定 ===")
ok = True
if len(lay) != exp_fp:
    print("  ❌ 改层点数 %d ≠ 预期 %d" % (len(lay), exp_fp)); ok=False
else:
    print("  ✅ 改层点数 = %d（足印公式成立）" % len(lay))
if len(wat) != exp_fp:
    print("  ⚠ water 点数 %d ≠ 足印 %d（water 掩码可能更小）" % (len(wat), exp_fp))
else:
    print("  ✅ water 点数 = %d" % len(wat))
if len(allsets) == exp_fp+exp_dil:
    print("  ✅ 改动点合计 = %d（= 足印 + 3x3 膨胀，完全吻合）" % len(allsets))
else:
    print("  ⚠ 改动点合计 %d vs 预期 %d（差 %+d）" % (len(allsets), exp_fp+exp_dil, len(allsets)-(exp_fp+exp_dil)))
print("\n  L 变化方向:", "L 2->0 (深水)" if lay and all(v==0 for _,_,v in lay) else "混合: " + str(sorted(set(v for _,_,v in lay))))
