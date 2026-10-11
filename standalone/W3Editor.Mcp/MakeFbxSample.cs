using System.Globalization;
using System.Text;
using Assimp;
using SkiaSharp;
using W3Editor.Services;
using FbxImportPlugin;

namespace W3Editor.Mcp;

/// <summary>
/// Builds FBX (ASCII) + OBJ + 512² PBR textures for manual plugin testing.
/// AssimpNet 4.1 cannot export FBX (no exporter), so FBX is authored as ASCII
/// that Assimp's importer still reads. Usage: --make-fbx-sample [outDir]
/// </summary>
internal static class MakeFbxSample
{
    public static int Run(string[] args)
    {
        string outDir = args.Length >= 2
            ? Path.GetFullPath(args[1])
            : Path.Combine(EditorPaths.OutDir, "fbx_sample");
        if (Directory.Exists(outDir))
            Directory.Delete(outDir, recursive: true);
        Directory.CreateDirectory(outDir);

        float edge = FountainReference.EdgeWorldUnits;
        WritePack(outDir, "sample_prop", edge);

        string largeDir = Path.Combine(outDir, "too_large");
        Directory.CreateDirectory(largeDir);
        WritePack(largeDir, "oversized_prop", FountainReference.MaxEdgeWorldUnits * 2f);

        string fbxPath = Path.Combine(outDir, "sample_prop.fbx");
        string largeFbx = Path.Combine(largeDir, "oversized_prop.fbx");

        // Sanity: Assimp must open the authored FBX.
        using (var ctx = new AssimpContext())
        {
            Scene scene = ctx.ImportFile(fbxPath, PostProcessSteps.Triangulate);
            if (scene.MeshCount == 0)
                throw new InvalidOperationException("generated FBX has no meshes");
        }

        File.WriteAllText(Path.Combine(outDir, "README.txt"), $"""
W3Editor FBX 导入测试样例（本机生成，非网上下载）

【推荐导入】正常比例（约喷泉 1×，不应自动缩放）
  {fbxPath}
  同目录贴图：sample_prop_albedo / _normal / _orm.png（512×512）

【缩放测试】超大比例（约喷泉 20×，应自动缩到 10×）
  {largeFbx}

备用：同目录还有 .obj + .mtl（插件同样支持）。

操作：
  1. 运行 START_W3_EDITOR.bat（Release）
  2. 打开任意地图 → 点「3D 视口」
  3. 工具 → 插件 → 导入 FBX（外部模型）…
  4. 选上面的 .fbx

喷泉参考边长 F = {FountainReference.EdgeWorldUnits:0.###}
上限 10×F = {FountainReference.MaxEdgeWorldUnits:0.###}
""", Encoding.UTF8);

        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            outDir,
            normalFbx = fbxPath,
            oversizedFbx = largeFbx,
            edge,
            maxEdge = FountainReference.MaxEdgeWorldUnits,
            note = "FBX is ASCII authored for Assimp import; AssimpNet cannot export FBX.",
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static void WritePack(string dir, string stem, float edge)
    {
        string albedo = Path.Combine(dir, $"{stem}_albedo.png");
        string normal = Path.Combine(dir, $"{stem}_normal.png");
        string orm = Path.Combine(dir, $"{stem}_orm.png");
        WriteAlbedo(albedo, 512);
        WriteNormal(normal, 512);
        WriteOrm(orm, 512);

        WriteAsciiFbx(Path.Combine(dir, $"{stem}.fbx"), stem, edge,
            Path.GetFileName(albedo), Path.GetFileName(normal), Path.GetFileName(orm));
        WriteObjMtl(dir, stem, edge,
            Path.GetFileName(albedo), Path.GetFileName(normal), Path.GetFileName(orm));
    }

    /// <summary>
    /// Minimal FBX 7.4 ASCII cube. Coordinates are Assimp/Y-up; the import
    /// plugin swaps to editor Z-up.
    /// </summary>
    private static void WriteAsciiFbx(
        string path, string stem, float edge,
        string albedoFile, string normalFile, string ormFile)
    {
        float h = edge * 0.5f;
        string F(float v) => v.ToString("0.######", CultureInfo.InvariantCulture);

        // 8 corners
        var corners = new (float X, float Y, float Z)[]
        {
            (-h, -h, -h), (h, -h, -h), (h, h, -h), (-h, h, -h),
            (-h, -h,  h), (h, -h,  h), (h, h,  h), (-h, h,  h),
        };
        // 6 faces × 4 verts (fan indices into corners), outward for Y-up box
        int[][] faces =
        [
            [4, 5, 6, 7], // +Z
            [1, 0, 3, 2], // -Z
            [0, 4, 7, 3], // -X
            [5, 1, 2, 6], // +X
            [3, 7, 6, 2], // +Y
            [0, 1, 5, 4], // -Y
        ];

        var sb = new StringBuilder();
        sb.AppendLine("; FBX 7.4.0 project file");
        sb.AppendLine("FBXHeaderExtension:  {");
        sb.AppendLine("    FBXHeaderVersion: 1003");
        sb.AppendLine("    FBXVersion: 7400");
        sb.AppendLine("}");
        sb.AppendLine("Definitions:  {");
        sb.AppendLine("    Version: 100");
        sb.AppendLine("    Count: 4");
        sb.AppendLine("    ObjectType: \"Geometry\" { Count: 1 }");
        sb.AppendLine("    ObjectType: \"Model\" { Count: 1 }");
        sb.AppendLine("    ObjectType: \"Material\" { Count: 1 }");
        sb.AppendLine("    ObjectType: \"Texture\" { Count: 3 }");
        sb.AppendLine("}");
        sb.AppendLine("Objects:  {");

        // Geometry — PolygonVertexIndex uses ~ for last index of each poly
        sb.AppendLine("    Geometry: 1000000001, \"Geometry::Cube\", \"Mesh\" {");
        sb.Append("        Vertices: *" + (corners.Length * 3) + " { a: ");
        for (int i = 0; i < corners.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"{F(corners[i].X)},{F(corners[i].Y)},{F(corners[i].Z)}");
        }
        sb.AppendLine(" }");
        sb.Append("        PolygonVertexIndex: *" + (faces.Length * 4) + " { a: ");
        bool first = true;
        foreach (int[] face in faces)
        {
            for (int i = 0; i < 4; i++)
            {
                if (!first) sb.Append(',');
                first = false;
                int idx = face[i];
                sb.Append(i == 3 ? (~idx).ToString(CultureInfo.InvariantCulture) : idx.ToString(CultureInfo.InvariantCulture));
            }
        }
        sb.AppendLine(" }");
        sb.AppendLine("        Layer: 0 {");
        sb.AppendLine("            Version: 100");
        sb.AppendLine("            LayerElement:  {");
        sb.AppendLine("                Type: \"LayerElementMaterial\"");
        sb.AppendLine("                TypedIndex: 0");
        sb.AppendLine("            }");
        sb.AppendLine("            LayerElement:  {");
        sb.AppendLine("                Type: \"LayerElementUV\"");
        sb.AppendLine("                TypedIndex: 0");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("        LayerElementMaterial: 0 {");
        sb.AppendLine("            Version: 101");
        sb.AppendLine("            Name: \"\"");
        sb.AppendLine("            MappingInformationType: \"AllSame\"");
        sb.AppendLine("            ReferenceInformationType: \"IndexToDirect\"");
        sb.AppendLine("            Materials: *1 { a: 0 }");
        sb.AppendLine("        }");
        sb.AppendLine("        LayerElementUV: 0 {");
        sb.AppendLine("            Version: 101");
        sb.AppendLine("            Name: \"UVMap\"");
        sb.AppendLine("            MappingInformationType: \"ByPolygonVertex\"");
        sb.AppendLine("            ReferenceInformationType: \"IndexToDirect\"");
        sb.Append("            UV: *48 { a: ");
        // 6 faces * 4 verts * (0,0)(1,0)(1,1)(0,1)
        var uv = new[] { 0f, 0f, 1f, 0f, 1f, 1f, 0f, 1f };
        first = true;
        for (int f = 0; f < 6; f++)
        for (int i = 0; i < 8; i++)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(F(uv[i]));
        }
        sb.AppendLine(" }");
        sb.Append("            UVIndex: *24 { a: ");
        first = true;
        for (int i = 0; i < 24; i++)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(i.ToString(CultureInfo.InvariantCulture));
        }
        sb.AppendLine(" }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");

        sb.AppendLine($"    Model: 1000000002, \"Model::{stem}\", \"Mesh\" {{");
        sb.AppendLine("        Version: 232");
        sb.AppendLine("        Properties70:  {");
        sb.AppendLine("            P: \"RotationActive\", \"bool\", \"\", \"\",1");
        sb.AppendLine("            P: \"InheritType\", \"enum\", \"\", \"\",1");
        sb.AppendLine("            P: \"ScalingMax\", \"Vector3D\", \"Vector\", \"\",0,0,0");
        sb.AppendLine("            P: \"DefaultAttributeIndex\", \"int\", \"Integer\", \"\",0");
        sb.AppendLine("        }");
        sb.AppendLine("        Shading: Y");
        sb.AppendLine("        Culling: \"CullingOff\"");
        sb.AppendLine("    }");

        sb.AppendLine("    Material: 1000000003, \"Material::SamplePropMat\", \"\" {");
        sb.AppendLine("        Version: 102");
        sb.AppendLine("        ShadingModel: \"phong\"");
        sb.AppendLine("        MultiLayer: 0");
        sb.AppendLine("        Properties70:  {");
        sb.AppendLine("            P: \"DiffuseColor\", \"Color\", \"\", \"A\",0.85,0.75,0.55");
        sb.AppendLine("            P: \"Diffuse\", \"Vector3D\", \"Vector\", \"\",0.85,0.75,0.55");
        sb.AppendLine("        }");
        sb.AppendLine("    }");

        void Tex(long id, string name, string file)
        {
            sb.AppendLine($"    Texture: {id}, \"Texture::{name}\", \"\" {{");
            sb.AppendLine("        Type: \"TextureVideoClip\"");
            sb.AppendLine("        Version: 202");
            sb.AppendLine($"        TextureName: \"Texture::{name}\"");
            sb.AppendLine("        Properties70:  {");
            sb.AppendLine("            P: \"CurrentTextureBlendMode\", \"enum\", \"\", \"\",0");
            sb.AppendLine("            P: \"UVSet\", \"KString\", \"\", \"\", \"UVMap\"");
            sb.AppendLine("            P: \"UseMaterial\", \"bool\", \"\", \"\",1");
            sb.AppendLine("        }");
            sb.AppendLine($"        Media: \"Video::{name}\"");
            sb.AppendLine($"        FileName: \"{file}\"");
            sb.AppendLine($"        RelativeFilename: \"{file}\"");
            sb.AppendLine("    }");
            sb.AppendLine($"    Video: {id + 10}, \"Video::{name}\", \"Clip\" {{");
            sb.AppendLine("        Type: \"Clip\"");
            sb.AppendLine("        Properties70:  {");
            sb.AppendLine($"            P: \"Path\", \"KString\", \"XRefUrl\", \"\", \"{file}\"");
            sb.AppendLine("        }");
            sb.AppendLine($"        UseMipMap: 0");
            sb.AppendLine($"        Filename: \"{file}\"");
            sb.AppendLine($"        RelativeFilename: \"{file}\"");
            sb.AppendLine("    }");
        }

        Tex(1000000010, "Albedo", albedoFile);
        Tex(1000000011, "Normal", normalFile);
        Tex(1000000012, "Orm", ormFile);

        sb.AppendLine("}");
        sb.AppendLine("Connections:  {");
        sb.AppendLine("    C: \"OO\",1000000001,1000000002");
        sb.AppendLine("    C: \"OO\",1000000002,0");
        sb.AppendLine("    C: \"OO\",1000000003,1000000002");
        sb.AppendLine("    C: \"OP\",1000000010,1000000003, \"DiffuseColor\"");
        sb.AppendLine("    C: \"OP\",1000000011,1000000003, \"NormalMap\"");
        sb.AppendLine("    C: \"OP\",1000000012,1000000003, \"EmissiveColor\"");
        sb.AppendLine("    C: \"OO\",1000000020,1000000010");
        sb.AppendLine("    C: \"OO\",1000000021,1000000011");
        sb.AppendLine("    C: \"OO\",1000000022,1000000012");
        sb.AppendLine("}");
        File.WriteAllText(path, sb.ToString(), Encoding.ASCII);
    }

    private static void WriteObjMtl(
        string dir, string stem, float edge,
        string albedoFile, string normalFile, string ormFile)
    {
        float h = edge * 0.5f;
        string F(float v) => v.ToString("0.######", CultureInfo.InvariantCulture);
        var obj = new StringBuilder();
        obj.AppendLine($"mtllib {stem}.mtl");
        obj.AppendLine($"o {stem}");
        obj.AppendLine($"v {F(-h)} {F(-h)} {F(-h)}");
        obj.AppendLine($"v {F(h)} {F(-h)} {F(-h)}");
        obj.AppendLine($"v {F(h)} {F(h)} {F(-h)}");
        obj.AppendLine($"v {F(-h)} {F(h)} {F(-h)}");
        obj.AppendLine($"v {F(-h)} {F(-h)} {F(h)}");
        obj.AppendLine($"v {F(h)} {F(-h)} {F(h)}");
        obj.AppendLine($"v {F(h)} {F(h)} {F(h)}");
        obj.AppendLine($"v {F(-h)} {F(h)} {F(h)}");
        obj.AppendLine("vt 0 0");
        obj.AppendLine("vt 1 0");
        obj.AppendLine("vt 1 1");
        obj.AppendLine("vt 0 1");
        obj.AppendLine("usemtl SamplePropMat");
        obj.AppendLine("f 1/1 2/2 3/3 4/4");
        obj.AppendLine("f 5/1 8/4 7/3 6/2");
        obj.AppendLine("f 1/1 5/2 6/3 2/4");
        obj.AppendLine("f 2/1 6/2 7/3 3/4");
        obj.AppendLine("f 3/1 7/2 8/3 4/4");
        obj.AppendLine("f 4/1 8/2 5/3 1/4");
        File.WriteAllText(Path.Combine(dir, $"{stem}.obj"), obj.ToString());

        var mtl = new StringBuilder();
        mtl.AppendLine("newmtl SamplePropMat");
        mtl.AppendLine("Kd 0.85 0.75 0.55");
        mtl.AppendLine($"map_Kd {albedoFile}");
        mtl.AppendLine($"map_Bump {normalFile}");
        mtl.AppendLine($"map_Ke {ormFile}");
        File.WriteAllText(Path.Combine(dir, $"{stem}.mtl"), mtl.ToString());
    }

    private static void WriteAlbedo(string path, int size)
    {
        using var bmp = new SKBitmap(size, size);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            bool checker = ((x / 32) + (y / 32)) % 2 == 0;
            bmp.SetPixel(x, y, checker
                ? new SKColor(210, 170, 110)
                : new SKColor(160, 120, 70));
        }
        SavePng(bmp, path);
    }

    private static void WriteNormal(string path, int size)
    {
        using var bmp = new SKBitmap(size, size);
        var flat = new SKColor(128, 128, 255);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            bmp.SetPixel(x, y, flat);
        SavePng(bmp, path);
    }

    private static void WriteOrm(string path, int size)
    {
        using var bmp = new SKBitmap(size, size);
        var orm = new SKColor(200, 140, 0);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            bmp.SetPixel(x, y, orm);
        SavePng(bmp, path);
    }

    private static void SavePng(SKBitmap bmp, string path)
    {
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        using var fs = File.OpenWrite(path);
        data.SaveTo(fs);
    }
}
