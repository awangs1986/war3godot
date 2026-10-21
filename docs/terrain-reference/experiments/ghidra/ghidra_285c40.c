/* FUN_140285C40 —— 渲染用边界位图刷新（§53）
 * 切片: f_bnd2.exe = 0x140285C40-0x140287000
 * 证据: ev_ca70f6bc418526833e8f69bd494e9daa8bc4c1f8a7d820aa9597ca4adfd23ca9
 * 关键: map+0x388 = 4x 分辨率字节位图（stride = W*4），bit4(0x10)=边界渲染标志；
 *       先整片 &= 0xEF 清，再对 flags&0xc0 的角点 |= 0x10（bit6/bit7 都参与判定）
 * 注意: 该函数【不写 w3e】，属于编辑器渲染
 */void FUN_140285C40(longlong param_1,int *param_2)
{
  int iVar1, iVar2, iVar3, iVar5, iVar6, iVar8, iVar9, iVar13, iVar16, iVar19, iVar20;
  uint uVar4;
  byte *pbVar7, *pbVar11;
  longlong lVar10, lVar15, lVar18;
  ulonglong uVar12, uVar14, uVar17;

  iVar20 = *(int *)(param_1 + 0xf4);                   /* W */
  iVar1 = iVar20 * 4;                                  /* 位图 stride = W*4 */
  iVar19 = param_2[1] + -1; if (iVar19 < 0) iVar19 = 0;
  iVar13 = *param_2 + -1;   if (iVar13 < 0) iVar13 = 0;
  iVar2  = param_2[3] + 1;  if (iVar20 < param_2[3] + 1) iVar2 = iVar20;
  iVar20 = iVar13 * 4;
  iVar3  = param_2[2] + 1;  if (*(int *)(param_1 + 0xf8) < param_2[2] + 1) iVar3 = *(int *)(param_1 + 0xf8);
  iVar16 = iVar20 + 3;
  lVar18 = (longlong)(iVar2 * 4 + -3);
  iVar8  = iVar3 * 4 + -3;
  iVar5  = iVar13;
  if (iVar16 < iVar8) {                                /* ① 整片清 bit4 */
    lVar15 = (longlong)(iVar19 * 4 + 3);
    pbVar11 = (byte *)(iVar16 * iVar1 + lVar15 + *(longlong *)(param_1 + 0x388));
    uVar17 = (ulonglong)(uint)(iVar8 - iVar16);
    do {
      if (lVar15 < lVar18) {
        lVar10 = lVar18 - lVar15;
        pbVar7 = pbVar11;
        do { *pbVar7 = *pbVar7 & 0xef; pbVar7 = pbVar7 + 1; lVar10 = lVar10 + -1; } while (lVar10 != 0);
      }
      pbVar11 = pbVar11 + iVar1;
      uVar17 = uVar17 - 1;
    } while (uVar17 != 0);
  }
  for (; iVar5 < iVar3; iVar5 = iVar5 + 1) {
    iVar8 = iVar19 * 4;
    for (iVar16 = iVar19; iVar16 < iVar2; iVar16 = iVar16 + 1) {
      if ((*(byte *)((longlong)((*(int *)(param_1 + 0xf4) + 1) * iVar5 + iVar16) * 0x1c + 0xc +
                    *(longlong *)(param_1 + 0x188)) & 0xc0) != 0) {     /* bit6|bit7 */
        iVar9 = iVar20 + -1; if (iVar5 == iVar13) iVar9 = iVar20;
        iVar6 = iVar8 + -1;  if (iVar16 == iVar19) iVar6 = iVar8;
        pbVar11 = (byte *)((longlong)(iVar9 * iVar1 + iVar6) + *(longlong *)(param_1 + 0x388));
        uVar4 = (iVar16 != iVar19) + 5;
        if (iVar16 == iVar2 + -1) uVar4 = (iVar16 != iVar19) + 4;
        uVar14 = (ulonglong)uVar4;
        uVar12 = (ulonglong)((iVar5 != iVar13) + 5);
        uVar17 = uVar14;
        pbVar7 = pbVar11;
        if (iVar5 == iVar3 + -1) uVar12 = (ulonglong)((iVar5 != iVar13) + 4);
        do {                                           /* ② 对应块置 bit4 */
          do { *pbVar11 = *pbVar11 | 0x10; pbVar11 = pbVar11 + 1; uVar17 = uVar17 - 1; } while (uVar17 != 0);
          pbVar11 = pbVar7 + iVar1;
          uVar12 = uVar12 - 1;
          uVar17 = uVar14;
          pbVar7 = pbVar11;
        } while (uVar12 != 0);
      }
      iVar8 = iVar8 + 4;
    }
    iVar20 = iVar20 + 4;
  }
  return;
}
