using System;
using Diablo1Like.MapFormat;
using Diablo1Like.W3.Terrain;
using Godot;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Diablo1Like.MapRuntime;

/// <summary>
/// d1map 边界适配：解析留在 W3TerrainCore，出口为强类型
/// <see cref="W3PackedTerrainMap"/> + <see cref="W3PlacementData"/>。
/// 仍保留 Dictionary 出口以兼容旧 GDS 调用方，但不再要求对方重组角点。
/// </summary>
[GlobalClass]
public partial class NativeMapLoader : RefCounted
{
    /// <summary>尝试加载文件夹；失败时 Dictionary 含 error。</summary>
    public GDictionary TryLoadFolder(string folder)
    {
        try
        {
            return LoadFolder(folder);
        }
        catch (Exception ex)
        {
            return new GDictionary { ["error"] = ex.Message };
        }
    }

    public GDictionary try_load_folder(string folder) => TryLoadFolder(folder);

    /// <summary>加载并返回 name / map / placements 强类型字段。</summary>
    public GDictionary LoadFolder(string folder)
    {
        NativeMap native = NativeMapFormat.Load(ProjectSettings.GlobalizePath(folder));
        var packed = new W3PackedTerrainMap();
        packed.PopulateFromCore(native.Terrain);

        var placements = new GArray();
        foreach (NativePlacement placement in native.Placements)
        {
            placements.Add(new W3PlacementData
            {
                Id = placement.Id,
                IsUnit = placement.IsUnit,
                Variation = placement.Variation,
                // War3 X/Y/Z-up → Godot X/Y-up/Z
                Position = new Vector3(placement.X, placement.Z, placement.Y),
                Angle = placement.AngleRad,
                Scale = new Vector3(placement.ScaleX, placement.ScaleZ, placement.ScaleY),
                SkinId = placement.Id,
                PlayerOwner = (int)placement.Owner,
                Flags = (int)placement.Flags,
                CreationNumber = unchecked((int)placement.RefId),
            });
        }

        return new GDictionary
        {
            ["name"] = native.Name,
            ["map"] = packed,
            ["placements"] = placements,
        };
    }

    public GDictionary load_folder(string folder) => LoadFolder(folder);
}
