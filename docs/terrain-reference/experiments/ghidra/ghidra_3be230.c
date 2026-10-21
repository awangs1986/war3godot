/* FUN_1403BE230 —— 唯一角点写入口（§47.1）
 * 切片: f_wr1.exe = 0x1403BE230-0x1403BE600（切自 we_decrypted.exe, sha256 2FF3ABA0…）
 * 工具: Ghidra 12.1.4 / REA procedure_pseudo_code
 * 证据: ev_37fac486570cbcf15f00d38e0f8c920dba24802811f26d009ea4c5b8323d84c9
 * 说明: 参数 (map, cornerIndex, newRec, undoOut, flags)
 */void FUN_1403BE230(longlong param_1,uint param_2,undefined4 *param_3,longlong param_4,ushort param_5)
{
  undefined4 *puVar1;
  undefined1 uVar2;
  undefined4 uVar3, uVar4, uVar5;
  char cVar6;
  undefined8 uVar7;
  ulonglong uVar8;
  ushort uVar9;
  longlong lVar10, lVar11;
  undefined1 auStack_a8 [32];
  undefined8 local_88;
  undefined1 *local_80;
  ulonglong local_78, local_70;
  undefined1 local_68 [32];
  ulonglong local_48;

  local_88 = 0xfffffffffffffffe;
  local_48 = _DAT_1417e9788 ^ (ulonglong)auStack_a8;
  if (param_2 == (param_3[1] & 0x3ffff)) {          /* 索引自校验 */
    lVar10 = (longlong)(int)param_2 * 0x1c;         /* 记录 stride = 0x1c */
    lVar11 = *(longlong *)(param_1 + 0x188);        /* 角点记录基址 */
    cVar6 = FUN_1403be720();
    if (cVar6 == '\0') {
      if (param_4 != 0) {                            /* undo 记录 */
        uVar7 = func_0x000141080b70(0x48,0x14129ed38,0);
        uVar7 = func_0x0001403c19b0(uVar7,param_1,*(longlong *)(param_1 + 0x188) + lVar10,param_3);
        func_0x0001403c1c30(param_4,uVar7);
        lVar11 = *(longlong *)(param_1 + 0x188);
      }
      uVar2 = *(undefined1 *)(lVar10 + 0xb + lVar11); /* 保存 rec[0xb]（扩散深度标记） */
      uVar3 = param_3[1]; uVar4 = param_3[2]; uVar5 = param_3[3];
      puVar1 = (undefined4 *)(lVar10 + lVar11);
      *puVar1 = *param_3; puVar1[1] = uVar3; puVar1[2] = uVar4; puVar1[3] = uVar5;
      *(undefined8 *)(lVar10 + 0x10 + lVar11) = *(undefined8 *)(param_3 + 4);
      *(undefined4 *)(lVar10 + 0x18 + lVar11) = param_3[6];
      *(undefined1 *)(*(longlong *)(param_1 + 0x188) + 0xb + lVar10) = uVar2; /* 回写保留值 */
      if ((int)param_2 < 0) {
        func_0x000141084490(0x57);
      } else {
        uVar9 = param_5 | 0x100;
        if ((param_5 & 0x8017) == 0) uVar9 = param_5;
        uVar8 = 0; local_78 = 0; local_80 = local_68;
        local_70 = 0x8000000000000004;
        func_0x000140263b00(param_1,param_2,&local_80,1);   /* 收集脏块 */
        if (local_78 != 0) {
          do {
            *(ushort *)(*(longlong *)(local_80 + uVar8 * 8) + 0x294) =
                 *(ushort *)(*(longlong *)(local_80 + uVar8 * 8) + 0x294) | uVar9;
            uVar8 = (ulonglong)((int)uVar8 + 1);
          } while (uVar8 < local_78);
        }
        if (((uVar9 & 0x8014) != 0) && (*(longlong *)(param_1 + 0xa78) != 0)) {
          uVar8 = (ulonglong)(*(int *)(param_1 + 0xf4) + 1);
          func_0x000140206140(*(longlong *)(param_1 + 0xa78),(ulonglong)param_2 % uVar8, param_2 / uVar8);
        }
        if ((local_70 & 0x8000000000000000) == 0) {
          func_0x000141080be0(local_80,0x1417f2a18,0xfffffffa);
        }
      }
      *(undefined1 *)(param_1 + 0xb88) = 1;          /* 全局脏标记 */
    }
  } else {
    func_0x000141084490(0x57);
  }
  func_0x00014115d620(local_48 ^ (ulonglong)auStack_a8);
  return;
}
