
void FUN_14026c5c0(longlong param_1,int param_2,int param_3,undefined8 param_4,int param_5)

{
  undefined1 auVar1 [16];
  uint uVar2;
  char cVar3;
  longlong lVar4;
  uint uVar5;
  undefined1 (*pauVar6) [16];
  uint uVar7;
  byte bVar8;
  uint uVar9;
  uint uVar10;
  undefined1 auStack_70 [16];
  undefined8 uStack_60;
  undefined4 uStack_58;
  
  lVar4 = (longlong)((*(int *)(param_1 + 0xf4) + 1) * param_3 + param_2) * 0x1c +
          *(longlong *)(param_1 + 0x188);
  if (*(char *)(lVar4 + 0xb) != '\0') {
    bVar8 = *(byte *)(lVar4 + 10) >> 4;
    uStack_60 = 0;
    uStack_58 = 0;
    for (uVar5 = param_2 - 1; (int)uVar5 <= param_2; uVar5 = uVar5 + 1) {
      uVar9 = param_3 - 1;
      uVar10 = uVar9;
      for (; (int)uVar9 <= param_3; uVar9 = uVar9 + 1) {
        uVar10 = uVar10 + 1;
        if (((uVar5 < *(uint *)(param_1 + 0xf4)) && (uVar9 < *(uint *)(param_1 + 0xf8))) &&
           (cVar3 = FUN_14026e050(param_1,uVar5,uVar9), cVar3 != '\0')) {
          for (uVar7 = uVar5; uVar2 = uVar9, (int)uVar7 <= (int)(uVar5 + 1); uVar7 = uVar7 + 1) {
            for (; (int)uVar2 <= (int)uVar10; uVar2 = uVar2 + 1) {
              pauVar6 = (undefined1 (*) [16])
                        ((longlong)(int)((*(int *)(param_1 + 0xf4) + 1) * uVar2 + uVar7) * 0x1c +
                        *(longlong *)(param_1 + 0x188));
              if ((((*pauVar6)[0xb] == 0) || (param_5 < (int)(uint)(byte)(*pauVar6)[0xb])) &&
                 ((byte)(*pauVar6)[10] >> 4 != bVar8)) {
                auVar1 = *pauVar6;
                uStack_60 = *(undefined8 *)pauVar6[1];
                uStack_58 = *(undefined4 *)(pauVar6[1] + 8);
                auStack_70._11_5_ = auVar1._11_5_;
                auStack_70._0_10_ = auVar1._0_10_;
                auStack_70[10] = auVar1[10] & 0xf | bVar8 << 4;
                FUN_1403be230(param_1,*(uint *)(*pauVar6 + 4) & 0x3ffff,auStack_70,param_4,0x8000);
                (*pauVar6)[0xb] = (char)param_5 + '\x01';
                FUN_14026c5c0(param_1,uVar7,uVar2,param_4,param_5 + 1);
              }
            }
          }
        }
      }
    }
  }
  return;
}

 (GhidraScript)  
