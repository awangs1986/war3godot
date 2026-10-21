/* FUN_140286F40 —— 44 字节 thunk：版本门 + 尾调用（§54.1）
 * 切片: f_tv.exe = 0x140286F40-0x140287A00   （body 0x140286f40-0x140286f6b，44 字节）
 * 证据: ev_4d060b53db567faa9d229812d2bde6c9c59a8a5114bf0ac5766a92bf30953c10
 * 说明: 等价于 FUN_140145400(map, 0)；之前的“坏指令截断”其实是尾调用跳出切片
 *
 * ---- 全量指令（Ghidra 逐条）----
 * 0x140286f40: SUB RSP,0x28
 * 0x140286f44: ADD RCX,0xc0                 ; rcx = &map->[0xc0]
 * 0x140286f4b: CMP dword ptr [RCX],0x12     ; 版本门：必须 == 18
 * 0x140286f4e: JZ 0x140286f61
 * 0x140286f50: MOV ECX,0x57
 * 0x140286f55: CALL 0x141084490             ; error(0x57)
 * 0x140286f5a: XOR AL,AL
 * 0x140286f5c: ADD RSP,0x28
 * 0x140286f60: RET
 * 0x140286f61: XOR EDX,EDX                  ; ★ 第二参数固定 = 0
 * 0x140286f63: ADD RSP,0x28
 * 0x140286f67: JMP 0x140145400              ; ★ 尾调用到加权随机选择
 */
/* 等价 C 表述 */
static inline undefined4 FUN_140286F40(longlong map)
{
    if (*(int *)(map + 0xc0) != 0x12) { func_0x000141084490(0x57); return 0; }
    return FUN_140145400(map, 0);
}
