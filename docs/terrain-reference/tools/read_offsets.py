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
def dwords(va, count):
    o = va2o(va)
    return [struct.unpack_from("<i", b, o+4*i)[0] for i in range(count)]

print("=== A3: 三邻偏移表（26e050 用 0x1412e165c/166c；27f170 用 0x1412e1658/1668）===")
for name, va in (("0x1412E1658 (x?)", 0x1412e1658), ("0x1412E165C (x)", 0x1412e165c),
                 ("0x1412E1668 (y?)", 0x1412e1668), ("0x1412E166C (y)", 0x1412e166c)):
    print("  %-20s -> %s" % (name, dwords(va, 4)))

print("\n  若按 (x=165c, y=166c) 配对，前 3 组偏移 =", list(zip(dwords(0x1412e165c,4), dwords(0x1412e166c,4))))
print("  若按 (x=1658, y=1668) 配对，前 4 组偏移 =", list(zip(dwords(0x1412e1658,4), dwords(0x1412e1668,4))))

print("\n=== 对照 1.27 已解出的偏移（记录值）===")
print("  1.27 三邻: (1,0) (0,1) (1,1)   —— 待与本表比对")
