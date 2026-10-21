
ulonglong FUN_14027f170(longlong param_1,int param_2,int param_3)

{
  longlong lVar1;
  int iVar2;
  ulonglong uVar3;
  int iVar4;
  longlong lVar5;
  int iVar6;
  
  lVar5 = 0;
  do {
    iVar2 = *(int *)(param_1 + 0xf4);
    iVar6 = *(int *)(&UNK_1412e1668 + lVar5) + param_3;
    iVar4 = *(int *)(&UNK_1412e1658 + lVar5) + param_2;
    lVar1 = *(longlong *)(param_1 + 0x188);
    uVar3 = FUN_14027ef50(param_1,iVar4,iVar6);
    if (((*(byte *)((longlong)((iVar2 + 1) * iVar6 + iVar4) * 0x1c + lVar1 + 0xc) >> 1 & 1) != 0) &&
       ((char)uVar3 == '\0')) {
      uVar3 = FUN_14027ece0(param_1,iVar4,iVar6);
      iVar2 = (int)uVar3;
      if ((iVar2 != 0) &&
         ((uVar3 = (ulonglong)(iVar2 - 1U), iVar2 - 1U == 0 ||
          (uVar3 = (ulonglong)(iVar2 - 2U), iVar2 - 2U == 0)))) {
        return CONCAT71((int7)(uVar3 >> 8),1);
      }
    }
    lVar5 = lVar5 + 4;
    if (0xf < lVar5) {
      return uVar3 & 0xffffffffffffff00;
    }
  } while( true );
}

 (GhidraScript)  
INFO  /we_decrypted.exe: file created (awang) (LocalFileSystem)  
INFO  REPORT: Save succeeded for: /we_decrypted.exe (rea2:/we_decrypted.exe) (HeadlessAnalyzer)  
INFO  REPORT: Import succeeded (HeadlessAnalyzer)  
