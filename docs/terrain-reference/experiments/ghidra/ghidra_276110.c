
/* WARNING: Globals starting with '_' overlap smaller symbols at the same address */

void FUN_140276110(longlong param_1,float param_2,int param_3,int param_4,char param_5,
                  undefined8 param_6,char param_7,longlong param_8)

{
  float fVar1;
  longlong lVar2;
  float fVar3;
  float fVar4;
  float fVar5;
  float fVar6;
  float fVar7;
  undefined8 *puVar8;
  uint uVar9;
  int iVar10;
  uint uVar11;
  float *pfVar12;
  int iVar13;
  longlong lVar14;
  ulonglong uVar15;
  int iVar16;
  longlong lVar17;
  int iVar18;
  float *pfVar19;
  ulonglong uVar20;
  longlong lVar21;
  longlong lVar22;
  longlong lVar23;
  longlong lVar24;
  int iVar25;
  int iVar26;
  longlong lVar27;
  float fVar28;
  undefined8 extraout_XMM0_Qb;
  undefined1 auVar29 [16];
  undefined8 extraout_XMM0_Qb_00;
  undefined1 auVar30 [16];
  undefined1 auVar31 [16];
  float fVar32;
  float fVar33;
  float fVar34;
  float fVar35;
  float fVar36;
  undefined1 auStack_208 [32];
  undefined2 uStack_1e8;
  float fStack_1d8;
  int iStack_1d4;
  float fStack_1d0;
  uint uStack_1c8;
  int iStack_1c4;
  uint uStack_1c0;
  uint uStack_1bc;
  float fStack_1b8;
  int iStack_1b4;
  uint uStack_1b0;
  uint uStack_1ac;
  int iStack_1a8;
  uint uStack_1a4;
  int iStack_1a0;
  longlong lStack_198;
  undefined8 uStack_190;
  undefined8 uStack_188;
  undefined8 uStack_180;
  undefined4 uStack_178;
  int iStack_170;
  int iStack_16c;
  longlong lStack_168;
  undefined8 *puStack_160;
  longlong lStack_158;
  longlong lStack_150;
  longlong lStack_148;
  longlong lStack_140;
  uint *puStack_138;
  longlong lStack_130;
  undefined8 uStack_128;
  undefined1 *puStack_120;
  ulonglong uStack_118;
  ulonglong uStack_110;
  undefined1 auStack_108 [32];
  ulonglong uStack_e8;
  
  uStack_128 = 0xfffffffffffffffe;
  uStack_e8 = _DAT_1417e9788 ^ (ulonglong)auStack_208;
  lStack_150 = param_8;
  puStack_160 = (undefined8 *)0x0;
  fStack_1d8 = param_2;
  iStack_1a0 = param_3;
  lStack_140 = param_1;
  if (param_8 != 0) {
    puStack_160 = (undefined8 *)FUN_141080b70(0x20,&UNK_14129ed38,0,0);
    *puStack_160 = &UNK_1412e2c68;
    *(undefined4 *)(puStack_160 + 1) = 0;
    puStack_160[2] = 0;
    *(undefined4 *)(puStack_160 + 3) = 0;
    FUN_140155f60(lStack_150,puStack_160);
  }
  uVar9 = func_0x000140263fb0(param_1);
  fVar4 = _DAT_1412b3e14;
  iVar25 = *(int *)(param_1 + 0xf4);
  uStack_1a4 = uVar9 / (iVar25 + 1U);
  uStack_1ac = uVar9 % (iVar25 + 1U);
  if (param_5 != '\0') {
    *(float *)(param_1 + 0xb94) =
         (float)((*(uint *)((ulonglong)uVar9 * 0x1c + *(longlong *)(param_1 + 0x188)) & 0x3fff |
                 0x22c000) << 9) - _DAT_1412b3e14;
  }
  fVar3 = _DAT_1412a0cc4;
  uStack_1c8 = (uStack_1ac - param_3) + 1;
  uVar20 = (ulonglong)uStack_1c8;
  if ((int)uStack_1c8 < 1) {
    uStack_1c8 = 0;
    uVar20 = 0;
  }
  iVar26 = param_3 + -1 + uStack_1ac;
  iStack_1d4 = iVar25;
  if (iVar26 < iVar25) {
    iStack_1d4 = iVar26;
  }
  uVar9 = (uStack_1a4 - param_3) + 1;
  uVar15 = (ulonglong)uVar9;
  if ((int)uVar9 < 1) {
    uVar15 = 0;
  }
  uStack_1c0 = (uint)uVar15;
  iVar18 = param_3 + -1 + uStack_1a4;
  iVar10 = *(int *)(param_1 + 0xf8);
  iVar16 = iVar18;
  if (iVar10 <= iVar18) {
    iVar16 = iVar10;
  }
  if (iVar25 <= iVar26) {
    iVar26 = iVar25;
  }
  iStack_1a8 = (iVar26 - uStack_1c8) + 1;
  if (iVar10 <= iVar18) {
    iVar18 = iVar10;
  }
  if ((int)uVar9 < 1) {
    uVar9 = 0;
  }
  uStack_1bc = (uint)(param_4 == 0);
  if ((uint)param_2 < 2) {
    uStack_1bc = 1;
  }
  iStack_1c4 = iVar16;
  if (param_2 == 5.60519e-45) {
    if (param_7 == '\0') {
      fVar34 = *(float *)(param_1 + 0xbdc);
      fVar35 = *(float *)(param_1 + 0xbd8);
    }
    else {
      fVar34 = *(float *)(param_1 + 0xbe0);
      fVar35 = *(float *)(param_1 + 0xbe4);
    }
    FUN_140279e40(param_1 + 0x2e0,((iVar18 - uVar9) + 1) * iStack_1a8);
    FUN_141163cf0(*(undefined8 *)(param_1 + 0x2e8),0,(ulonglong)*(uint *)(param_1 + 0x2e0) << 2);
    uVar20 = (ulonglong)(int)uStack_1c8;
    uStack_1b0 = uStack_1c8;
    uVar15 = (ulonglong)uStack_1c0;
    if ((int)uStack_1c8 <= iStack_1d4) {
      lStack_168 = uVar20 - 1;
      uVar9 = uStack_1c8;
      iVar25 = iStack_1d4;
      do {
        iVar26 = (int)uVar15;
        if (iVar26 <= iVar16) {
          iVar10 = uVar9 - uStack_1ac;
          lVar23 = (longlong)iVar26 + -1;
          lVar17 = (longlong)(int)(uVar9 - (int)uVar20) << 2;
          lStack_130 = (longlong)iStack_1a8 << 2;
          iStack_16c = 1 - uStack_1a4;
          iVar25 = iVar26 + -1;
          uStack_1b0 = uVar9;
          do {
            iVar26 = *(int *)(param_1 + 0xf4);
            iStack_1b4 = iVar26 + 1;
            iStack_170 = iVar25 + 1;
            lVar2 = *(longlong *)(param_1 + 0x188);
            puStack_138 = (uint *)((longlong)(int)(iStack_1b4 * iStack_170 + uVar9) * 0x1c + lVar2);
            lStack_198 = lVar23;
            lStack_158 = lVar17;
            auVar29._0_8_ =
                 FUN_141173d48((float)(iVar25 + iStack_16c) * (float)(iVar25 + iStack_16c) +
                               (float)iVar10 * (float)iVar10);
            auVar29._8_8_ = extraout_XMM0_Qb;
            if (1 < iStack_1a0) {
              fVar32 = (float)auVar29._0_8_;
              auVar29._0_4_ = fVar32 / (float)(iStack_1a0 + -1);
            }
            if (((char)uStack_1bc == '\0') || (auVar29._0_4_ <= fVar3)) {
              iVar16 = uVar9 - 1;
              lVar17 = lStack_168;
              if (iVar16 < 1) {
                lVar17 = 0;
              }
              if (iVar16 < 1) {
                iVar16 = 0;
              }
              if (iVar25 < 1) {
                lVar23 = 0;
              }
              fVar32 = 0.0;
              fVar33 = 0.0;
              iVar18 = iVar25;
              if (iVar25 < 1) {
                iVar18 = 0;
              }
              iVar13 = uVar9 + 1;
              if (iVar26 <= (int)(uVar9 + 1)) {
                iVar13 = iVar26;
              }
              lVar27 = (longlong)iVar13;
              iVar26 = iVar25 + 2;
              if (*(int *)(param_1 + 0xf8) <= iVar25 + 2) {
                iVar26 = *(int *)(param_1 + 0xf8);
              }
              lStack_148 = (longlong)iVar26;
              for (lVar24 = lVar23; lVar24 <= lStack_148; lVar24 = lVar24 + 1) {
                if (lVar17 <= lVar27) {
                  lVar21 = lVar17;
                  iVar25 = iVar16;
                  if (3 < (lVar27 - lVar17) + 1) {
                    iVar26 = iStack_1b4 * iVar18 + iVar16;
                    pfVar19 = (float *)(((lVar24 - lVar23) * 3 + 1) * 4 + 0x1417bc528);
                    lVar22 = ((lVar27 - lVar17) - 3U >> 2) + 1;
                    iVar25 = iVar16 + (int)lVar22 * 4;
                    lVar21 = lVar17 + lVar22 * 4;
                    do {
                      lVar14 = (longlong)iVar26;
                      fStack_1d0 = (float)((*(uint *)((lVar14 + 3) * 0x1c + lVar2) & 0x3fff |
                                           0x22c000) << 9);
                      fVar32 = fVar32 + ((float)((*(uint *)(lVar14 * 0x1c + lVar2) & 0x3fff |
                                                 0x22c000) << 9) - fVar4) * pfVar19[-1] +
                               ((float)((*(uint *)(lVar14 * 0x1c + 0x1c + lVar2) & 0x3fff | 0x22c000
                                        ) << 9) - fVar4) * *pfVar19 +
                               ((float)((*(uint *)((lVar14 + 2) * 0x1c + lVar2) & 0x3fff | 0x22c000)
                                       << 9) - fVar4) * pfVar19[1] +
                               (fStack_1d0 - fVar4) * pfVar19[2];
                      fVar33 = fVar33 + pfVar19[-1] + *pfVar19 + pfVar19[1] + pfVar19[2];
                      iVar26 = iVar26 + 4;
                      pfVar19 = pfVar19 + 4;
                      lVar22 = lVar22 + -1;
                    } while (lVar22 != 0);
                  }
                  if (lVar21 <= lVar27) {
                    iVar25 = iStack_1b4 * iVar18 + iVar25;
                    pfVar19 = (float *)((((lVar24 - lVar23) * 3 - lVar17) + lVar21) * 4 +
                                       0x1417bc528);
                    lVar21 = (lVar27 - lVar21) + 1;
                    do {
                      fStack_1d0 = (float)((*(uint *)((longlong)iVar25 * 0x1c + lVar2) & 0x3fff |
                                           0x22c000) << 9);
                      fVar32 = fVar32 + (fStack_1d0 - fVar4) * *pfVar19;
                      fVar33 = fVar33 + *pfVar19;
                      iVar25 = iVar25 + 1;
                      pfVar19 = pfVar19 + 1;
                      lVar21 = lVar21 + -1;
                    } while (lVar21 != 0);
                  }
                }
                iVar18 = iVar18 + 1;
                param_1 = lStack_140;
              }
              fStack_1b8 = (float)((*puStack_138 & 0x3fff | 0x22c000) << 9);
              *(float *)(lStack_158 + *(longlong *)(param_1 + 0x2e8)) =
                   (fVar32 * fVar35) / fVar33 + (fStack_1b8 - fVar4) * fVar34;
              lVar17 = lStack_158;
              lVar23 = lStack_198;
              uVar9 = uStack_1b0;
            }
            lVar17 = lVar17 + lStack_130;
            lVar23 = lVar23 + 1;
            iVar25 = iStack_170;
          } while (iStack_170 + 1 <= iStack_1c4);
          uVar20 = (ulonglong)uStack_1c8;
          uVar15 = (ulonglong)uStack_1c0;
          iVar25 = iStack_1d4;
          lStack_198 = lVar23;
          lStack_158 = lVar17;
          iVar16 = iStack_1c4;
        }
        uVar9 = uVar9 + 1;
        lStack_168 = lStack_168 + 1;
        param_2 = fStack_1d8;
        uStack_1b0 = uVar9;
      } while ((int)uVar9 <= iVar25);
    }
  }
  fVar7 = _DAT_1412e1f60;
  fVar6 = _DAT_1412d02b8;
  fVar5 = _DAT_1412ca628;
  fVar33 = _DAT_1412c9718;
  fVar32 = _DAT_1412b3e08;
  fVar35 = _DAT_1412a8a08;
  fVar34 = _DAT_1412a0cbc;
  uStack_118 = 0;
  uStack_110 = 0x8000000000000004;
  puStack_120 = auStack_108;
  if ((int)uVar20 <= iStack_1d4) {
    lStack_198 = 0;
    iVar25 = iStack_1d4;
    do {
      iVar26 = iStack_1a0;
      uVar9 = uStack_1a4;
      uVar11 = (uint)uVar20;
      if ((int)uVar15 <= iVar16) {
        fVar36 = (float)(int)((uint)uVar20 - uStack_1ac);
        lVar23 = (longlong)iStack_1a8;
        lVar17 = lStack_198;
        do {
          iVar25 = (int)uVar15;
          lVar27 = (longlong)((*(int *)(param_1 + 0xf4) + 1) * iVar25 + (int)uVar20) * 0x1c;
          lVar2 = *(longlong *)(param_1 + 0x188);
          uStack_190 = *(undefined8 *)(lVar27 + lVar2);
          uStack_188 = ((undefined8 *)(lVar27 + lVar2))[1];
          uStack_180 = *(undefined8 *)(lVar27 + 0x10 + lVar2);
          uStack_178 = *(undefined4 *)(lVar27 + 0x18 + lVar2);
          auVar30._0_8_ =
               FUN_141173d48((float)(int)(iVar25 - uVar9) * (float)(int)(iVar25 - uVar9) +
                             fVar36 * fVar36);
          auVar30._8_8_ = extraout_XMM0_Qb_00;
          if (1 < iVar26) {
            fVar28 = (float)auVar30._0_8_;
            auVar30._0_4_ = fVar28 / (float)(iVar26 + -1);
          }
          if (((char)uStack_1bc == '\0') || (auVar30._0_4_ <= fVar3)) {
            if (-1 < (int)param_2) {
              auVar31._4_12_ = auVar30._4_12_;
              if ((int)param_2 < 2) {
                auVar31._0_4_ = auVar30._0_4_ * fVar33 * fVar34;
                fVar28 = (float)FUN_1411721f0(auVar31._0_8_);
                fStack_1d8 = fVar28 * (float)iVar26 * fVar6;
                if (param_2 == 1.4013e-45) {
                  fStack_1d8 = fStack_1d8 * fVar35;
                }
                if (param_7 != '\0') {
                  fStack_1d8 = fStack_1d8 * fVar35;
                }
                fStack_1d8 = ((float)(((uint)uStack_190 & 0x3fff | 0x22c000) << 9) - fVar4) +
                             fStack_1d8;
                pfVar19 = (float *)(param_1 + 0xbc4);
                fVar28 = *(float *)(param_1 + 0xbc8);
                uVar11 = (uint)uStack_190;
                if (fVar28 <= fStack_1d8) {
                  pfVar12 = &fStack_1d8;
                  if (*pfVar19 <= fStack_1d8 && fStack_1d8 != *pfVar19) {
                    pfVar12 = pfVar19;
                  }
LAB_140276a03:
                  fVar28 = *pfVar12;
                  uVar11 = (uint)uStack_190;
                }
              }
              else {
                if (param_2 == 2.8026e-45) {
                  pfVar19 = (float *)(param_1 + 0xbc4);
                  fVar1 = *(float *)(param_1 + 0xb94);
                  fVar28 = *(float *)(param_1 + 0xbc8);
                  if (fVar28 <= fVar1) {
                    pfVar12 = (float *)(param_1 + 0xb94);
                    if (*pfVar19 <= fVar1 && fVar1 != *pfVar19) {
                      pfVar12 = pfVar19;
                    }
                    goto LAB_140276a03;
                  }
                }
                else {
                  if (param_2 != 4.2039e-45) {
                    if (param_2 == 5.60519e-45) {
                      pfVar19 = (float *)(param_1 + 0xbc4);
                      pfVar12 = (float *)(*(longlong *)(param_1 + 0x2e8) + lVar17);
                      fVar1 = *pfVar12;
                      fVar28 = *(float *)(param_1 + 0xbc8);
                      uVar11 = (uint)uStack_190;
                      if (fVar28 <= fVar1) {
                        if (*pfVar19 <= fVar1 && fVar1 != *pfVar19) {
                          pfVar12 = pfVar19;
                        }
                        fVar28 = *pfVar12;
                      }
                      goto LAB_140276a07;
                    }
                    goto LAB_140276a21;
                  }
                  fStack_1d8 = (float)(((uint)uStack_190 & 0x3fff | 0x22c000) << 9);
                  iVar10 = FUN_14014e6a0(0,0x15);
                  fStack_1d8 = (fStack_1d8 - fVar4) + ((float)iVar10 * fVar6 - fVar32);
                  pfVar19 = (float *)(param_1 + 0xbc4);
                  fVar28 = *(float *)(param_1 + 0xbc8);
                  if (fVar28 <= fStack_1d8) {
                    pfVar12 = &fStack_1d8;
                    if (*pfVar19 <= fStack_1d8 && fStack_1d8 != *pfVar19) {
                      pfVar12 = pfVar19;
                    }
                    fVar28 = *pfVar12;
                  }
                }
                uVar11 = (uint)uStack_190;
              }
LAB_140276a07:
              uStack_190 = CONCAT44(uStack_190._4_4_,
                                    uVar11 & 0xffffc000 | (uint)(longlong)((fVar28 + fVar5) * fVar7)
                                   );
            }
LAB_140276a21:
            uStack_1e8 = 3;
            FUN_1403be230(param_1,uStack_190._4_4_ & 0x3ffff,&uStack_190,puStack_160);
            FUN_140263b00(param_1,uStack_190._4_4_ & 0x3ffff,&puStack_120,0);
          }
          uVar15 = (ulonglong)(iVar25 + 1U);
          lVar17 = lVar17 + lVar23 * 4;
          uVar20 = (ulonglong)uStack_1c8;
        } while ((int)(iVar25 + 1U) <= iStack_1c4);
        uVar15 = (ulonglong)uStack_1c0;
        iVar25 = iStack_1d4;
        iVar16 = iStack_1c4;
        uVar11 = uStack_1c8;
      }
      uStack_1c8 = uVar11 + 1;
      uVar20 = (ulonglong)uStack_1c8;
      lStack_198 = lStack_198 + 4;
    } while ((int)uStack_1c8 <= iVar25);
  }
  puVar8 = puStack_160;
  uVar15 = 0;
  uVar20 = uVar15;
  if (uStack_118 != 0) {
    do {
      FUN_140275e10(param_1,*(undefined8 *)(puStack_120 + uVar20 * 8),puVar8);
      uVar20 = (ulonglong)((int)uVar20 + 1);
    } while (uVar20 < uStack_118);
  }
  if (*(int *)(param_1 + 0x180) != 0) {
    do {
      *(undefined1 *)(uVar15 * 0x1c + 0xb + *(longlong *)(param_1 + 0x188)) = 0;
      uVar9 = (int)uVar15 + 1;
      uVar15 = (ulonglong)uVar9;
    } while (uVar9 < *(uint *)(param_1 + 0x180));
  }
  FUN_1402dd8b0(param_1,&puStack_120,lStack_150);
  func_0x0001402d9e70(param_1);
  func_0x0001402d7b40(param_1);
  if ((uStack_110 & 0x8000000000000000) == 0) {
    FUN_141080be0(puStack_120,0x1417f2a18,0xfffffffa);
  }
  FUN_14115d620(uStack_e8 ^ (ulonglong)auStack_208);
  return;
}

 (GhidraScript)  
INFO  /we_decrypted.exe: file created (awang) (LocalFileSystem)  
INFO  REPORT: Save succeeded for: /we_decrypted.exe (rea3:/we_decrypted.exe) (HeadlessAnalyzer)  
INFO  REPORT: Import succeeded (HeadlessAnalyzer)  
