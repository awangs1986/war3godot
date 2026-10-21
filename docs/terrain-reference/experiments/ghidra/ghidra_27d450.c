
/* WARNING: Globals starting with '_' overlap smaller symbols at the same address */

void FUN_14027d450(longlong param_1,byte param_2,float *param_3,longlong param_4,int param_5,
                  int param_6)

{
  int iVar1;
  undefined1 auVar2 [16];
  undefined1 (*pauVar3) [16];
  undefined4 *puVar4;
  ulonglong uVar5;
  code *pcVar6;
  bool bVar7;
  bool bVar8;
  bool bVar9;
  undefined8 uVar10;
  char cVar11;
  char cVar12;
  byte bVar13;
  byte bVar14;
  int iVar15;
  ulonglong uVar16;
  undefined8 *puVar17;
  uint uVar18;
  int iVar19;
  int iVar20;
  uint uVar21;
  int iVar22;
  uint uVar23;
  undefined8 *puVar24;
  longlong *plVar25;
  int iVar26;
  longlong lVar27;
  int iVar28;
  int iVar29;
  uint uVar30;
  longlong lVar31;
  int iVar32;
  undefined8 *puVar33;
  longlong lVar34;
  float fVar35;
  undefined1 auStack_198 [32];
  uint *puStack_178;
  uint *puStack_170;
  byte bStack_168;
  uint uStack_164;
  uint uStack_160;
  uint uStack_15c;
  undefined1 auStack_158 [10];
  undefined1 auStack_14e [4];
  undefined2 uStack_14a;
  undefined8 uStack_148;
  undefined4 uStack_140;
  int iStack_138;
  uint uStack_134;
  uint uStack_130;
  uint uStack_12c;
  uint uStack_128;
  uint uStack_124;
  longlong lStack_120;
  float fStack_118;
  float fStack_114;
  float fStack_110;
  undefined4 uStack_108;
  undefined4 uStack_104;
  undefined4 uStack_100;
  ushort uStack_fc;
  undefined2 uStack_fa;
  undefined8 uStack_f8;
  undefined4 uStack_f0;
  undefined8 uStack_e8;
  undefined1 *puStack_e0;
  undefined8 *puStack_d8;
  ulonglong uStack_d0;
  undefined1 auStack_c8 [32];
  longlong alStack_a8 [10];
  longlong alStack_58 [2];
  ulonglong uStack_48;
  
  plVar25 = alStack_a8 + 2;
  uStack_e8 = 0xfffffffffffffffe;
  uStack_48 = _DAT_1417e9788 ^ (ulonglong)auStack_198;
  fStack_110 = param_3[2];
  fStack_114 = param_3[1] - _DAT_1412b3e08;
  fStack_118 = *param_3 - _DAT_1412b3e08;
  bStack_168 = param_2;
  lStack_120 = param_4;
  alStack_58[0] = param_1;
  uVar16 = func_0x000140263fb0();
  uVar18 = *(int *)(param_1 + 0xf4) + 1;
  uVar5 = (uVar16 & 0xffffffff) / (ulonglong)uVar18;
  uVar16 = (uVar16 & 0xffffffff) % (ulonglong)uVar18;
  iVar22 = (int)uVar16;
  iVar26 = (int)uVar5;
  lVar34 = *(longlong *)(param_1 + 0x188);
  cVar11 = FUN_14026e050(param_1,uVar16,uVar5);
  cVar12 = FUN_14027f170(param_1,uVar16,uVar5);
  lVar27 = lStack_120;
  if ((cVar11 != '\0') || (cVar12 != '\0')) {
    uVar18 = *(uint *)((longlong)(int)(uVar18 * iVar26 + iVar22) * 0x1c + lVar34);
    fVar35 = (*param_3 - ((float)((uVar18 >> 0x17 | 0x11e00) << 0xe) - _DAT_1412e1f70)) *
             _DAT_1412b83c8;
    iVar29 = 0;
    bVar7 = fVar35 < _DAT_1412a0cbc;
    uStack_134 = (uint)bVar7;
    bVar8 = _DAT_1412a0cbc <= fVar35;
    uStack_124 = (uint)bVar8;
    bVar9 = (param_3[1] - ((float)(uVar18 & 0x7fc000 | 0x47800000) - _DAT_1412e1f70)) *
            _DAT_1412b83c8 < _DAT_1412a0cbc;
    uStack_12c = (uint)bVar9;
    uStack_15c = 1 - uStack_12c;
    iStack_138 = *(int *)(param_1 + 0xf4);
    iVar1 = iStack_138 + 1;
    iVar19 = (uStack_15c + iVar26) * iVar1;
    lVar34 = *(longlong *)(param_1 + 0x188);
    iVar20 = ((uint)bVar9 + iVar26) * iVar1;
    uStack_160 = *(byte *)((longlong)(int)((uint)bVar7 + iVar19 + iVar22) * 0x1c + 10 + lVar34) &
                 0xf;
    uVar18 = *(byte *)((longlong)(int)((uint)bVar8 + iVar19 + iVar22) * 0x1c + 10 + lVar34) & 0xf;
    uStack_164 = uStack_160 - uVar18;
    uStack_130 = *(byte *)((longlong)(int)(iVar20 + (uint)bVar8 + iVar22) * 0x1c + 10 + lVar34) &
                 0xf;
    uStack_128 = uStack_130 - uVar18;
    iVar32 = (uStack_128 ^ (int)uStack_128 >> 0x1f) - ((int)uStack_128 >> 0x1f);
    iVar15 = (uStack_164 ^ (int)uStack_164 >> 0x1f) - ((int)uStack_164 >> 0x1f);
    iVar19 = iVar15 + iVar32;
    iVar28 = -(uint)(iVar19 != 1);
    if ((iVar15 == 1) && (iVar32 == 1)) {
      if ((*(byte *)((longlong)(int)(iVar20 + (uint)bVar7 + iVar22) * 0x1c + 10 + lVar34) & 0xf) ==
          uVar18) goto LAB_14027dbf0;
      iVar28 = 1;
    }
    else if (iVar19 != 1) goto LAB_14027dbf0;
    uVar30 = uVar18 - uStack_160;
    if (uStack_134 != 1) {
      uVar30 = uStack_164;
    }
    uVar18 = uVar18 - uStack_130;
    if (uStack_12c != 1) {
      uVar18 = uStack_128;
    }
    if (uVar30 == 0xffffffff) {
      uVar21 = 1;
    }
    else {
      uVar21 = 0;
      if (uVar30 == 0) {
        uVar21 = uStack_124;
      }
    }
    if (uVar18 == 0xffffffff) {
      uVar23 = 1;
    }
    else {
      uVar23 = 0;
      if (uVar18 == 0) {
        uVar23 = uStack_15c;
      }
    }
    if (iVar28 == 0) {
      alStack_a8[0] = (longlong)(int)((uVar23 + iVar26) * iVar1 + uVar21 + iVar22) * 0x1c + lVar34;
      alStack_a8[1] =
           (longlong)(int)((uVar23 + uVar18 + iVar26) * iVar1 + uVar21 + uVar30 + iVar22) * 0x1c +
           lVar34;
      alStack_a8[2] =
           (longlong)(int)((uVar23 + uVar18 * 2 + iVar26) * iVar1 + uVar21 + uVar30 * 2 + iVar22) *
           0x1c + lVar34;
      lVar34 = 3;
    }
    else {
      if (iVar28 != 1) goto LAB_14027dbf0;
      lVar31 = 3;
      do {
        iVar1 = iStack_138 + 1;
        plVar25[-2] = (longlong)(int)(iVar1 * (uVar23 + iVar26) + iVar29 + uVar21 + iVar22) * 0x1c +
                      lVar34;
        plVar25[-1] = (longlong)(int)(iVar1 * (uVar23 + uVar18 + iVar26) + iVar29 + uVar21 + iVar22)
                      * 0x1c + lVar34;
        *plVar25 = (longlong)
                   (int)(iVar29 + iVar1 * (uVar23 + uVar18 * 2 + iVar26) + uVar21 + iVar22) * 0x1c +
                   lVar34;
        iVar29 = iVar29 + uVar30;
        plVar25 = plVar25 + 3;
        lVar31 = lVar31 + -1;
      } while (lVar31 != 0);
      lVar34 = 9;
      param_1 = alStack_58[0];
      param_2 = bStack_168;
    }
    puVar33 = (undefined8 *)0x0;
    uVar18 = *(byte *)(alStack_a8[0] + 10) & 0xf;
    lVar31 = 1;
    uVar30 = uVar18;
    do {
      uVar21 = *(byte *)(alStack_a8[lVar31] + 10) & 0xf;
      if (uVar21 < uVar18) {
        uVar18 = uVar21;
      }
      if (uVar30 < uVar21) {
        uVar30 = uVar21;
      }
      lVar31 = lVar31 + 1;
    } while (lVar31 < lVar34);
    if ((int)(uVar30 - uVar18) < 2) {
      puVar17 = puVar33;
      if (lStack_120 != 0) {
        puVar17 = (undefined8 *)FUN_141080b70(0x20,&UNK_14129ed38,0);
        *puVar17 = &UNK_1412e2c68;
        *(undefined4 *)(puVar17 + 1) = 0;
        puVar17[2] = 0;
        *(undefined4 *)(puVar17 + 3) = 0;
      }
      puStack_d8 = (undefined8 *)0x0;
      puStack_e0 = auStack_c8;
      uStack_d0 = 0x8000000000000004;
      uStack_148 = 0;
      uStack_140 = 0;
      puVar24 = puVar33;
      do {
        if ((undefined8 *)0x8 < puVar24) {
          FUN_14115de08();
          pcVar6 = (code *)swi(3);
          (*pcVar6)();
          return;
        }
        *(undefined1 *)((longlong)alStack_58 + (longlong)puVar24) = 0;
        pauVar3 = (undefined1 (*) [16])alStack_a8[(longlong)puVar24];
        if (((byte)(*pauVar3)[0xc] >> 1 & 1) != param_2) {
          auVar2 = *pauVar3;
          uStack_148 = *(undefined8 *)pauVar3[1];
          uStack_140 = *(undefined4 *)(pauVar3[1] + 8);
          uStack_14a = auVar2._14_2_;
          _auStack_158 = auVar2._0_12_;
          if (param_2 == 0) {
            auStack_14e._2_2_ = (undefined2)*(undefined4 *)(*pauVar3 + 0xc);
            auStack_14e._2_2_ = auStack_14e._2_2_ & 0xfffd;
          }
          else {
            auStack_14e._2_2_ = auVar2._12_2_ | 2;
          }
          puStack_178 = (uint *)CONCAT62(puStack_178._2_6_,0x8000);
          FUN_1403be230(param_1,*(uint *)(*pauVar3 + 4) & 0x3ffff,auStack_158,puVar17);
          FUN_140263b00(param_1,auStack_158._4_4_ & 0x3ffff,&puStack_e0,0);
          *(undefined1 *)((longlong)alStack_58 + (longlong)puVar24) = 1;
        }
        puVar24 = (undefined8 *)((longlong)puVar24 + 1);
      } while ((longlong)puVar24 < lVar34);
      puVar24 = puVar33;
      if (puStack_d8 != (undefined8 *)0x0) {
        do {
          puStack_170 = &uStack_164;
          puStack_178 = &uStack_15c;
          func_0x0001402640b0(param_1,*(undefined8 *)(puStack_e0 + (longlong)puVar24 * 8),
                              &iStack_138,&uStack_160);
          iVar26 = iStack_138;
          uVar18 = uStack_160;
          uVar30 = uStack_164;
          if (iStack_138 < (int)uStack_160) {
            do {
              uVar21 = uStack_15c;
              if ((int)uStack_15c < (int)uVar30) {
                do {
                  cVar11 = FUN_14027f170(param_1,iVar26,uVar21);
                  if (cVar11 != '\0') {
                    cVar11 = FUN_14027f580(param_1,iVar26,uVar21,0);
                    if (cVar11 == '\0') {
                      cVar11 = FUN_14027f480(param_1,iVar26,uVar21);
                      bVar13 = bStack_168;
                      if (cVar11 == '\0') goto LAB_14027db50;
                    }
                  }
                  uVar21 = uVar21 + 1;
                  uVar18 = uStack_160;
                  uVar30 = uStack_164;
                } while ((int)uVar21 < (int)uStack_164);
              }
              iVar26 = iVar26 + 1;
            } while (iVar26 < (int)uVar18);
          }
          puVar24 = (undefined8 *)(ulonglong)((int)puVar24 + 1);
          lVar27 = lStack_120;
        } while (puVar24 < puStack_d8);
      }
      if (puVar17 != (undefined8 *)0x0) {
        FUN_140155f60(lVar27,puVar17);
      }
      if (param_5 != -0x21524111) {
        if (param_5 == 0x424c4947) {
          do {
            pauVar3 = (undefined1 (*) [16])alStack_a8[(longlong)puVar33];
            uVar5 = (ulonglong)(*(int *)(param_1 + 0xf4) + 1);
            uVar16 = (ulonglong)(*(uint *)(*pauVar3 + 4) & 0x3ffff);
            cVar11 = FUN_14030b120(param_1,uVar16 % uVar5,uVar16 / uVar5);
            if (cVar11 != '\0') {
              auVar2 = *pauVar3;
              uStack_148 = *(undefined8 *)pauVar3[1];
              uStack_140 = *(undefined4 *)(pauVar3[1] + 8);
              uStack_14a = auVar2._14_2_;
              _auStack_158 = auVar2._0_12_;
              auStack_14e._2_2_ = auVar2._12_2_ | 4;
              puStack_178 = (uint *)CONCAT62(puStack_178._2_6_,0x8000);
              FUN_1403be230(param_1,*(uint *)(*pauVar3 + 4) & 0x3ffff,auStack_158,puVar17);
            }
            puVar33 = (undefined8 *)((longlong)puVar33 + 1);
          } while ((longlong)puVar33 < lVar34);
        }
        else if ((param_5 == 0x4e4f5448) || (param_5 == 0x4e4f5452)) {
          if (param_5 == 0x4e4f5448) {
            plVar25 = alStack_a8;
            do {
              pauVar3 = (undefined1 (*) [16])*plVar25;
              auVar2 = *pauVar3;
              uStack_148 = *(undefined8 *)pauVar3[1];
              uStack_140 = *(undefined4 *)(pauVar3[1] + 8);
              uStack_14a = auVar2._14_2_;
              _auStack_158 = auVar2._0_12_;
              auStack_14e._2_2_ = auVar2._12_2_ | 0x40;
              puStack_178 = (uint *)CONCAT62(puStack_178._2_6_,0x8000);
              FUN_1403be230(param_1,*(uint *)(*pauVar3 + 4) & 0x3ffff,auStack_158,puVar17);
              plVar25 = plVar25 + 1;
              lVar34 = lVar34 + -1;
            } while (lVar34 != 0);
          }
          else {
            do {
              pauVar3 = (undefined1 (*) [16])alStack_a8[(longlong)puVar33];
              auVar2 = *pauVar3;
              uStack_148 = *(undefined8 *)pauVar3[1];
              uStack_140 = *(undefined4 *)(pauVar3[1] + 8);
              uStack_14a = auVar2._14_2_;
              _auStack_158 = auVar2._0_12_;
              auStack_14e._2_2_ = auVar2._12_2_ & 0xffbf;
              puStack_178 = (uint *)CONCAT62(puStack_178._2_6_,0x8000);
              FUN_1403be230(param_1,*(uint *)(*pauVar3 + 4) & 0x3ffff,auStack_158,puVar17);
              puVar33 = (undefined8 *)((longlong)puVar33 + 1);
            } while ((longlong)puVar33 < lVar34);
          }
        }
        else {
          bVar13 = FUN_1402871f0(param_1,param_5,1);
          if (param_6 == -1) {
            plVar25 = alStack_a8;
            do {
              puVar33 = (undefined8 *)*plVar25;
              uStack_148 = puVar33[2];
              uStack_140 = *(undefined4 *)(puVar33 + 3);
              stack0xfffffffffffffeb1 = (undefined7)((ulonglong)puVar33[1] >> 8);
              auStack_158[8] = (byte)puVar33[1] & 0xf0 | bVar13 & 0xf;
              auStack_158._0_8_ = *puVar33;
              bVar14 = FUN_140286f40(param_1,(int)(char)bVar13);
              auStack_158[9] = auStack_158[9] ^ (auStack_158[9] ^ bVar14) & 0x1f;
              puStack_178 = (uint *)CONCAT62(puStack_178._2_6_,0x8000);
              FUN_1403be230(param_1,*(uint *)((longlong)puVar33 + 4) & 0x3ffff,auStack_158,puVar17);
              plVar25 = plVar25 + 1;
              lVar34 = lVar34 + -1;
            } while (lVar34 != 0);
          }
          else {
            do {
              puVar24 = (undefined8 *)alStack_a8[(longlong)puVar33];
              uVar10 = puVar24[1];
              uStack_148 = puVar24[2];
              uStack_140 = *(undefined4 *)(puVar24 + 3);
              auStack_158[8] = (byte)uVar10 & 0xf0 | bVar13 & 0xf;
              auStack_158._0_8_ = *puVar24;
              auStack_158[9] = (byte)((ulonglong)uVar10 >> 8);
              _auStack_14e = (undefined6)((ulonglong)uVar10 >> 0x10);
              auStack_158[9] = auStack_158[9] & 0xe0 | (byte)param_6 & 0x1f;
              puStack_178 = (uint *)CONCAT62(puStack_178._2_6_,0x8000);
              FUN_1403be230(param_1,*(uint *)((longlong)puVar24 + 4) & 0x3ffff,auStack_158,puVar17);
              puVar33 = (undefined8 *)((longlong)puVar33 + 1);
            } while ((longlong)puVar33 < lVar34);
          }
        }
      }
      FUN_14027e310(param_1,&puStack_e0,puVar17);
      FUN_14030b870(param_1,&puStack_e0,lStack_120);
      FUN_140271390(param_1,&puStack_e0);
      FUN_1402dd8b0(param_1,&puStack_e0,lStack_120);
      func_0x0001402d9e70(param_1);
      func_0x0001402d7b40(param_1);
      if ((uStack_d0 & 0x8000000000000000) == 0) {
        FUN_141080be0(puStack_e0,0x1417f2a18,0xfffffffa);
      }
    }
  }
  goto LAB_14027dbf0;
LAB_14027db50:
  do {
    if (*(char *)((longlong)alStack_58 + (longlong)puVar33) != '\0') {
      puVar4 = (undefined4 *)alStack_a8[(longlong)puVar33];
      uStack_108 = *puVar4;
      uStack_104 = puVar4[1];
      uStack_100 = puVar4[2];
      uStack_fc = (ushort)puVar4[3];
      uStack_fa = (undefined2)((uint)puVar4[3] >> 0x10);
      uStack_f8 = *(undefined8 *)(puVar4 + 4);
      uStack_f0 = puVar4[6];
      if (bVar13 == 0) {
        uStack_fc = (ushort)((uint6)*(undefined6 *)(puVar4 + 2) >> 0x20) | 2;
      }
      else {
        uStack_fc = uStack_fc & 0xfffd;
      }
      puStack_178 = (uint *)CONCAT62(puStack_178._2_6_,0x8000);
      FUN_1403be230(param_1,puVar4[1] & 0x3ffff,&uStack_108,puVar17);
    }
    puVar33 = (undefined8 *)((longlong)puVar33 + 1);
  } while ((longlong)puVar33 < lVar34);
  if (puVar17 != (undefined8 *)0x0) {
    (**(code **)*puVar17)(puVar17,1);
  }
  if ((uStack_d0 & 0x8000000000000000) == 0) {
    FUN_141080be0(puStack_e0,0x1417f2a18,0xfffffffa);
  }
LAB_14027dbf0:
  FUN_14115d620(uStack_48 ^ (ulonglong)auStack_198);
  return;
}

 (GhidraScript)  
