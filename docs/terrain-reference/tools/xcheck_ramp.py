# -*- coding: utf-8 -*-
import sys, os, glob, collections
sys.path.insert(0, r"D:\tools\pr6\docs\terrain-reference\tools")
import w3e_diff as W

# 表里 8 种合法签名（字母）：A=空, L/R=坡道方向
LEGAL = [
 ['R','L','A','A'], ['L','R','A','A'],
 ['A','A','L','R'], ['A','A','R','L'],
 ['A','R','L','A'], ['A','L','R','A'],
 ['R','A','A','L'], ['L','A','A','R'],
]
# 只用"是否坡道"的二值化签名做交叉验证
LEGAL_BIN = set(tuple(1 if c!='A' else 0 for c in q) for q in LEGAL)
print("  表二值化后的合法签名(1=有坡道):", sorted(LEGAL_BIN))

files = []
for d in (r"D:\tools\pr6\docs\terrain-reference\fixtures",
          r"D:\tools\pr6\docs\terrain-reference\experiments\artifacts",
          r"D:\tools\exp"):
    files += glob.glob(os.path.join(d, "*.w3e")) + glob.glob(os.path.join(d, "*.w3m"))

def load_any(p):
    if p.endswith(".w3m"):
        import mpyq, tempfile, os
        a = mpyq.MPQArchive(p, listfile=False)
        raw = a.read_file(b"war3map.w3e")
        t = os.path.join(tempfile.gettempdir(), "x.w3e"); open(t,"wb").write(raw)
        return W.load(t)
    return W.load(p)

results = []
allpat = collections.Counter()
for p in sorted(files):
    try:
        h, c = load_any(p)
    except Exception:
        continue
    Wd, Hd = h["w"], h["h"]
    ramp_i = [i for i in range(Wd*Hd) if c[i].ramp]
    if not ramp_i:
        continue
    pats = collections.Counter()
    for i in ramp_i:
        x, y = i % Wd, i // Wd
        # 顺序 (x,y+1),(x+1,y+1),(x+1,y),(x,y) —— 越界按 0 处理
        def r(a,b):
            if 0 <= a < Wd and 0 <= b < Hd: return 1 if c[b*Wd+a].ramp else 0
            return 0
        sig = (r(x,y+1), r(x+1,y+1), r(x+1,y), r(x,y))
        pats[sig] += 1
        allpat[sig] += 1
    results.append((os.path.basename(p), len(ramp_i), len(pats), pats))

print("\n=== 每个坡道存档的 2x2 二值签名分布 ===")
for name, n, np_, pats in results:
    legal_all = all(s in LEGAL_BIN for s in pats)
    mark = "OK" if legal_all else "含表外签名"
    print("  %-26s ramp=%3d  签名种类=%d  [%s]" % (name[:26], n, np_, mark))
    for s, cnt in sorted(pats.items()):
        tag = "" if s in LEGAL_BIN else "   <== 不在表的 8 种内"
        print("        %s x%d%s" % (str(list(s)), cnt, tag))

print("\n=== 全体签名汇总（去重）===")
for s, cnt in sorted(allpat.items()):
    tag = "合法" if s in LEGAL_BIN else "**表外**"
    print("  %-20s x%-5d %s" % (str(list(s)), cnt, tag))
print("\n  表内 8 种:", len(LEGAL_BIN), "  实测出现:", len(allpat), "  表外:", len([s for s in allpat if s not in LEGAL_BIN]))
