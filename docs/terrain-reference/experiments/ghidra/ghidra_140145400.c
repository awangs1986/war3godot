/* FUN_140145400 —— 加权随机选择（§54.2；由 FUN_140286F40 thunk 尾调用进入）
 * 切片: f_var.exe = 0x140145400-0x140145E00
 * 证据: ev_9867aed6d365342f8af68da7df5a881cbf1e33e05887566b564eb3af63ef2c14
 * 关键: 项 = 8 字节 {int id, int weight}；forceMin100 时总权重不足 100 补到 100；
 *       取模用 (total-1) => k 落在 1..total-1（★ 理论取不到最后一个权重单位）
 */undefined4 FUN_140145400(uint *param_1,char param_2)
{
  longlong lVar1, lVar2, lVar3, lVar4, lVar6, lVar8;
  uint uVar5, uVar7;
  int *piVar9;
  int iVar10, iVar12, iVar13, iVar14, iVar15, iVar16, iVar17, iVar18, iVar19;
  ulonglong uVar11;

  uVar5 = *param_1;                                    /* 项数 */
  iVar12 = 0; iVar10 = 0;
  if ((0 < (int)uVar5) && (7 < uVar5)) {               /* 8 项展开求权重和（性能优化） */
    lVar8 = *(longlong *)(param_1 + 2);                /* 项数组 */
    iVar12 = 0; iVar14 = 0; iVar16 = 0; iVar18 = 0; iVar13 = 0; iVar15 = 0; iVar17 = 0; iVar19 = 0;
    uVar7 = uVar5 & 0x80000007;
    if ((int)uVar7 < 0) uVar7 = (uVar7 - 1 | 0xfffffff8) + 1;
    lVar6 = 0;
    do {
      iVar10 = iVar10 + 8;
      lVar1 = lVar6 * 8; lVar2 = lVar6 * 8; lVar3 = lVar6 * 8;
      iVar12 = *(int *)(lVar8 + 4 + lVar6 * 8) + iVar12;
      iVar14 = *(int *)(lVar8 + 0xc + lVar6 * 8) + iVar14;
      iVar16 = *(int *)(lVar8 + 0x14 + lVar6 * 8) + iVar16;
      iVar18 = *(int *)(lVar8 + 0x1c + lVar6 * 8) + iVar18;
      lVar4 = lVar6 * 8;
      lVar6 = lVar6 + 8;
      iVar13 = *(int *)(lVar8 + 0x24 + lVar4) + iVar13;
      iVar15 = *(int *)(lVar8 + 0x2c + lVar3) + iVar15;
      iVar17 = *(int *)(lVar8 + 0x34 + lVar1) + iVar17;
      iVar19 = *(int *)(lVar8 + 0x3c + lVar2) + iVar19;
    } while (lVar6 < (int)(uVar5 - uVar7));
    iVar12 = iVar13 + iVar12 + iVar17 + iVar16 + iVar15 + iVar14 + iVar19 + iVar18;
  }
  iVar14 = 0; iVar13 = 0;
  if (iVar10 < (int)uVar5) {                           /* 尾部两两展开 */
    if (1 < (int)(uVar5 - iVar10)) {
      lVar8 = (longlong)iVar10 * 8;
      uVar7 = ((uVar5 - iVar10) - 2 >> 1) + 1;
      uVar11 = (ulonglong)uVar7;
      iVar10 = iVar10 + uVar7 * 2;
      iVar13 = 0;
      do {
        iVar14 = iVar14 + *(int *)(lVar8 + 4 + *(longlong *)(param_1 + 2));
        iVar13 = iVar13 + *(int *)(lVar8 + 0xc + *(longlong *)(param_1 + 2));
        lVar8 = lVar8 + 0x10;
        uVar11 = uVar11 - 1;
      } while (uVar11 != 0);
    }
    if (iVar10 < (int)uVar5) {
      iVar12 = iVar12 + *(int *)(*(longlong *)(param_1 + 2) + 4 + (longlong)iVar10 * 8);
    }
    iVar12 = iVar12 + iVar13 + iVar14;
  }
  if ((param_2 != '\0') && (iVar12 < 100)) iVar12 = 100;            /* forceMin100 */
  if (iVar12 + -1 < 1) {
    iVar12 = 1;
  } else {
    iVar10 = func_0x00014118cab0();                    /* rand */
    iVar12 = iVar10 % (iVar12 + -1) + 1;               /* ★ % (total-1) + 1 */
  }
  iVar13 = 0; iVar10 = 0;
  if (0 < (int)uVar5) {                                /* 累计权重选第 k 个 */
    piVar9 = (int *)(*(longlong *)(param_1 + 2) + 4);
    do {
      iVar13 = iVar13 + *piVar9;
      if (iVar12 <= iVar13) return *(undefined4 *)(*(longlong *)(param_1 + 2) + (longlong)iVar10 * 8);
      iVar10 = iVar10 + 1;
      piVar9 = piVar9 + 2;
    } while (iVar10 < (int)uVar5);
  }
  return 0;
}
