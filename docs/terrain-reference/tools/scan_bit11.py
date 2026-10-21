import struct, re
P=r"D:\tools\we_decrypted.exe"
b=open(P,"rb").read()
pe=struct.unpack_from("<I",b,0x3C)[0]; n=struct.unpack_from("<H",b,pe+6)[0]
os_=struct.unpack_from("<H",b,pe+20)[0]; opt=pe+24
base=struct.unpack_from("<Q",b,opt+24)[0]
secs=[]; off=opt+os_
for i in range(n):
    o=off+i*40; vsz,va,rsz,rp=struct.unpack_from("<IIII",b,o+8); secs.append((va,vsz,rsz,rp))
def off2va(i):
    for sva,vsz,rsz,rp in secs:
        if rp <= i < rp+rsz: return base + sva + (i-rp)
    return None
def scan(name, pat, hi=None):
    out=[]
    for m in re.finditer(pat, b):
        i=m.start()
        if hi is not None:
            if b[i+3:i+5] != hi: continue
        v=off2va(i)
        if v: out.append(v)
    print("  %-46s 命中 %d: %s" % (name, len(out), [hex(x) for x in out[:14]]))
    return out

print("=== 清 bit11(0x800) / 设 bit11 的 16 位操作 ===")
a = scan("66 81 .. .. FF F7  (and word, 0xF7FF)", b"\x66\x81", b"\xff\xf7")
c = scan("66 81 .. .. 00 08  (or  word, 0x0800)", b"\x66\x81", b"\x00\x08")
print("\n=== 清/设 bit10(0x400) ===")
d = scan("66 81 .. .. FF FB  (and word, 0xFBFF)", b"\x66\x81", b"\xff\xfb")
e = scan("66 81 .. .. 00 04  (or  word, 0x0400)", b"\x66\x81", b"\x00\x04")
print("\n=== 若 16 位形态为 66 81 4? .. 或 c1 形式，退一步扫 32 位片段 ===")
f = scan("81 ?? ?? FF F7 FF FF  (and dword, 0xFFFFF7FF)", b"\x81", None)
