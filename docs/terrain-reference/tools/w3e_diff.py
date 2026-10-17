#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""w3e_diff.py — W3E 统一解析与逐字段差分（实验证据唯一入口）

字段表（v11，每角点 7 字节，零起始偏移）——与
`standalone/W3Editor.Core/Formats/W3EMap.cs:222-233` 的 v11 分支一致：

    0-1  地面高度（raw，语义见 GroundHeight 换算）
    2-3  水面高度（低 14 位）+ 地图边缘位 0x4000
    4    低 4 位 = 纹理索引；0x10 坡道 / 0x20 荒芜 / 0x40 水 / 0x80 边界
    5    低 5 位 = 地表变体；高 3 位 = 悬崖变体
    6    低 4 位 = 层级；高 4 位 = 悬崖类型

注意：这是 **W3E 文件字段**，不是编辑器进程内存记录字段
（内存记录 0x1c 字节，布局不同，见 docs/terrain-reference/experiments/dynamic-memory-address-map.md）。

用法：
    python w3e_diff.py A.w3e B.w3e [--only blight,boundary] [--json]
    python w3e_diff.py --count blight A.w3e B.w3e C.w3e
"""
import struct
import sys
import json

CS = 7  # bytes per corner

FIELD_NAMES = [
    "groundHeightRaw", "waterRaw", "tex", "ramp", "blight", "water", "boundary",
    "groundVar", "cliffVar", "layer", "cliffType",
]

# 字段名 → Corner 属性名（groundHeightRaw 在对象里叫 h）
ATTR = {"groundHeightRaw": "h", "mapEdge": "_edge"}


class Corner:
    __slots__ = ("h", "waterRaw", "tex", "ramp", "blight", "water", "boundary",
                 "groundVar", "cliffVar", "layer", "cliffType", "_edge")

    def __init__(self, b):
        self.h = struct.unpack_from("<H", b, 0)[0]
        w = struct.unpack_from("<H", b, 2)[0]
        self.waterRaw = w & 0x3FFF
        edge = bool(w & 0x4000)
        tf = b[4]
        self.tex = tf & 0x0F
        self.ramp = bool(tf & 0x10)
        self.blight = bool(tf & 0x20)
        self.water = bool(tf & 0x40)
        self.boundary = bool(tf & 0x80)
        self.groundVar = b[5] & 0x1F
        self.cliffVar = (b[5] & 0xE0) >> 5
        self.layer = b[6] & 0x0F
        self.cliffType = (b[6] & 0xF0) >> 4
        self._edge = edge

    def as_dict(self):
        d = {k: getattr(self, k) for k in FIELD_NAMES}
        d["mapEdge"] = self._edge
        return d


def parse_header(d):
    """按版本与 tileset 列表长度计算头部偏移（禁止硬编码）。"""
    if d[:4] != b"W3E!":
        raise ValueError("not a W3E file (missing W3E! signature)")
    off = 4
    version = struct.unpack_from("<I", d, off)[0]; off += 4
    tileset = d[off]; off += 1
    custom = struct.unpack_from("<I", d, off)[0]; off += 4
    gnd_n = struct.unpack_from("<I", d, off)[0]; off += 4 + 4 * gnd_n
    clf_n = struct.unpack_from("<I", d, off)[0]; off += 4 + 4 * clf_n
    w = struct.unpack_from("<I", d, off)[0]
    h = struct.unpack_from("<I", d, off + 4)[0]
    off += 8
    cx = struct.unpack_from("<f", d, off)[0]
    cy = struct.unpack_from("<f", d, off + 4)[0]
    off += 8
    expected = off + w * h * CS
    if expected != len(d):
        raise ValueError(
            f"size mismatch: header={off} W={w} H={h} → expected {expected}, actual {len(d)}")
    return dict(version=version, tileset=tileset, custom=custom, gnd=gnd_n,
                cliff=clf_n, w=w, h=h, cx=cx, cy=cy, off=off)


def load(path):
    d = open(path, "rb").read()
    hd = parse_header(d)
    off, w, h = hd["off"], hd["w"], hd["h"]
    corners = [Corner(d[off + i * CS: off + (i + 1) * CS]) for i in range(w * h)]
    return hd, corners


def diff(path_a, path_b, only=None):
    ha, ca = load(path_a)
    hb, cb = load(path_b)
    if (ha["w"], ha["h"]) != (hb["w"], hb["h"]):
        raise ValueError("map size differs between inputs")
    w, h = ha["w"], ha["h"]
    rows = []
    for i in range(w * h):
        a, b = ca[i], cb[i]
        for f in FIELD_NAMES + ["mapEdge"]:
            attr = ATTR.get(f, f)
            va = getattr(a, attr)
            vb = getattr(b, attr)
            if va != vb:
                if only and f not in only:
                    continue
                rows.append(dict(index=i, x=i % w, y=i // w, field=f,
                                 before=va, after=vb))
    return ha, hb, rows


def counts(paths):
    """每个文件里各布尔标志的置位数。"""
    out = {}
    for p in paths:
        hd, cs = load(p)
        out[p] = {f: sum(1 for c in cs if getattr(c, f)) for f in
                  ("ramp", "blight", "water", "boundary")}
        out[p]["_map"] = f'{hd["w"]}x{hd["h"]} v{hd["version"]} off={hd["off"]}'
    return out


def main(argv):
    if len(argv) >= 2 and argv[0] == "--count":
        for p, c in counts(argv[1:]).items():
            flags = ", ".join(f"{k}={c[k]}" for k in ("ramp", "blight", "water", "boundary"))
            print(f"{p}: {c['_map']}  {flags}")
        return 0
    if len(argv) < 2:
        print(__doc__)
        return 2
    only = None
    as_json = "--json" in argv
    args = [a for a in argv if not a.startswith("--")]
    for a in argv:
        if a.startswith("--only"):
            only = set(x.strip() for x in a.split("=", 1)[1].split(","))
    ha, hb, rows = diff(args[0], args[1], only)
    if as_json:
        print(json.dumps(dict(a=ha, b=hb, rows=rows), ensure_ascii=False, indent=1))
    else:
        print(f'# {args[0]} → {args[1]}   W={ha["w"]} H={ha["h"]} 头={ha["off"]}')
        print(f"# 差异条目 {len(rows)}（每条 = 一个角点的一个字段）")
        for r in rows:
            print(f'({r["x"]:>3},{r["y"]:>3}) {r["field"]:<14} {r["before"]} → {r["after"]}')
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
