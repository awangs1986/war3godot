
/* WARNING: Globals starting with '_' overlap smaller symbols at the same address */

void FUN_140314880(longlong param_1,int param_2,undefined4 param_3,int param_4,uint param_5,
                  char param_6,ulonglong param_7,longlong param_8)

{
  undefined1 auVar1 [16];
  float fVar2;
  longlong lVar3;
  ulonglong uVar4;
  int iVar5;
  int iVar6;
  uint uVar7;
  uint uVar8;
  int iVar9;
  undefined1 (*pauVar10) [16];
  int iVar11;
  int iVar12;
  uint *puVar13;
  byte bVar14;
  int iVar15;
  float fVar16;
  float fVar17;
  float fVar18;
  float fVar19;
  undefined1 auStack_158 [32];
  uint uStack_138;
  ulonglong uStack_130;
  ulonglong uStack_128;
  longlong lStack_120;
  float fStack_118;
  uint uStack_114;
  undefined8 *puStack_110;
  int iStack_108;
  int iStack_104;
  undefined4 uStack_100;
  undefined1 auStack_f8 [16];
  undefined8 uStack_e8;
  undefined4 uStack_e0;
  longlong lStack_d8;
  ulonglong uStack_d0;
  undefined8 uStack_c8;
  undefined1 *puStack_c0;
  undefined8 uStack_b8;
  ulonglong uStack_b0;
  undefined1 auStack_a8 [32];
  ulonglong uStack_88;
  
  uStack_c8 = 0xfffffffffffffffe;
  uStack_88 = _DAT_1417e9788 ^ (ulonglong)auStack_158;
  uStack_d0 = param_7;
  lStack_d8 = param_8;
  uStack_100 = param_3;
  iVar6 = FUN_14026c340(param_1,param_3,1);
  if ((iVar6 != -1) && (4 < param_2)) {
    uVar7 = func_0x000140263fb0(param_1);
    uVar8 = *(int *)(param_1 + 0xf4) + 1;
    uStack_114 = uVar7 / uVar8;
    uVar8 = uVar7 % uVar8;
    puVar13 = (uint *)((longlong)(int)uVar7 * 0x1c + *(longlong *)(param_1 + 0x188));
    bVar14 = (byte)puVar13[3] >> 4 & 1;
    fVar16 = (float)FUN_1403bde90(param_1,puVar13,7);
    fVar2 = _DAT_1412b3e14;
    iVar6 = 0;
    if (param_6 != '\0') {
      if (bVar14 == 0) {
        if (param_2 == 5) {
          iVar6 = -2;
        }
        else if (param_2 == 6) {
          iVar6 = -1;
        }
      }
      else {
        fVar17 = ((float)((puVar13[1] >> 0x12 | 0x22c000) << 9) - _DAT_1412b3e14) +
                 *(float *)(*(longlong *)(param_1 + 0x4d8) + 0x108);
        if (param_2 == 5) {
          fVar17 = fVar17 - *(float *)(param_1 + 0x4cc);
        }
        iVar6 = (int)((fVar17 - fVar16) * _DAT_1412b83c8);
        if (fVar17 < fVar16) {
          iVar6 = iVar6 + -1;
        }
      }
      *(uint *)(param_1 + 0x948) = (*(byte *)((longlong)puVar13 + 10) & 0xf) + iVar6;
      fStack_118 = (float)((*puVar13 & 0x3fff | 0x22c000) << 9);
      *(float *)(param_1 + 0x94c) = fStack_118 - fVar2;
      if (bVar14 == 0) {
        *(float *)(param_1 + 0x950) = fVar16;
      }
      else {
        fStack_118 = (float)((puVar13[1] >> 0x12 | 0x22c000) << 9);
        *(float *)(param_1 + 0x950) = fStack_118 - fVar2;
      }
    }
    puStack_110 = (undefined8 *)0x0;
    if (param_8 != 0) {
      puStack_110 = (undefined8 *)FUN_141080b70(0x20,&UNK_14129ed38,0);
      *puStack_110 = &UNK_1412e2c68;
      *(undefined4 *)(puStack_110 + 1) = 0;
      puStack_110[2] = 0;
      *(undefined4 *)(puStack_110 + 3) = 0;
      FUN_140155f60(param_8,puStack_110);
    }
    fVar17 = _DAT_1412e1f60;
    fVar16 = _DAT_1412ca628;
    fVar2 = _DAT_1412a0cc4;
    iVar6 = (uVar8 - param_4) + 1;
    iStack_108 = param_4 + -1 + uVar8;
    if (*(int *)(param_1 + 0xf4) <= iStack_108) {
      iStack_108 = *(int *)(param_1 + 0xf4);
    }
    iStack_104 = (uStack_114 - param_4) + 1;
    if (iStack_104 < 1) {
      iStack_104 = 0;
    }
    iVar15 = (uStack_114 - 1) + param_4;
    if (*(int *)(param_1 + 0xf8) <= iVar15) {
      iVar15 = *(int *)(param_1 + 0xf8);
    }
    uStack_b8 = 0;
    puStack_c0 = auStack_a8;
    uStack_b0 = 0x8000000000000004;
    if (iVar6 < 1) {
      iVar6 = 0;
    }
    if (iVar6 <= iStack_108) {
      fStack_118 = (float)(iVar6 - uVar8);
      iVar9 = iStack_108;
      uVar7 = uStack_114;
      iVar11 = iStack_104;
      do {
        iVar5 = iVar11;
        if (iVar11 <= iVar15) {
          fVar19 = (float)(int)fStack_118;
          iVar12 = iVar11 - uVar7;
          do {
            pauVar10 = (undefined1 (*) [16])
                       ((longlong)((*(int *)(param_1 + 0xf4) + 1) * iVar11 + iVar6) * 0x1c +
                       *(longlong *)(param_1 + 0x188));
            auVar1 = *pauVar10;
            uStack_e8 = *(undefined8 *)pauVar10[1];
            uStack_e0 = *(undefined4 *)(pauVar10[1] + 8);
            fVar18 = (float)iVar12 * (float)iVar12 + fVar19 * fVar19;
            if (1 < param_4) {
              fVar18 = fVar18 / (float)((param_4 + -1) * (param_4 + -1));
            }
            if ((param_5 != 0) || (auStack_f8 = auVar1, fVar18 <= fVar2)) {
              auStack_f8._14_2_ = auVar1._14_2_;
              auStack_f8._0_12_ = auVar1._0_12_;
              auStack_f8._12_2_ = auVar1._12_2_ | 0x10;
              uVar7 = auVar1._4_4_ & 0x3ffff;
              auStack_f8._0_4_ = auVar1._0_4_;
              auStack_f8._4_4_ =
                   uVar7 | (int)(longlong)((*(float *)(param_1 + 0x950) + fVar16) * fVar17) << 0x12;
              uStack_138 = CONCAT22(uStack_138._2_2_,0x8000);
              FUN_1403be230(param_1,uVar7,auStack_f8,puStack_110);
              FUN_140263b00(param_1,auStack_f8._4_4_ & 0x3ffff,&puStack_c0,0);
            }
            iVar11 = iVar11 + 1;
            iVar12 = iVar12 + 1;
            iVar9 = iStack_108;
            uVar7 = uStack_114;
            iVar5 = iStack_104;
          } while (iVar11 <= iVar15);
        }
        iVar11 = iVar5;
        iVar6 = iVar6 + 1;
        fStack_118 = (float)((int)fStack_118 + 1);
      } while (iVar6 <= iVar9);
    }
    FUN_140315af0(param_1,0);
    uVar4 = uStack_d0;
    lVar3 = lStack_d8;
    *(undefined4 *)(param_1 + 0xb94) = *(undefined4 *)(param_1 + 0x94c);
    lStack_120 = lStack_d8;
    uStack_128 = uStack_128 & 0xffffffffffffff00;
    uStack_130 = uStack_d0;
    uStack_138 = uStack_138 & 0xffffff00;
    FUN_140276110(param_1,2,param_4 + 2,param_5);
    *(undefined4 *)(param_1 + 0xb98) = *(undefined4 *)(param_1 + 0x948);
    lStack_120 = lVar3;
    uStack_128 = uVar4;
    uStack_130 = uStack_130 & 0xffffffffffffff00;
    uStack_138 = param_5;
    FUN_14026a480(param_1,2,uStack_100,param_4);
    if ((uStack_b0 & 0x8000000000000000) == 0) {
      FUN_141080be0(puStack_c0,0x1417f2a18,0xfffffffa);
    }
  }
  FUN_14115d620(uStack_88 ^ (ulonglong)auStack_158);
  return;
}

 (GhidraScript)  
