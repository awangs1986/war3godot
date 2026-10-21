/* FUN_14027EF50 —— t = 本格是否为 3x3 坡道邻域最高层；并写 flags 缓存（§50/§51）
 * 切片: f_t.exe = 0x14027EF50-0x14027F170
 * 证据: ev_ad6c5f70f5505215ce9102975fcd5d97dc98d35721354841deb4aae6c1a9dadb
 * 关键: rec.flags(ushort @+0xc) bit11(0x800)=已计算, bit10(0x400)=缓存值(t)
 *       邻域 = 4 锚点 {(0,0),(1,0),(0,1),(1,1)} x 每锚点 2x2 => 3x3
 */ulonglong FUN_14027EF50(longlong param_1,int param_2,int param_3)
{
  int iVar1;
  uint uVar2, uVar4, uVar8, uVar10, uVar11;
  longlong lVar3, lVar9, lVar12, lVar13;
  ulonglong uVar5;
  uint uVar6;
  ushort uVar7;

  uVar2 = *(uint *)(param_1 + 0xf4);
  lVar3 = *(longlong *)(param_1 + 0x188);
  uVar5 = (ulonglong)(int)((uVar2 + 1) * param_3 + param_2);
  lVar9 = uVar5 * 0x1c + lVar3;
  if (DAT_14183c8e0 == '\0') {                         /* 缓存开关 */
    uVar7 = *(ushort *)(lVar9 + 0xc);
    uVar5 = (ulonglong)uVar7;
    if ((uVar7 & 0x800) != 0) return (ulonglong)(uVar7 >> 10) & 0xffffffffffffff01;  /* bit11 已算 => 返回 bit10 */
  }
  uVar8 = *(uint *)(param_1 + 0xbd4);                  /* minL 初值 */
  uVar6 = 0;                                           /* maxL */
  lVar12 = 0;
  do {                                                 /* 4 个锚点 */
    uVar11 = param_2 - *(int *)(&DAT_1412e1658 + lVar12);
    uVar10 = param_3 - *(int *)(&DAT_1412e1668 + lVar12);
    if ((uVar11 < uVar2) && (uVar10 < *(uint *)(param_1 + 0xf8))) {
      iVar1 = uVar2 + 1;
      /* 四邻（每次一个 2x2 块） */
      lVar13 = (longlong)(int)((_DAT_1412e1668 + uVar10) * iVar1 + _DAT_1412e1658 + uVar11) * 0x1c;
      if ((*(byte *)(lVar13 + 0xc + lVar3) >> 1 & 1) != 0) {
        uVar4 = *(byte *)(lVar13 + 10 + lVar3) & 0xf;
        if ((int)uVar4 < (int)uVar8) uVar8 = uVar4;
        if (uVar6 < uVar4) uVar6 = uVar4;
      }
      lVar13 = (longlong)(int)((_DAT_1412e166c + uVar10) * iVar1 + _DAT_1412e165c + uVar11) * 0x1c;
      if ((*(byte *)(lVar13 + 0xc + lVar3) >> 1 & 1) != 0) {
        uVar4 = *(byte *)(lVar13 + 10 + lVar3) & 0xf;
        if ((int)uVar4 < (int)uVar8) uVar8 = uVar4;
        if (uVar6 < uVar4) uVar6 = uVar4;
      }
      lVar13 = (longlong)(int)((_DAT_1412e1670 + uVar10) * iVar1 + _DAT_1412e1660 + uVar11) * 0x1c;
      if ((*(byte *)(lVar13 + 0xc + lVar3) >> 1 & 1) != 0) {
        uVar4 = *(byte *)(lVar13 + 10 + lVar3) & 0xf;
        if ((int)uVar4 < (int)uVar8) uVar8 = uVar4;
        if (uVar6 < uVar4) uVar6 = uVar4;
      }
      lVar13 = (longlong)(int)((uVar10 + _DAT_1412e1674) * iVar1 + _DAT_1412e1664 + uVar11) * 0x1c;
      uVar5 = 0;
      if ((*(byte *)(lVar13 + 0xc + lVar3) >> 1 & 1) != 0) {
        uVar10 = *(byte *)(lVar13 + 10 + lVar3) & 0xf;
        uVar5 = 0;
        if ((int)uVar10 < (int)uVar8) uVar8 = uVar10;
        if (uVar6 < uVar10) uVar6 = uVar10;
      }
    }
    lVar12 = lVar12 + 4;
  } while (lVar12 < 0x10);
  if (uVar8 == uVar6) {
    uVar5 = uVar5 & 0xffffffffffffff00;                /* 邻域全同层 => false */
  } else {
    uVar5 = (ulonglong)((*(byte *)(lVar9 + 10) & 0xf) == uVar6);   /* 本格 == 最高层 */
  }
  if (DAT_14183c8e0 == '\0') {                         /* 写回缓存 */
    uVar7 = *(ushort *)(lVar9 + 0xc) | 0x400;
    if ((char)uVar5 == '\0') uVar7 = *(ushort *)(lVar9 + 0xc) & 0xfbff;
    *(ushort *)(lVar9 + 0xc) = uVar7 | 0x800;
  }
  return uVar5;
}
