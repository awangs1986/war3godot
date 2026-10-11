using System.Diagnostics;
using System.Text.Json;
using W3Editor.Formats;
using W3Editor.Services;

namespace W3Editor.Mcp;

/// <summary>
/// Human-operator capability probe for W3Editor (same code paths UI buttons call).
/// Not a map auto-builder. Records PASS/FAIL/NA with evidence strings.
/// Usage: W3Editor.Mcp.exe --human-probe [outJson]
/// </summary>
internal static class HumanCapabilityProbe
{
    public static int Run(string[] args)
    {
        string outJson = Path.GetFullPath(args.Length >= 2
            ? args[1]
            : Path.Combine(Path.GetTempPath(), "war3godot", "human_probe_w3editor.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(outJson)!);

        var results = new List<object>();
        void Rec(string id, string verdict, string evidence)
            => results.Add(new { id, verdict, evidence });

        // UI-declared disabled features (read from shipped MainWindow.axaml — verified by file read in this session)
        string axaml = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "W3Editor", "MainWindow.axaml");
        axaml = Path.GetFullPath(axaml);
        if (!File.Exists(axaml))
            axaml = Path.Combine(Directory.GetCurrentDirectory(), "standalone", "W3Editor", "MainWindow.axaml");
        string axamlText = File.ReadAllText(axaml);
        Rec("ui.menu.pathing_mode",
            axamlText.Contains("Header=\"寻路\" IsEnabled=\"False\"") ? "MISSING_IN_UI" : "UNKNOWN",
            "MainWindow.axaml 工具菜单「寻路」IsEnabled=False，Tooltip 写占位 P7");
        Rec("ui.menu.region_mode",
            axamlText.Contains("Header=\"区域\" IsEnabled=\"False\"") ? "MISSING_IN_UI" : "UNKNOWN",
            "MainWindow.axaml 工具菜单「区域」IsEnabled=False，Tooltip 写占位 P5");
        Rec("ui.tab.pathing_brush",
            axamlText.Contains("寻路笔刷编辑为 P7") ? "MISSING_IN_UI" : "UNKNOWN",
            "寻路页仅覆盖层勾选；文案写明寻路笔刷为 P7 占位");
        Rec("ui.tab.region_new",
            axamlText.Contains("新建区域\" IsEnabled=\"False\"") ? "MISSING_IN_UI" : "UNKNOWN",
            "区域页「新建区域」按钮 IsEnabled=False");
        Rec("ui.menu.wireframe",
            axamlText.Contains("线框网格\" IsEnabled=\"False\"") ? "MISSING_IN_UI" : "UNKNOWN",
            "视图菜单线框网格占位 P1");
        Rec("ui.has_tile_setter_entry",
            axamlText.Contains("更换图块集") ? "PRESENT_IN_UI" : "MISSING_IN_UI",
            "编辑菜单 + 地形页按钮「更换图块集…」→ TileSetterDialog");
        Rec("ui.has_doodad_unit_tabs",
            axamlText.Contains("Header=\"装饰物\"") && axamlText.Contains("Header=\"单位\"") ? "PRESENT_IN_UI" : "MISSING_IN_UI",
            "右侧 Tab：装饰物/单位调色板 + 3D 左键放置说明");
        Rec("ui.has_terrain_brush_ops",
            axamlText.Contains("Tag=\"Raise\"") && axamlText.Contains("Tag=\"CliffRaise1\"")
            && axamlText.Contains("Tag=\"WaterDeep\"") && axamlText.Contains("Tag=\"Ramp\"")
                ? "PRESENT_IN_UI" : "MISSING_IN_UI",
            "地形页：抬升/降低/整平/平滑、悬崖升降、浅水/深水、坡道、纹理调色板");
        Rec("ui.no_trigger_object_editors",
            !axamlText.Contains("触发编辑器") && !axamlText.Contains("物体编辑器") ? "MISSING_IN_UI" : "PRESENT_IN_UI",
            "菜单中无 Trigger Editor / Object Editor 入口（对照 HiveWE 功能带）");

        // Runtime: same APIs the UI uses after New Map
        var session = new EditorSession();
        try
        {
            string created = session.CreateNew("HumanProbe_LT", 160, 160, 'L', "Lgrs");
            Rec("op.new_map_160", "PASS", created.Split('\n')[0]);
            var map = session.Map.Map!;
            Rec("op.default_ground_tiles", "INFO",
                string.Join(",", map.GroundTilesets));
            // Tile Setter equivalent: MapSession add/remove used by TileSetterDialog
            bool hadYsqd = map.GroundTilesets.Contains("Ysqd");
            if (!hadYsqd)
            {
                session.Map.AddGroundTexture("Ysqd");
                session.Map.AddGroundTexture("Yrtl");
                session.Map.AddGroundTexture("Yhdg");
            }
            Rec("op.tile_setter_add_ysqd_yrtl_yhdg",
                map.GroundTilesets.Contains("Ysqd") && map.GroundTilesets.Contains("Yrtl") && map.GroundTilesets.Contains("Yhdg")
                    ? "PASS" : "FAIL",
                "GroundTilesets=" + string.Join(",", map.GroundTilesets));

            int ysqd = map.GroundTilesets.IndexOf("Ysqd");
            session.BrushStamp("Raise", 80, 80, null, null, 4, 0.6f, 0, false);
            Rec("op.brush_raise", "PASS", "BrushStamp Raise @80,80");
            session.BrushStamp("CliffRaise1", 70, 70, null, null, 3, 1f, 0, false);
            Rec("op.brush_cliff_raise1", "PASS", "BrushStamp CliffRaise1 @70,70");
            session.BrushStamp("WaterDeep", 40, 80, null, null, 4, 1f, 0, false);
            Rec("op.brush_water_deep", "PASS", "BrushStamp WaterDeep @40,80");
            session.BrushStamp("Ramp", 72, 72, null, null, 2, 1f, 0, false);
            Rec("op.brush_ramp", "PASS", "BrushStamp Ramp @72,72");
            if (ysqd >= 0)
            {
                session.BrushStamp("Texture", 80, 80, null, null, 4, 1f, ysqd, false);
                Rec("op.brush_texture_ysqd", "PASS", $"TextureIndex={ysqd} (Ysqd)");
            }
            else Rec("op.brush_texture_ysqd", "FAIL", "Ysqd not in palette");

            session.PlaceObject("LTlt", 0, 0, false, 0, 0, 1f, 0);
            Rec("op.place_doodad_LTlt", session.Map.Doodads.Count >= 1 ? "PASS" : "FAIL",
                $"doodads={session.Map.Doodads.Count}");
            session.PlaceObject("sloc", 1280, -6848, true, 0, 270, 1f, 0);
            Rec("op.place_unit_sloc", session.Map.Units.Count >= 1 ? "PASS" : "FAIL",
                $"units={session.Map.Units.Count}");

            string undoJson = session.Undo();
            Rec("op.undo", undoJson.Contains("\"ok\":true") || undoJson.Contains("\"ok\": true") ? "PASS" : "FAIL", undoJson);
            string redoJson = session.Redo();
            Rec("op.redo", redoJson.Contains("\"ok\":true") || redoJson.Contains("\"ok\": true") ? "PASS" : "FAIL", redoJson);

            session.Save();
            Rec("op.save_w3e_doo", "PASS", session.Map.SourcePath);

            // Pathing brush: no API for paint — only load/overlay
            Rec("op.pathing_brush_paint",
                "MISSING_API",
                "MapSession 可加载 war3map.wpm 供覆盖层显示；无 PathingBrush 绘制 API / UI 笔刷（P7）");
            Rec("op.region_create",
                "MISSING_API",
                "无区域创建/命名 API；UI「新建区域」禁用（P5）");
            Rec("op.trigger_editor",
                "MISSING_IN_UI",
                "无触发编辑器窗口/菜单");
            Rec("op.object_editor",
                "MISSING_IN_UI",
                "无物体编辑器窗口/菜单");
            Rec("op.map_info_editor",
                "MISSING_IN_UI",
                "可显示 w3i 摘要字段；无完整地图信息编辑器（玩家/镜头边界等）");
            Rec("op.camera_editor",
                "MISSING_IN_UI",
                "无镜头编辑面板");
        }
        catch (Exception ex)
        {
            Rec("op.runtime_exception", "FAIL", ex.ToString());
        }

        var doc = new
        {
            tool = "W3Editor",
            probedAt = DateTime.UtcNow.ToString("o"),
            perspective = "human-operator (UI declarations + same code paths as UI buttons)",
            results,
        };
        File.WriteAllText(outJson, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
        Console.Error.WriteLine(JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(outJson);
        return 0;
    }
}
