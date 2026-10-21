/* FUN_14027F480 —— 坡道合法性判定（§48）
 * 切片: f_rval.exe = 0x14027F480-0x14027F700
 * 证据: ev_f7f4d45b52a9f113550a7e3619b9872c85eba949ccf85cc98cb92df57f0cc3ac
 * 关键: 2x2 四角签名(顺序 (x,y+1),(x+1,y+1),(x+1,y),(x,y)) 与表 0x1412E3654 的 16 行前 4 字节 memcmp
 */undefined8 FUN_14027F480(undefined8 param_1,int param_2,int param_3)
{
  ulonglong uVar1;
  longlong lVar2, lVar3;
  char local_res20 [8];

  uVar1 = func_0x00014027f170();                      /* 2x2 内存在 R/S 类坡道 */
  if ((char)uVar1 != '\0') {
    uVar1 = FUN_14027f670(param_1,param_2,param_3);
    if ((char)uVar1 != '\0') {
LAB_14027f55a:
      return CONCAT71((int7)(uVar1 >> 8),1);
    }
    local_res20[0] = func_0x00014027eb00(param_1,param_2,param_3 + 1);
    local_res20[1] = func_0x00014027eb00(param_1,param_2 + 1,param_3 + 1);
    local_res20[2] = func_0x00014027eb00(param_1,param_2 + 1,param_3);
    local_res20[3] = func_0x00014027eb00(param_1,param_2,param_3);
    lVar3 = 0;
    lVar2 = 0x1412e3654 - (longlong)local_res20;       /* 表基址 0x1412E3654 */
    do {
      uVar1 = 0;
      while (local_res20[uVar1] == (local_res20 + uVar1)[lVar2]) {
        uVar1 = uVar1 + 1;
        if (3 < (longlong)uVar1) goto LAB_14027f55a;   /* 前 4 字节全等 => 命中 */
      }
      lVar3 = lVar3 + 1;
      lVar2 = lVar2 + 0x18;                            /* 每项 0x18 = 24 字节 */
    } while (lVar3 < 0x10);                            /* 共 16 项 */
  }
  return uVar1 & 0xffffffffffffff00;
}
