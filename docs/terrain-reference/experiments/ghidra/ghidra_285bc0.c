/* FUN_140285BC0 —— 边界批量分发（薄壳，§52.2）
 * 切片: f_bnd.exe = 0x140285BC0-0x140285F00
 * 证据: ev_55bfdd180870f1205730943b23fc8c927d99162454b749c986b2fc9f8dc08bbf
 * 说明: 遍历 list，每项解出 (x,y,?) 三元组后交给 FUN_140285C40
 */void FUN_140285BC0(longlong param_1,longlong *param_2)
{
  ulonglong uVar1;
  int local_18;
  undefined1 auStack_14 [4];
  undefined8 uStack_10;

  uVar1 = 0;
  if (param_2[1] != 0) {
    do {
      _local_18 = 0;
      uStack_10 = 0;
      func_0x0001402640b0(param_1,*(undefined8 *)(*param_2 + uVar1 * 8),auStack_14,
                          (longlong)&uStack_10 + 4,&local_18,&uStack_10);
      FUN_140285c40(param_1,&local_18);
      uVar1 = (ulonglong)((int)uVar1 + 1);
    } while (uVar1 < (ulonglong)param_2[1]);
  }
  return;
}
