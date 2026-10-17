
/* WARNING: Globals starting with '_' overlap smaller symbols at the same address */

void FUN_14026a480(longlong param_1,int param_2,undefined4 param_3,int param_4,int param_5,
                  char param_6,undefined8 param_7,longlong param_8)

{
  undefined1 auVar1 [16];
  float fVar2;
  float fVar3;
  float fVar4;
  byte bVar5;
  undefined1 auVar6 [16];
  char cVar7;
  uint uVar8;
  int iVar9;
  ulonglong uVar10;
  undefined8 *puVar11;
  int iVar12;
  int iVar13;
  ulonglong uVar14;
  ulonglong uVar15;
  int iVar16;
  longlong lVar17;
  ulonglong uVar18;
  int iVar19;
  uint uVar20;
  uint uVar22;
  uint uVar23;
  uint *puVar24;
  undefined1 (*pauVar25) [16];
  float fVar26;
  float fVar27;
  float fVar28;
  undefined1 auStack_1f8 [32];
  undefined8 *puStack_1d8;
  byte bStack_1c8;
  int iStack_1c4;
  int iStack_1c0;
  int iStack_1bc;
  uint uStack_1b8;
  int iStack_1b0;
  int iStack_1ac;
  int iStack_1a8;
  int iStack_1a4;
  uint uStack_1a0;
  int iStack_19c;
  int iStack_198;
  int iStack_194;
  float fStack_190;
  longlong lStack_188;
  uint auStack_180 [2];
  uint *puStack_178;
  undefined4 uStack_170;
  undefined1 auStack_168 [16];
  undefined8 uStack_158;
  undefined4 uStack_150;
  undefined1 auStack_148 [9];
  byte bStack_13f;
  byte bStack_13e;
  undefined5 uStack_13d;
  undefined8 uStack_138;
  undefined4 uStack_130;
  undefined8 uStack_128;
  undefined1 *puStack_120;
  ulonglong uStack_118;
  ulonglong uStack_110;
  undefined1 auStack_108 [32];
  undefined8 auStack_e8 [10];
  ulonglong uStack_98;
  undefined8 *puVar21;
  
  uStack_128 = 0xfffffffffffffffe;
  uStack_98 = _DAT_1417e9788 ^ (ulonglong)auStack_1f8;
  lStack_188 = param_8;
  iStack_194 = param_4;
  uVar8 = FUN_14026c340(param_1,param_3,1);
  uStack_1b8 = uVar8;
  if ((uVar8 != 0xffffffff) && (param_2 < 5)) {
    uVar10 = func_0x000140263fb0(param_1);
    lVar17 = lStack_188;
    iVar13 = *(int *)(param_1 + 0xf4);
    uVar23 = iVar13 + 1;
    iStack_198 = (int)((uVar10 & 0xffffffff) % (ulonglong)uVar23);
    iVar12 = (int)((uVar10 & 0xffffffff) / (ulonglong)uVar23);
    iVar19 = (iStack_198 - param_4) + 1;
    puVar11 = (undefined8 *)0x0;
    iStack_1ac = 0;
    if (0 < iVar19) {
      iStack_1ac = iVar19;
    }
    iVar19 = (iVar12 - param_4) + 1;
    iStack_1b0 = 0;
    if (0 < iVar19) {
      iStack_1b0 = iVar19;
    }
    iStack_1a4 = param_4 + -1 + iStack_198;
    if (iVar13 <= iStack_1a4) {
      iStack_1a4 = iVar13;
    }
    iStack_1a4 = iStack_1a4 + 1;
    iStack_1a8 = param_4 + -1 + iVar12;
    if (*(int *)(param_1 + 0xf8) <= iStack_1a8) {
      iStack_1a8 = *(int *)(param_1 + 0xf8);
    }
    iStack_1a8 = iStack_1a8 + 1;
    iStack_1bc = iVar12;
    if (param_6 != '\0') {
      if (param_2 == 0) {
        puVar21 = (undefined8 *)0xfffffffe;
      }
      else if (param_2 == 1) {
        puVar21 = (undefined8 *)0xffffffff;
      }
      else {
        puVar21 = puVar11;
        if (param_2 != 2) {
          if (param_2 == 3) {
            puVar21 = (undefined8 *)0x1;
          }
          else if (param_2 == 4) {
            puVar21 = (undefined8 *)0x2;
          }
        }
      }
      uVar20 = (uint)puVar21;
      if (2 < (uVar20 ^ (int)uVar20 >> 0x1f) - ((int)uVar20 >> 0x1f)) {
        func_0x000141084490(0x57);
        goto LAB_14026ac0b;
      }
      *(uint *)(param_1 + 0xb98) =
           (*(byte *)((longlong)(int)(iStack_198 + uVar23 * iVar12) * 0x1c + 10 +
                     *(longlong *)(param_1 + 0x188)) & 0xf) + uVar20;
    }
    uStack_1a0 = *(uint *)(param_1 + 0xb98);
    if ((-1 < (int)uStack_1a0) && (iVar13 = *(int *)(param_1 + 0xbd4), (int)uStack_1a0 <= iVar13)) {
      if (lStack_188 != 0) {
        puVar11 = (undefined8 *)FUN_141080b70(0x20,&UNK_14129ed38,0);
        *puVar11 = &UNK_1412e2c68;
        *(undefined4 *)(puVar11 + 1) = 0;
        puVar11[2] = 0;
        *(undefined4 *)(puVar11 + 3) = 0;
        FUN_140155f60(lVar17,puVar11);
        iVar13 = *(int *)(param_1 + 0xbd4);
      }
      uStack_118 = 0;
      puStack_120 = auStack_108;
      uStack_110 = 0x8000000000000004;
      auStack_180[0] = 0;
      puStack_178 = (uint *)0x0;
      uStack_170 = 0;
      FUN_1402704e0(auStack_180,iVar13 + 1);
      FUN_141163cf0(puStack_178,0,(ulonglong)auStack_180[0] << 2);
      fVar4 = _DAT_1412e2d40;
      fVar3 = _DAT_1412b3e14;
      fVar2 = _DAT_1412a0cc4;
      iStack_1c4 = iStack_1ac;
      iVar13 = iStack_1ac;
      iVar19 = iStack_1a4;
      if (iStack_1ac < iStack_1a4) {
        do {
          iStack_1c0 = iStack_1b0;
          if (iStack_1b0 < iStack_1a8) {
            iVar9 = iVar13 - iStack_198;
            iVar12 = -iVar12;
            iVar16 = iStack_1b0;
            iStack_1c4 = iVar13;
            iStack_19c = iVar12;
            do {
              uVar10 = 0;
              pauVar25 = (undefined1 (*) [16])
                         ((longlong)((*(int *)(param_1 + 0xf4) + 1) * iVar16 + iVar13) * 0x1c +
                         *(longlong *)(param_1 + 0x188));
              auStack_168 = *pauVar25;
              uStack_158 = *(undefined8 *)pauVar25[1];
              uStack_150 = *(undefined4 *)(pauVar25[1] + 8);
              fVar27 = (float)(iVar12 + iVar16) * (float)(iVar12 + iVar16) +
                       (float)iVar9 * (float)iVar9;
              if (1 < iStack_194) {
                fVar27 = fVar27 / (float)((iStack_194 + -1) * (iStack_194 + -1));
              }
              if ((param_5 != 0) || (fVar27 <= fVar2)) {
                uVar23 = 0;
                uVar8 = *(uint *)(param_1 + 0xbd4);
                iStack_1c0 = iVar16;
                cVar7 = FUN_14013caa0(0x9a,0);
                if (cVar7 != '\0') {
                  lVar17 = (longlong)((*(int *)(param_1 + 0xf4) + 1) * iVar16 + iStack_1c4) * 0x1c +
                           *(longlong *)(param_1 + 0x188);
                  uVar15 = uVar10;
                  if ((*(byte *)(lVar17 + 0xc) & 0x10) != 0) {
                    fStack_190 = (float)((*(uint *)(lVar17 + 4) >> 0x12 | 0x22c000) << 9);
                    fVar28 = fStack_190 - fVar3;
                    fVar27 = *(float *)(*(longlong *)(param_1 + 0x4d8) + 0x108);
                    fVar26 = (float)FUN_1403bde90(param_1,lVar17,7);
                    uVar23 = (*(byte *)(lVar17 + 10) & 0xf) -
                             (2 - (int)((fVar26 - (fVar28 + fVar27)) * fVar4));
                    uVar15 = 0;
                    if (-1 < (int)uVar23) {
                      uVar15 = (ulonglong)uVar23;
                    }
                  }
                  uVar23 = (uint)uVar15;
                  iVar16 = iStack_1c0;
                }
                if ((int)uStack_1a0 < (int)uVar8) {
                  uVar8 = uStack_1a0;
                }
                uStack_1a0 = uVar23;
                if ((int)uVar23 < (int)uVar8) {
                  uStack_1a0 = uVar8;
                }
                if (((auStack_168[10] & 0xf) != uStack_1a0) ||
                   (iVar13 = iStack_1c4, iVar12 = iStack_19c, auStack_168[10] >> 4 != uStack_1b8)) {
                  puStack_178[auStack_168[10] & 0xf] = puStack_178[auStack_168[10] & 0xf] + 1;
                  bStack_1c8 = (char)uStack_1b8 << 4;
                  auStack_168[10] = (byte)uStack_1a0 & 0xf | bStack_1c8;
                  cVar7 = FUN_14014e6a0(0,3);
                  auVar1 = auStack_168;
                  auStack_168[9] = auStack_168[9] & 0x1f | cVar7 << 5;
                  auVar6 = auStack_168;
                  auStack_168._4_4_ = auVar1._4_4_;
                  uVar8 = auStack_168._4_4_ & 0x3ffff;
                  puStack_1d8 = (undefined8 *)CONCAT62(puStack_1d8._2_6_,0x8000);
                  auStack_168 = auVar6;
                  FUN_1403be230(param_1,uVar8,auStack_168,puVar11);
                  (*pauVar25)[0xb] = 1;
                  FUN_1403be070(param_1,iStack_1c4,iVar16,auStack_e8);
                  uVar15 = uVar10;
                  do {
                    bVar5 = bStack_1c8;
                    uVar14 = 0;
                    uVar18 = uVar14;
                    do {
                      if ((((int)uVar10 != 1) || ((int)uVar14 != 1)) &&
                         (pauVar25 = (undefined1 (*) [16])auStack_e8[uVar15 + uVar18],
                         pauVar25 != (undefined1 (*) [16])0x0)) {
                        auVar1 = *pauVar25;
                        uStack_138 = *(undefined8 *)pauVar25[1];
                        uStack_130 = *(undefined4 *)(pauVar25[1] + 8);
                        uStack_13d = auVar1._11_5_;
                        _auStack_148 = auVar1._0_10_;
                        bStack_13e = auVar1[10] & 0xf | bVar5;
                        cVar7 = FUN_14014e6a0(0,3);
                        auVar1 = _auStack_148;
                        bStack_13f = bStack_13f & 0x1f | cVar7 << 5;
                        auVar6 = _auStack_148;
                        auStack_148._4_4_ = auVar1._4_4_;
                        uVar8 = auStack_148._4_4_ & 0x3ffff;
                        puStack_1d8 = (undefined8 *)CONCAT62(puStack_1d8._2_6_,0x8000);
                        _auStack_148 = auVar6;
                        FUN_1403be230(param_1,uVar8,auStack_148,puVar11);
                      }
                      uVar8 = (int)uVar14 + 1;
                      uVar14 = (ulonglong)uVar8;
                      uVar18 = uVar18 + 1;
                    } while ((int)uVar8 < 3);
                    uVar8 = (int)uVar10 + 1;
                    uVar10 = (ulonglong)uVar8;
                    uVar15 = uVar15 + 3;
                  } while ((int)uVar8 < 3);
                  FUN_140263b00(param_1,auStack_168._4_4_ & 0x3ffff,&puStack_120,0);
                  iVar13 = iStack_1c4;
                  iVar16 = iStack_1c0;
                  iVar12 = iStack_19c;
                }
              }
              iVar16 = iVar16 + 1;
              iStack_1c0 = iVar16;
              iVar19 = iStack_1a4;
            } while (iVar16 < iStack_1a8);
          }
          iStack_1c4 = iVar13 + 1;
          iVar12 = iStack_1bc;
          iVar13 = iStack_1c4;
          uVar8 = uStack_1b8;
        } while (iStack_1c4 < iVar19);
      }
      uVar14 = 0;
      iVar13 = *(int *)(param_1 + 0xbd4) + 1;
      uVar10 = uVar14;
      uVar15 = uVar14;
      uVar18 = uVar14;
      puVar24 = puStack_178;
      if (0 < iVar13) {
        do {
          uVar20 = (uint)uVar15;
          uVar23 = *puVar24;
          if ((int)*puVar24 <= (int)(uint)uVar18) {
            uVar20 = (uint)uVar10;
            uVar23 = (uint)uVar18;
          }
          uVar22 = (uint)uVar15 + 1;
          uVar10 = (ulonglong)uVar20;
          uVar15 = (ulonglong)uVar22;
          uVar18 = (ulonglong)uVar23;
          puVar24 = puVar24 + 1;
        } while ((int)uVar22 < iVar13);
        if ((0 < (int)uVar23) && (iVar13 = iStack_1ac, iStack_1ac < iVar19)) {
          do {
            iVar12 = iStack_1b0;
            if (iStack_1b0 < iStack_1a8) {
              do {
                puStack_1d8 = puVar11;
                FUN_140269520(param_1,*(uint *)((longlong)
                                                ((*(int *)(param_1 + 0xf4) + 1) * iVar12 + iVar13) *
                                                0x1c + 4 + *(longlong *)(param_1 + 0x188)) & 0x3ffff
                              ,uVar8,uVar20);
                iVar12 = iVar12 + 1;
                iVar19 = iStack_1a4;
              } while (iVar12 < iStack_1a8);
            }
            iVar13 = iVar13 + 1;
          } while (iVar13 < iVar19);
        }
      }
      uVar10 = uVar14;
      if (uStack_118 != 0) {
        do {
          FUN_140268d70(param_1,*(undefined8 *)(puStack_120 + uVar10 * 8),uVar8,puVar11);
          uVar10 = (ulonglong)((int)uVar10 + 1);
        } while (uVar10 < uStack_118);
      }
      func_0x0001403be5a0(param_1);
      FUN_14027e650(param_1,&puStack_120,puVar11);
      lVar17 = lStack_188;
      FUN_14030eed0(param_1,lStack_188);
      FUN_140315320(param_1,&puStack_120,uVar8,puVar11);
      FUN_14030b870(param_1,&puStack_120,lVar17);
      FUN_140277030(param_1,&puStack_120,puVar11);
      func_0x0001403be1c0(param_1,&iStack_1b0);
      if (uStack_118 != 0) {
        do {
          FUN_14026c810(param_1,*(undefined8 *)(puStack_120 + uVar14 * 8),puVar11);
          uVar14 = (ulonglong)((int)uVar14 + 1);
        } while (uVar14 < uStack_118);
      }
      func_0x0001403be5a0(param_1);
      FUN_140271390(param_1,&puStack_120);
      FUN_1402dd8b0(param_1,&puStack_120,lVar17);
      func_0x0001402d9e70(param_1);
      func_0x0001402d7b40(param_1);
      if (puStack_178 != (uint *)0x0) {
        FUN_141080be0(puStack_178,&UNK_14129ed38,0xfffffffe);
      }
      if ((uStack_110 & 0x8000000000000000) == 0) {
        FUN_141080be0(puStack_120,0x1417f2a18,0xfffffffa);
      }
    }
  }
LAB_14026ac0b:
  FUN_14115d620(uStack_98 ^ (ulonglong)auStack_1f8);
  return;
}

 (GhidraScript)  
