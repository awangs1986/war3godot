using System;
using Godot;
using W3Compat.Pathing;
using CorePathingGrid = W3Compat.Pathing.W3PathingGrid;

namespace Diablo1Like.W3.Pathing;

/// <summary>Godot 适配：Warsmash PathingGrid。</summary>
[GlobalClass]
public partial class W3PathingGridBridge : RefCounted
{
    public const float CellSize = CorePathingGrid.CellSize;
    public const float Cell = CorePathingGrid.Cell;
    public const int Unwalkable = CorePathingGrid.Unwalkable;
    public const int Unflyable = CorePathingGrid.Unflyable;
    public const int Unbuildable = CorePathingGrid.Unbuildable;
    public const int BlockVision = CorePathingGrid.BlockVision;
    public const int Blighted = CorePathingGrid.Blighted;
    public const int Unswimable = CorePathingGrid.Unswimable;

    /// <summary>与 GDScript enum 对齐。</summary>
    public enum MovementType
    {
        Foot = 0,
        Fly = 1,
        Float = 2,
        Amphibious = 3,
        Hover = 4,
        Disabled = 5,
    }

    private CorePathingGrid? _grid;

    public static string LastError => CorePathingGrid.LastError;
    public static string last_error => LastError;

    /// <summary>供 GDScript 以方法形式读取静态错误（勿再新增同名 get_/set_ 方法，以免 CS0082）。</summary>
    public static string GetLastError() => LastError;

    public int width => _grid?.Width ?? 0;
    public int height => _grid?.Height ?? 0;

    public Vector2 center_offset
    {
        get => new Vector2(_grid?.CenterOffsetX ?? 0f, _grid?.CenterOffsetY ?? 0f);
        set
        {
            if (_grid != null)
            {
                _grid.CenterOffsetX = value.X;
                _grid.CenterOffsetY = value.Y;
            }
        }
    }

    public byte[] cells => _grid?.Cells ?? System.Array.Empty<byte>();

    internal CorePathingGrid? CoreGrid => _grid;

    public static W3PathingGridBridge? LoadBytes(byte[] bytes, Vector2 centerOffset)
    {
        var grid = CorePathingGrid.LoadBytes(bytes, centerOffset.X, centerOffset.Y);
        return grid == null ? null : new W3PathingGridBridge { _grid = grid };
    }

    public static W3PathingGridBridge? load_bytes(byte[] bytes, Vector2 centerOffset) =>
        LoadBytes(bytes, centerOffset);

    /// <summary>从 W3PathingMapBridge / w3_wpm.gd 对象构造。</summary>
    public static W3PathingGridBridge? FromWpm(GodotObject? wpm, Vector2 center)
    {
        if (wpm == null)
            return null;
        int w = wpm.Get("width").AsInt32();
        int h = wpm.Get("height").AsInt32();
        if (w <= 0 || h <= 0)
            return null;

        // GDS w3_wpm.gd 字段为 pathing；C# 桥可能暴露 get_cells()
        byte[] pathing = Array.Empty<byte>();
        if (wpm.HasMethod("get_cells"))
            pathing = wpm.Call("get_cells").AsByteArray();
        if (pathing.Length == 0)
        {
            Variant pathingVar = wpm.Get("pathing");
            if (pathingVar.VariantType == Variant.Type.PackedByteArray)
                pathing = pathingVar.AsByteArray();
        }

        int expected = checked(w * h);
        if (pathing.Length != expected)
        {
            GD.PushError($"W3PathingGridBridge.FromWpm: pathing length {pathing.Length} != {w}x{h}");
            return null;
        }

        var grid = CorePathingGrid.FromCells(w, h, center.X, center.Y, pathing);
        return grid == null ? null : new W3PathingGridBridge { _grid = grid };
    }

    public static W3PathingGridBridge? from_wpm(GodotObject? wpm, Vector2 center) => FromWpm(wpm, center);

    /// <summary>无 WPM 时从 HeightSampler 合成 4x4 子格。</summary>
    public static W3PathingGridBridge? FromSampler(GodotObject? sampler, Vector2 center)
    {
        if (sampler == null)
            return null;
        GodotObject? map = sampler.Get("map").AsGodotObject();
        if (map == null)
            return null;
        var terrainMap = Terrain.W3TerrainMapBridge.FromGodotResource(map);
        var heightSampler = new Terrain.W3HeightSampler(terrainMap);
        int tileW = terrainMap.TileWidth;
        int tileH = terrainMap.TileHeight;
        int gridW = tileW * 4;
        int gridH = tileH * 4;
        var outCells = new byte[gridW * gridH];
        for (int cy = 0; cy < gridH; cy++)
        {
            for (int cx = 0; cx < gridW; cx++)
            {
                int flags = 0;
                if (!heightSampler.IsTileWalkable(cx / 4, cy / 4))
                    flags = Unwalkable | Unswimable | Unbuildable;
                outCells[cy * gridW + cx] = (byte)flags;
            }
        }
        var grid = CorePathingGrid.FromCells(gridW, gridH, center.X, center.Y, outCells);
        return new W3PathingGridBridge { _grid = grid };
    }

    public static W3PathingGridBridge? from_sampler(GodotObject? sampler, Vector2 center) =>
        FromSampler(sampler, center);

    public int get_cell_x(float x) => _grid?.GetCellX(x) ?? 0;
    public int get_cell_y(float y) => _grid?.GetCellY(y) ?? 0;
    public float get_world_x(int cellX) => _grid?.GetWorldX(cellX) ?? 0f;
    public float get_world_y(int cellY) => _grid?.GetWorldY(cellY) ?? 0f;
    public float get_world_z(int cellY) => get_world_y(cellY);
    public float get_world_x_from_corner(int cornerX) => _grid?.GetWorldXFromCorner(cornerX) ?? 0f;
    public float get_world_y_from_corner(int cornerY) => _grid?.GetWorldYFromCorner(cornerY) ?? 0f;
    public float get_world_z_from_corner(int cornerY) => get_world_y_from_corner(cornerY);
    public int get_corner_x(float x) => _grid?.GetCornerX(x) ?? 0;
    public int get_corner_y(float y) => _grid?.GetCornerY(y) ?? 0;
    public bool contains(float x, float y) => _grid?.Contains(x, y) ?? false;
    public int get_cell_pathing(int cellX, int cellY) => _grid?.GetCellPathing(cellX, cellY) ?? 0;
    public void blit_unit_collision(float unitX, float unitY, float collisionSize) =>
        _grid?.BlitUnitCollision(unitX, unitY, collisionSize);
    public int get_pathing(float x, float y) => _grid?.GetPathing(x, y) ?? 0;

    public bool is_pathable_movement(float x, float y, int movement) =>
        _grid?.IsPathableMovement(x, y, (W3MovementType)movement) ?? false;

    public bool is_pathable(float x, float y, int movement, float collisionSize) =>
        _grid?.IsPathable(x, y, (W3MovementType)movement, collisionSize) ?? false;

    public bool is_cell_pathable(int cellX, int cellY, int movement, float collisionSize = 0f) =>
        _grid?.IsCellPathable(cellX, cellY, (W3MovementType)movement, collisionSize) ?? false;

    public bool is_pathable_foot(float x, float y, float collisionSize) =>
        _grid?.IsPathableFoot(x, y, collisionSize) ?? false;
}
