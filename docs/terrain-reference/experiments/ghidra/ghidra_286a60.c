
/* WARNING: Globals starting with '_' overlap smaller symbols at the same address */

void FUN_140286a60(longlong param_1,int param_2,int param_3,int param_4,int param_5,
                  undefined8 *param_6,longlong param_7,byte param_8)

{
  undefined1 auVar1 [16];
  float fVar2;
  undefined1 auVar3 [16];
  char cVar4;
  byte bVar5;
  ushort uVar6;
  int iVar7;
  ulonglong uVar8;
  longlong lVar9;
  undefined8 uVar10;
  int *piVar11;
  longlong lVar12;
  int iVar13;
  int iVar14;
  int iVar15;
  int iVar16;
  int iVar17;
  int iVar18;
  undefined8 extraout_XMM0_Qb;
  undefined1 auVar20 [16];
  undefined1 auStack_128 [32];
  undefined2 uStack_108;
  int iStack_f8;
  int iStack_f4;
  int iStack_f0;
  int iStack_ec;
  int iStack_e8;
  undefined8 uStack_e0;
  float fStack_d8;
  undefined1 auStack_d0 [16];
  undefined8 uStack_c0;
  undefined4 uStack_b8;
  int iStack_b0;
  int iStack_a8;
  int iStack_a4;
  longlong lStack_a0;
  undefined8 *puStack_98;
  undefined8 uStack_90;
  undefined1 *puStack_88;
  ulonglong uStack_80;
  ulonglong uStack_78;
  undefined1 auStack_70 [32];
  ulonglong uStack_50;
  float fVar19;
  
  uStack_90 = 0xfffffffffffffffe;
  uStack_50 = _DAT_1417e9788 ^ (ulonglong)auStack_128;
  lStack_a0 = param_7;
  iStack_a4 = param_4;
  if (param_2 != 0) {
    uStack_e0 = *param_6;
    fStack_d8 = *(float *)(param_6 + 1);
    if (param_2 == 0x48544f4e) {
      uStack_e0._4_4_ = (float)((ulonglong)uStack_e0 >> 0x20);
      uStack_e0 = CONCAT44(uStack_e0._4_4_ + _DAT_1412e32e0,(float)uStack_e0 + _DAT_1412e32e0);
      fStack_d8 = fStack_d8 + _DAT_1412e32e0;
    }
    uVar8 = func_0x000140263fb0(param_1);
    iVar14 = *(int *)(param_1 + 0xf4);
    iStack_a8 = (int)((uVar8 & 0xffffffff) / (ulonglong)(iVar14 + 1));
    iVar16 = (int)((uVar8 & 0xffffffff) % (ulonglong)(iVar14 + 1));
    iStack_f4 = (iVar16 - param_4) + 1;
    iVar7 = 0;
    if (iStack_f4 < 1) {
      iStack_f4 = 0;
    }
    iStack_ec = param_4 + -1 + iVar16;
    if (iVar14 <= iStack_ec) {
      iStack_ec = iVar14;
    }
    iStack_e8 = (iStack_a8 - param_4) + 1;
    if (iStack_e8 < 1) {
      iStack_e8 = 0;
    }
    iVar14 = param_4 + -1 + iStack_a8;
    if (*(int *)(param_1 + 0xf8) <= iVar14) {
      iVar14 = *(int *)(param_1 + 0xf8);
    }
    puStack_98 = (undefined8 *)0x0;
    iStack_f0 = iVar14;
    if (lStack_a0 != 0) {
      puStack_98 = (undefined8 *)FUN_141080b70(0x20,&UNK_14129ed38,0,0);
      *puStack_98 = &UNK_1412e2c68;
      *(undefined4 *)(puStack_98 + 1) = 0;
      puStack_98[2] = 0;
      *(undefined4 *)(puStack_98 + 3) = 0;
      FUN_140155f60(lStack_a0,puStack_98);
    }
    if (param_2 == 0x48544f4e) {
      iStack_f8 = -1;
    }
    else {
      iStack_f8 = -1;
      if (0 < *(int *)(param_1 + 0x1b0)) {
        piVar11 = *(int **)(param_1 + 0x1b8);
        do {
          if (*piVar11 == param_2) {
            iVar14 = iStack_f0;
            iStack_f8 = iVar7;
            if (iVar7 != -1) goto LAB_140286c62;
            break;
          }
          iVar7 = iVar7 + 1;
          piVar11 = piVar11 + 10;
        } while (iVar7 < *(int *)(param_1 + 0x1b0));
      }
      lVar9 = FUN_1401cc080(param_2);
      iVar14 = iStack_f0;
      if (lVar9 != 0) {
        uVar10 = func_0x000140562190(auStack_d0);
        cVar4 = FUN_140286340(param_1,lVar9 + 0x34,uVar10);
        iVar14 = iStack_f0;
        if (cVar4 != '\0') {
          *(int *)(*(longlong *)(param_1 + 0x1b8) +
                  (ulonglong)(*(int *)(param_1 + 0x1b0) - 1) * 0x28) = param_2;
          iStack_f8 = *(int *)(param_1 + 0x1b0) + -1;
        }
      }
    }
LAB_140286c62:
    cVar4 = FUN_14041e8d0(2);
    fVar2 = _DAT_1412a0cc4;
    bVar5 = param_8 ^ 1;
    if (cVar4 != '\0') {
      bVar5 = param_8;
    }
    param_8 = bVar5;
    uStack_80 = 0;
    puStack_88 = auStack_70;
    uStack_78 = 0x8000000000000004;
    if (iStack_f4 <= iStack_ec) {
      iStack_b0 = iStack_f4 - iVar16;
      iVar7 = iStack_ec;
      iVar16 = iStack_e8;
      do {
        iVar18 = iStack_f4;
        iVar13 = iStack_b0;
        if (iVar16 <= iVar14) {
          iVar15 = iVar16 - iStack_a8;
          iVar17 = iVar16;
          do {
            lVar12 = (longlong)((*(int *)(param_1 + 0xf4) + 1) * iVar17 + iVar18) * 0x1c;
            lVar9 = *(longlong *)(param_1 + 0x188);
            auVar1 = *(undefined1 (*) [16])(lVar12 + lVar9);
            uStack_c0 = *(undefined8 *)(lVar12 + 0x10 + lVar9);
            uStack_b8 = *(undefined4 *)(lVar12 + 0x18 + lVar9);
            auStack_d0 = auVar1;
            if ((auVar1._8_8_ >> 0x20 & 1) == 0) {
              auVar20._0_8_ =
                   FUN_141173d48((float)iVar13 * (float)iVar13 + (float)iVar15 * (float)iVar15);
              auVar3 = auStack_d0;
              auVar20._8_8_ = extraout_XMM0_Qb;
              if (1 < iStack_a4) {
                fVar19 = (float)auVar20._0_8_;
                auVar20._0_4_ = fVar19 / (float)(iStack_a4 + -1);
              }
              if ((param_5 != 0) || (iVar18 = iStack_f4, iVar13 = iStack_b0, auVar20._0_4_ <= fVar2)
                 ) {
                if (param_2 == 0x48544f4e) {
                  if (param_8 == 0) {
                    uVar6 = 0xffbf;
                    goto LAB_140286dfc;
                  }
                  auStack_d0._12_2_ = auVar1._12_2_ | 0x40;
                }
                else {
                  auStack_d0[8] = auStack_d0[8] ^ ((byte)iStack_f8 ^ auStack_d0[8]) & 0xf;
                  if (param_3 == -1) {
                    if (*(int *)(param_1 + 0xc0) == 0x12) {
                      bVar5 = FUN_140145400((int *)(param_1 + 0xc0),0);
                    }
                    else {
                      func_0x000141084490(0x57);
                      bVar5 = 0;
                    }
                    auStack_d0[9] = auStack_d0[9] ^ (auStack_d0[9] ^ bVar5) & 0x1f;
                  }
                  else {
                    auStack_d0[9] = auVar3[9];
                    auStack_d0._10_6_ = auVar3._10_6_;
                    auStack_d0[9] = auStack_d0[9] ^ ((byte)param_3 ^ auStack_d0[9]) & 0x1f;
                  }
                  uVar6 = 0xfffb;
LAB_140286dfc:
                  auStack_d0._12_2_ = auStack_d0._12_2_ & uVar6;
                }
                uStack_108 = 4;
                FUN_1403be230(param_1,auStack_d0._4_4_ & 0x3ffff,auStack_d0,puStack_98);
                FUN_140263b00(param_1,auStack_d0._4_4_ & 0x3ffff,&puStack_88,0);
                iVar18 = iStack_f4;
                iVar13 = iStack_b0;
              }
            }
            iVar17 = iVar17 + 1;
            iVar15 = iVar15 + 1;
            iVar7 = iStack_ec;
            iVar16 = iStack_e8;
            iVar14 = iStack_f0;
          } while (iVar17 <= iStack_f0);
        }
        iStack_f4 = iVar18 + 1;
        iStack_b0 = iVar13 + 1;
      } while (iStack_f4 <= iVar7);
    }
    uVar8 = 0;
    if (DAT_14183c8e0 != '\0') {
      if (param_2 == 0x48544f4e) {
        if (uStack_80 != 0) {
          do {
            *(ushort *)(*(longlong *)(puStack_88 + uVar8 * 8) + 0x294) =
                 *(ushort *)(*(longlong *)(puStack_88 + uVar8 * 8) + 0x294) | 8;
            uVar8 = (ulonglong)((int)uVar8 + 1);
          } while (uVar8 < uStack_80);
        }
        FUN_140285bc0(param_1,&puStack_88);
      }
      FUN_14030b870(param_1,&puStack_88,lStack_a0);
      FUN_1402dd8b0(param_1,&puStack_88,lStack_a0);
    }
    if ((uStack_78 & 0x8000000000000000) == 0) {
      FUN_141080be0(puStack_88,0x1417f2a18,0xfffffffa);
    }
  }
  FUN_14115d620(uStack_50 ^ (ulonglong)auStack_128);
  return;
}

 (GhidraScript)  
