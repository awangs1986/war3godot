import struct
P=r"D:\tools\we_decrypted.exe"
b=open(P,"rb").read()
pe=struct.unpack_from("<I",b,0x3C)[0]; n=struct.unpack_from("<H",b,pe+6)[0]
os_=struct.unpack_from("<H",b,pe+20)[0]; opt=pe+24
base=struct.unpack_from("<Q",b,opt+24)[0]
secs=[]; off=opt+os_
for i in range(n):
    o=off+i*40; vsz,va,rsz,rp=struct.unpack_from("<IIII",b,o+8); secs.append((va,vsz,rsz,rp))
def va2o(v):
    r=v-base
    for sva,vsz,rsz,rp in secs:
        if sva<=r<sva+max(vsz,rsz): return rp+(r-sva)
    return -1

TABLE = 0x1412E3654
o = va2o(TABLE)
print("=== 坡道合法组合表 @0x%X （16 项 x 0x18 字节）===" % TABLE)
print("  解释: 每项前 4 字节 = 2x2 四角的坡道分类，顺序 (x,y+1),(x+1,y+1),(x+1,y),(x,y)  [顺时针，从左上]")
print("")
rows=[]
for k in range(16):
    ent = b[o + k*0x18 : o + k*0x18 + 0x18]
    q = list(ent[0:4])
    rest = ent[4:].hex()
    rows.append(q)
    print("  #%-2d  四元组 = %-22s   余下 20 字节 = %s" % (k, str(q), rest))

print("\n=== 四元组汇总（去重/统计）===")
import collections
print("  出现过的字节值:", sorted(set(v for q in rows for v in q)))
c = collections.Counter(tuple(q) for q in rows)
for q, cnt in c.most_common():
    print("   %-22s x%d" % (str(list(q)), cnt))

print("\n=== 相邻项差异（看是否按朝向排列）===")
for k in range(1, 16):
    d = [ (i, rows[k-1][i], rows[k][i]) for i in range(4) if rows[k-1][i] != rows[k][i] ]
    print("  #%2d -> #%2d  变化: %s" % (k-1, k, d))
