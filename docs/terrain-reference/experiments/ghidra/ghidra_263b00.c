/* FUN_140263B00 —— 脏块收集（§47.2）
 * 切片: f_wr2.exe = 0x140263B00-0x140263F00
 * 证据: ev_6fccbe326845954e08879baf413d4be84de090d01ee1392487acacd80679209d
 * 关键: rec+0 = 打包角点世界坐标(X>>0x17 / Y &0x7fc000)；世界半径 = margin*C+1.0；脏块 4x4 格 stride 0xFD8
 */void FUN_140263B00(longlong param_1,ulonglong param_2,ulonglong *param_3,int param_4)
{
  float fVar1, fVar18;
  uint uVar2;
  undefined8 *puVar3;
  ulonglong uVar4, uVar7, uVar9, uVar10, uVar12, uVar13;
  uint uVar5, uVar6, uVar11;
  undefined8 *puVar8, *puVar14;
  int iVar15, lVar16_i;
  longlong lVar16;
  float *pfVar17;
  undefined1 auStack_98 [32];
  undefined8 local_78;
  float local_70, local_6c, local_64, local_60, local_58, local_54, local_4c, local_48;
  undefined4 local_68, local_5c, local_50, local_44;
  ulonglong local_40;

  fVar1 = _DAT_1412e1f5c;
  local_40 = _DAT_1417e9788 ^ (ulonglong)auStack_98;
  pfVar17 = &local_70;
  lVar16 = 4;                                        /* 最多 4 个脏块 */
  local_68 = 0;
  uVar2 = *(uint *)((param_2 & 0xffffffff) * 0x1c + *(longlong *)(param_1 + 0x188));
  local_5c = 0;
  fVar18 = (float)param_4 * _DAT_1412b83d0 + _DAT_1412a0cc4;   /* 世界半径 = size*C + 1.0f */
  local_50 = 0;
  local_58 = (float)((uVar2 >> 0x17 | 0x11e00) << 0xe) - _DAT_1412e1f70;   /* worldX */
  local_78 = CONCAT44(local_78._4_4_,uVar2) & 0xffffffff007fc000 | 0x47800000;
  local_6c = (float)(uVar2 & 0x7fc000 | 0x47800000) - _DAT_1412e1f70;      /* worldY */
  local_44 = 0;
  local_70 = local_58 - fVar18;  local_58 = local_58 + fVar18;
  local_60 = local_6c + fVar18;  local_6c = local_6c - fVar18;
  local_64 = local_70; local_54 = local_6c; local_4c = local_58; local_48 = local_60;
  do {                                               /* 四个角各查一次 */
    uVar4 = 0;
    uVar6 = *(int *)(param_1 + 0xf4) - 1U >> 2;      /* 块列数 = (W-1)>>2 */
    uVar11 = *(int *)(param_1 + 0xf8) - 1U >> 2;     /* 块行数 */
    uVar5 = (uint)((*pfVar17 - *(float *)(param_1 + 0x100)) * fVar1);
    uVar2 = (uint)((pfVar17[1] - *(float *)(param_1 + 0xfc)) * fVar1);
    if (uVar6 < uVar5) uVar5 = ~((int)uVar5 >> 0x1f) & uVar6;
    if (uVar11 < uVar2) uVar2 = ~((int)uVar2 >> 0x1f) & uVar11;
    uVar13 = param_3[1];
    uVar9 = 0;
    iVar15 = (uVar6 + 1) * uVar2 + uVar5;
    if (uVar13 == 0) {
LAB_140263cde:
      puVar3 = (undefined8 *)*param_3;
      uVar4 = param_3[2];
      uVar10 = (longlong)&local_78 - (longlong)puVar3 >> 3;
      uVar9 = (longlong)iVar15 * 0xfd8 + *(longlong *)(param_1 + 0x1d0);   /* 块记录 stride 0xFD8 */
      uVar12 = uVar4 & 0x7fffffffffffffff;
      local_78 = uVar9;
      if ((uVar13 == uVar12) && (uVar12 < uVar12 + 1)) {   /* 扩容 */
        uVar7 = uVar4 >> 1 & 0x3fffffffffffffff;
        uVar13 = 1;
        if (uVar12 + 1 <= uVar12 + uVar7) uVar13 = uVar7;
        param_3[2] = (uVar13 + uVar12 ^ uVar4) & 0x7fffffffffffffff ^ uVar4;
        puVar3 = (undefined8 *)func_0x000141080b70((uVar13 + uVar12) * 8,0x1417f2a18,0xfffffffa);
        puVar14 = (undefined8 *)*param_3;
        uVar13 = 0;
        uVar4 = param_3[1] & 0x1fffffffffffffff;
        if (puVar14 + param_3[1] < puVar14) uVar4 = uVar13;
        puVar8 = puVar3;
        if (uVar4 != 0) {
          do { uVar13 = uVar13 + 1; *puVar8 = *puVar14; puVar14 = puVar14 + 1; puVar8 = puVar8 + 1; } while (uVar13 != uVar4);
          puVar14 = (undefined8 *)*param_3;
        }
        uVar4 = param_3[2];
        if (-1 < (longlong)uVar4) {
          func_0x000141080be0(puVar14,0x1417f2a18,0xfffffffa);
          uVar4 = param_3[2];
        }
        *param_3 = (ulonglong)puVar3;
        param_3[2] = uVar4 & 0x7fffffffffffffff;
      }
      uVar4 = param_3[1];
      if (uVar10 < uVar4) puVar3[uVar4] = puVar3[uVar10];
      else puVar3[uVar4] = uVar9;
      param_3[1] = param_3[1] + 1;
    } else {
      do {                                             /* 线性去重 */
        if (*(longlong *)(*param_3 + uVar4 * 8) ==
            *(longlong *)(param_1 + 0x1d0) + (longlong)iVar15 * 0xfd8) uVar9 = 1;
        uVar4 = (ulonglong)((int)uVar4 + 1);
      } while (uVar4 < uVar13);
      if ((char)uVar9 == '\0') goto LAB_140263cde;
    }
    pfVar17 = pfVar17 + 3;
    lVar16 = lVar16 + -1;
    if (lVar16 == 0) {
      func_0x00014115d620(local_40 ^ (ulonglong)auStack_98);
      return;
    }
  } while( true );
}
