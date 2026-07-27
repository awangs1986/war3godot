# W3Editor 插件契约 v1 → 模块 v2 迁移

## 当前边界

v1 `IW3EditorPlugin` 保持冻结，用于现有 FBX 插件的二进制兼容。v2 使用
`IEditorModule` + `IModuleContext`，新功能只向 v2 贡献点增长。

`host.plugin` 是 v1 兼容宿主的显式开关；禁用它时 Shell 不扫描、不加载 v1 DLL。
`import.fbx` 是 FBX 能力入口，目前由 v1 shim 完成实际导入。

## 迁移映射

| v1 做法 | v2 做法 |
|---|---|
| `Register(context)` 中添加菜单 | `IEditorModule.Register` 中 `AddCommand` |
| 直接访问窗口/会话 | 由 `Capabilities.Get<T>()` 取窄服务 |
| 自己写地图文件 | 实现 `IPartCodec`，由 `AtomicFileSet` 统一提交 |
| 插件自带面板/窗口生命周期 | `IPanelDescriptor` / `IWindowDescriptor` |
| 隐式永久开启 | `standalone/modules.json` 明确启用/禁用 |

## 验收

1. 模块 `Register` 不做 IO、不解析地图、不创建顶层窗口。
2. 禁用模块后其 UI/命令消失，其余模块仍加载。
3. 若模块认领地图数据，禁用后该数据必须以 `UnknownPart` 字节不变地存活。
4. 执行 `--module-host-accept`、`--plugin-host-selftest`和该插件的专项验收。
