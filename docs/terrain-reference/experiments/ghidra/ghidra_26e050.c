
ulonglong FUN_14026e050(longlong param_1,int param_2,int param_3)

{
  ulonglong uVar1;
  int iVar2;
  
  iVar2 = *(int *)(param_1 + 0xf4) + 1;
  uVar1 = 0;
  do {
    if ((*(byte *)((longlong)
                   ((*(int *)(&UNK_1412e166c + uVar1) + param_3) * iVar2 +
                    *(int *)(&UNK_1412e165c + uVar1) + param_2) * 0x1c + 10 +
                  *(longlong *)(param_1 + 0x188)) & 0xf) !=
        (*(byte *)((longlong)(iVar2 * param_3 + param_2) * 0x1c + 10 +
                  *(longlong *)(param_1 + 0x188)) & 0xf)) {
      return CONCAT71((int7)(uVar1 >> 8),1);
    }
    uVar1 = uVar1 + 4;
  } while ((longlong)uVar1 < 0xc);
  return uVar1 & 0xffffffffffffff00;
}

 (GhidraScript)  
