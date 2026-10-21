/* FUN_1402871F0 —— 纹理 tileId -> 纹理表索引（§52.1）
 * 切片: f_tex2b.exe = 0x1402871F0-0x140287600
 * 证据: ev_1334cae15b451c2699e436a50adb87b8a0ef81203afb3379f34f2be27c1f0faa
 * 关键: tileId==0 => 0xFFFFFFFF(无效)；表项 0x28 字节；未命中且 allowRegister 时注册并返回最后一项索引
 */ulonglong FUN_1402871F0(longlong param_1,int param_2,char param_3)
{
  char cVar1;
  int *piVar2;
  longlong lVar3;
  undefined8 uVar4;
  ulonglong uVar5, uVar6, uVar7;
  undefined1 local_28 [32];

  if (param_2 == 0) return 0xffffffff;               /* id 0 = 无效纹理 */
  uVar5 = 0;
  uVar7 = 0xffffffff;
  if (0 < *(int *)(param_1 + 0x1b0)) {               /* 纹理表项数 */
    piVar2 = *(int **)(param_1 + 0x1b8);             /* 纹理表基址 */
    uVar6 = uVar5;
    do {
      if (*piVar2 == param_2) {                      /* 命中 id */
        uVar7 = uVar6;
        if ((int)uVar6 != -1) return uVar6;
        break;
      }
      uVar6 = (ulonglong)((int)uVar6 + 1);
      uVar5 = uVar5 + 1;
      piVar2 = piVar2 + 10;                          /* 每项 10 int = 0x28 字节 */
    } while ((longlong)uVar5 < (longlong)*(int *)(param_1 + 0x1b0));
  }
  if ((param_3 != '\0') && (lVar3 = func_0x0001401cc080(param_2), lVar3 != 0)) {
    uVar4 = func_0x000140562190(local_28);
    cVar1 = func_0x000140286340(param_1,lVar3 + 0x34,uVar4);   /* 注册新纹理项 */
    if (cVar1 != '\0') {
      *(int *)(*(longlong *)(param_1 + 0x1b8) + (ulonglong)(*(int *)(param_1 + 0x1b0) - 1) * 0x28) = param_2;
      uVar7 = (ulonglong)(*(int *)(param_1 + 0x1b0) - 1);      /* 返回"最后一项"索引 */
    }
  }
  return uVar7;
}
