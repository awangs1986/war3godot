using W3Editor.PluginContracts;

namespace FbxImportPlugin;

public sealed class FbxImportPlugin : IW3EditorPlugin
{
    public string Id => "com.d1.fbx-import";
    public string Name => "FBX 导入";
    public string Version => "0.1.0";

    public void Register(IEditorPluginContext ctx)
    {
        ctx.AddMenuCommand(
            "工具(_T)/插件/导入 FBX（外部模型）…",
            "fbx.import.external",
            ct => ImportAsync(ctx, FbxOwnership.External, ct));
        ctx.AddMenuCommand(
            "工具(_T)/插件/导入 FBX（打包进地图）…",
            "fbx.import.map-packed",
            ct => ImportAsync(ctx, FbxOwnership.MapPacked, ct));
    }

    private static async Task ImportAsync(
        IEditorPluginContext ctx,
        FbxOwnership ownership,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (ownership == FbxOwnership.MapPacked && ctx.CurrentMapFolder == null)
        {
            var report = new PluginValidationReport
            {
                Ownership = "map-packed",
                Issues =
                {
                    new PluginValidationIssue
                    {
                        Severity = PluginIssueSeverity.Error,
                        Code = "no-map-folder",
                        Message = "打包进地图需要已打开的文件夹地图（d1map 或解压的 War3 目录）。请先另存为文件夹/d1map。",
                    },
                },
            };
            ctx.ShowReport("FBX 导入结果", report);
            return;
        }

        string? path = await ctx.PickOpenFileAsync(
            ownership == FbxOwnership.External ? "导入 FBX（外部模型）" : "导入 FBX（打包进地图）",
            "*.fbx;*.obj");
        if (string.IsNullOrWhiteSpace(path))
            return;

        string targetRoot = ownership == FbxOwnership.External
            ? ctx.SharedModelsRoot
            : ctx.CurrentMapFolder!;

        FbxImportResult result;
        try
        {
            result = FbxPipeline.Import(path, ownership, targetRoot);
        }
        catch (Exception ex)
        {
            ctx.ShowReport("FBX 导入结果 — " + Path.GetFileName(path), new PluginValidationReport
            {
                Ownership = ownership == FbxOwnership.External ? "external" : "map-packed",
                Issues =
                {
                    new PluginValidationIssue
                    {
                        Severity = PluginIssueSeverity.Error,
                        Code = "import-failed",
                        Message = "导入失败：" + ex.Message,
                    },
                },
            });
            return;
        }

        try
        {
            ctx.PlacePreviewMesh(result.Mesh, result.ModelId);
        }
        catch (Exception ex)
        {
            result.Report.Issues.Add(new PluginValidationIssue
            {
                Severity = PluginIssueSeverity.Warn,
                Code = "preview-unavailable",
                Message = "已落盘，但 3D 预览尚未就绪：" + ex.Message,
            });
        }

        ctx.ShowReport("FBX 导入结果 — " + Path.GetFileName(path), result.Report);
    }
}
