
void FUN_14026c810(longlong param_1,undefined8 param_2,undefined8 param_3)

{
  undefined1 auVar1 [16];
  uint uVar2;
  uint uVar3;
  longlong lVar4;
  int iVar5;
  uint uVar6;
  uint uVar7;
  undefined1 (*pauVar8) [16];
  int iVar9;
  int iVar10;
  byte bVar11;
  uint uVar12;
  int iStack_78;
  int iStack_74;
  int iStack_70;
  int iStack_6c;
  int iStack_68;
  int iStack_64;
  unkbyte10 Stack_60;
  byte bStack_56;
  undefined5 uStack_55;
  undefined8 uStack_50;
  undefined4 uStack_48;
  
  func_0x0001402640b0(param_1,param_2,&iStack_68,&iStack_70,&iStack_64,&iStack_78);
  iVar9 = iStack_70;
  iVar5 = iStack_78;
  if (iStack_68 <= iStack_70) {
    do {
      iVar10 = iStack_64;
      iStack_6c = iStack_68;
      if (iStack_64 <= iVar5) {
        do {
          lVar4 = (longlong)((*(int *)(param_1 + 0xf4) + 1) * iVar10 + iStack_68) * 0x1c +
                  *(longlong *)(param_1 + 0x188);
          if (*(char *)(lVar4 + 0xb) != '\0') {
            uVar12 = iStack_68 - 1;
            bVar11 = *(byte *)(lVar4 + 10) >> 4;
            iStack_74 = iVar10;
            if ((int)uVar12 <= iStack_68) {
              do {
                uVar6 = iVar10 - 1;
                uVar2 = uVar6;
                if ((int)uVar6 <= iVar10) {
                  do {
                    lVar4 = 0;
                    if ((uVar12 < *(uint *)(param_1 + 0xf4)) && (uVar6 < *(uint *)(param_1 + 0xf8)))
                    {
                      iVar9 = *(uint *)(param_1 + 0xf4) + 1;
LAB_14026c983:
                      if ((*(byte *)((longlong)
                                     (int)((*(int *)(&UNK_1412e166c + lVar4) + uVar6) * iVar9 +
                                           *(int *)(&UNK_1412e165c + lVar4) + uVar12) * 0x1c + 10 +
                                    *(longlong *)(param_1 + 0x188)) & 0xf) ==
                          (*(byte *)((longlong)(int)(iVar9 * uVar6 + uVar12) * 0x1c + 10 +
                                    *(longlong *)(param_1 + 0x188)) & 0xf))
                      goto code_r0x00014026c9ae;
                      for (uVar7 = uVar12; uVar3 = uVar6, (int)uVar7 <= (int)(uVar12 + 1);
                          uVar7 = uVar7 + 1) {
                        for (; (int)uVar3 <= (int)(uVar2 + 1); uVar3 = uVar3 + 1) {
                          pauVar8 = (undefined1 (*) [16])
                                    ((longlong)(int)((*(int *)(param_1 + 0xf4) + 1) * uVar3 + uVar7)
                                     * 0x1c + *(longlong *)(param_1 + 0x188));
                          if (((*pauVar8)[0xb] != '\x01') && ((byte)(*pauVar8)[10] >> 4 != bVar11))
                          {
                            auVar1 = *pauVar8;
                            uStack_50 = *(undefined8 *)pauVar8[1];
                            uStack_48 = *(undefined4 *)(pauVar8[1] + 8);
                            uStack_55 = auVar1._11_5_;
                            Stack_60 = auVar1._0_10_;
                            bStack_56 = auVar1[10] & 0xf | bVar11 << 4;
                            FUN_1403be230(param_1,*(uint *)(*pauVar8 + 4) & 0x3ffff,&Stack_60,
                                          param_3,0x8000);
                            (*pauVar8)[0xb] = 2;
                            FUN_14026c5c0(param_1,uVar7,uVar3,param_3,2);
                          }
                        }
                      }
                    }
LAB_14026cabe:
                    uVar6 = uVar6 + 1;
                    iStack_68 = iStack_6c;
                    iVar10 = iStack_74;
                    uVar2 = uVar2 + 1;
                  } while ((int)uVar6 <= iStack_74);
                }
                uVar12 = uVar12 + 1;
                iVar5 = iStack_78;
              } while ((int)uVar12 <= iStack_68);
            }
          }
          iVar10 = iVar10 + 1;
          iVar9 = iStack_70;
        } while (iVar10 <= iVar5);
      }
      iStack_68 = iStack_68 + 1;
    } while (iStack_68 <= iVar9);
  }
  return;
code_r0x00014026c9ae:
  lVar4 = lVar4 + 4;
  if (0xb < lVar4) goto LAB_14026cabe;
  goto LAB_14026c983;
}

 (GhidraScript)  
INFO  /we_decrypted.exe: file created (awang) (LocalFileSystem)  
INFO  REPORT: Save succeeded for: /we_decrypted.exe (rea:/we_decrypted.exe) (HeadlessAnalyzer)  
INFO  REPORT: Import succeeded (HeadlessAnalyzer)  
