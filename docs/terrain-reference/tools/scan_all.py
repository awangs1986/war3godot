# -*- coding: utf-8 -*-
import sys, os, glob, struct, math, collections
sys.path.insert(0, r"D:\tools\pr6\docs\terrain-reference\tools")
import w3e_diff as W

BASE_L, BASE_CT, BASE_TEX = 2, 15, 0
DIRS = [("fixtures", r"D:\tools\pr6\docs\terrain-reference\fixtures"),
        ("artifacts", r"D:\tools\pr6\docs\terrain-reference\experiments\artifacts")]

seen = []
rows = []
for tag, d in DIRS:
    for p in sorted(glob.glob(os.path.join(d, "*.w3e"))):
        name = os.path.basename(p)[:-4]
        try:
            h, c = W.load(p)
            raw = open(p,"rb").read()
            ver = struct.unpack_from("<i", raw, 4)[0]
            Wd, Hd = h["w"], h["h"]
            nL = nCt = nW = nR = nB = nT = nBnd = 0
            xs=[]; ys=[]
            for i in range(Wd*Hd):
                q = c[i]
                hit = False
                if q.layer != BASE_L: nL += 1; hit=True
                if q.cliffType != BASE_CT: nCt += 1; hit=True
                if q.water: nW += 1; hit=True
                if q.ramp: nR += 1; hit=True
                if q.blight: nB += 1; hit=True
                if q.tex != BASE_TEX: nT += 1; hit=True
                if q.boundary: nBnd += 1; hit=True
                if hit: xs.append(i % Wd); ys.append(i // Wd)
            if xs:
                cx = sum(xs)/len(xs); cy = sum(ys)/len(ys)
                rmax = max(math.hypot(x-cx, y-cy) for x, y in zip(xs, ys))
                bbox = "(%d..%d,%d..%d)" % (min(xs),max(xs),min(ys),max(ys))
            else:
                rmax = 0.0; bbox = "-"
            rows.append((tag, name, ver, Wd, Hd, nL, nCt, nW, nR, nB, nT, nBnd, rmax, bbox))
        except Exception as e:
            rows.append((tag, name, -1, 0, 0, -1,-1,-1,-1,-1,-1,-1, 0.0, "ERR:"+str(e)[:30]))

print("=== 全量扫描：%d 个 w3e（基准 L=2 / ct=15 / tex=0）===" % len(rows))
print("%-40s %3s %7s %5s %5s %5s %5s %5s %5s %5s %6s  %s" %
      ("文件","ver","W x H","L","ct","wat","ramp","blgt","tex","bnd","rmax","bbox"))
for r in rows:
    tag,name,ver,Wd,Hd,nL,nCt,nW,nR,nB,nT,nBnd,rmax,bbox = r
    print("%-40s %3d %7s %5d %5d %5d %5d %5d %5d %5d %6.2f  %s" %
          (name[:40], ver, "%dx%d"%(Wd,Hd), nL,nCt,nW,nR,nB,nT,nBnd, rmax, bbox))

print("\n=== 汇总：按特征分组（便于发现未解释的形态）===")
pat = collections.Counter()
for r in rows:
    tag,name,ver,Wd,Hd,nL,nCt,nW,nR,nB,nT,nBnd,rmax,bbox = r
    if ver < 0: pat["读取失败"] += 1; continue
    key = []
    if nL: key.append("L")
    if nCt: key.append("ct")
    if nW: key.append("water")
    if nR: key.append("ramp")
    if nB: key.append("blight")
    if nT: key.append("tex")
    if nBnd: key.append("bnd")
    pat["+".join(key) if key else "(无变化)"] += 1
for k, v in pat.most_common():
    print("  %-42s %d 个文件" % (k, v))

print("\n=== 非 65x65 的地图（B3 相关）===")
odd = [r for r in rows if r[3] and (r[3]!=65 or r[4]!=65)]
print("  %d 个: %s" % (len(odd), ", ".join("%s(%dx%d)"%(r[1],r[3],r[4]) for r in odd[:10])) if odd else "  无（全部 65x65）")

print("\n=== w3e 版本分布 ===")
print("  " + str(dict(collections.Counter(r[2] for r in rows))))
