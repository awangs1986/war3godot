/* FUN_14027EB00 —— 坡道"字母"分类器（§49，A5 结案）
 * 切片: f_cls.exe = 0x14027EB00-0x14027EF50
 * 证据: ev_92c6631e22c58c9243616d6099b2516a789fa3c5b864ef6bb870cd623e08546d
 * 字母: 'A'=65(无坡道位,t=0) 'B'=66(无坡道位,t=1) 'L'=76/'R'=82/'S'=83(有坡道位,t=0,14027ECE0=0/1/2) 'H'=72(有坡道位,t=1) '!'=33
 */char FUN_14027EB00(longlong param_1,int param_2,int param_3)
{
  longlong lVar1;
  char cVar2;
  int iVar3;

  iVar3 = *(int *)(param_1 + 0xf4);
  lVar1 = *(longlong *)(param_1 + 0x188);
  cVar2 = FUN_14027ef50();                            /* t = 本格是否为 3x3 坡道邻域最高层 */
  if ((*(byte *)((longlong)((iVar3 + 1) * param_3 + param_2) * 0x1c + lVar1 + 0xc) >> 1 & 1) == 0) {
    cVar2 = (cVar2 != '\0') + 'A';                    /* 'A'(65) 或 'B'(66) */
  } else if (cVar2 == '\0') {
    iVar3 = FUN_14027ece0(param_1,param_2,param_3);
    if (iVar3 == 0)      cVar2 = 'L';                 /* 76 */
    else if (iVar3 == 1) cVar2 = 'R';                 /* 82 */
    else if (iVar3 == 2) cVar2 = 'S';                 /* 83 */
    else                 cVar2 = '!';                 /* 33 */
  } else {
    cVar2 = 'H';                                      /* 72：坡道且位于高端 */
  }
  return cVar2;
}
