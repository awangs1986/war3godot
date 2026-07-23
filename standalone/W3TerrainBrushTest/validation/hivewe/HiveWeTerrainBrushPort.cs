// TEMPORARY AGPL-3.0 VALIDATION PORT.
// Provenance: HiveWE src/brush/brush.cpp, terrain_brush.cpp, terrain_operators.cpp
// Revision: 2f001ee16421dc9a9d6ff744dd032900e934f082. See NOTICE.md in this folder.
// This file is intentionally isolated from the production terrain add-on. It is a
// behavioural C# translation for workflow validation, not project-owned engine code.
using System;
using System.Collections.Generic;
using Godot;
using W3Foundation.Terrain.Data;
using W3Foundation.Terrain.Editing;

namespace W3Foundation.Validation.HiveWe;

/// <summary>
/// Behavioural port of HiveWE's TerrainBrush + terrain operators for the temporary
/// Godot validation panel. The document has no HiveWE pathing/blight/variation fields,
/// so those source-only side effects deliberately have no destination here.
/// </summary>
public sealed class HiveWeTerrainBrushPort
{
    private TerrainDocument? _before;
    private bool _active;
    private float _deformationGround;
    private float _deformationWater;
    private byte _layerHeight;
    private Rect2I _updatedArea;

    public TerrainDocument? Before => _before;
    public bool Active => _active;

    // HiveWE Brush::get_position(), position_granularity = 1, size_granularity = 4.
    public Vector2 GetPosition(Vector2 mouseWorld, TerrainBrush brush)
    {
        int size = GetSize(brush);
        bool evenWidth = size % 2 == 0;
        Vector2 even = (mouseWorld + new Vector2(0.5f, 0.5f)).Floor();
        Vector2 uneven = mouseWorld.Floor() + new Vector2(0.5f, 0.5f);
        // TerrainBrush uses a corner brush, so HiveWE swaps the two candidates.
        (even, uneven) = (uneven, even);
        return evenWidth ? even : uneven;
    }

    // HiveWE TerrainBrush::get_unclipped_pos() for Brush::Type::corner.
    public Vector2I GetUnclippedPosition(Vector2 mouseWorld, TerrainBrush brush)
    {
        float brushSize = GetSize(brush);
        Vector2 position = mouseWorld - new Vector2(brushSize * 0.5f, brushSize * 0.5f) + Vector2.One;
        return new Vector2I(Mathf.FloorToInt(position.X), Mathf.FloorToInt(position.Y));
    }

    public void ApplyBegin(TerrainDocument document, Vector2 mouseWorld, TerrainBrush brush)
    {
        document.EnsureInitialized();
        _before = document.CloneDocument(); // HiveWE snapshots all corners at stroke begin.
        _active = true;
        _updatedArea = new Rect2I();
        Rect2I area = GetArea(document, mouseWorld, brush);
        int centerX = Math.Clamp(area.Position.X + Mathf.FloorToInt(area.Size.X * 0.5f), 0, document.CornerWidth - 1);
        int centerY = Math.Clamp(area.Position.Y + Mathf.FloorToInt(area.Size.Y * 0.5f), 0, document.CornerHeight - 1);
        int center = document.CornerIndex(centerX, centerY);
        _deformationGround = document.GroundHeights[center];
        _deformationWater = document.WaterHeights[center];
        _layerHeight = GetTargetLayer(document, center, brush.Operation);
    }

    public Rect2I Apply(TerrainDocument document, Vector2 mouseWorld, TerrainBrush brush, double frameDelta)
    {
        if (!_active)
            return new Rect2I();
        Rect2I area = GetArea(document, mouseWorld, brush);
        if (area.Size.X <= 0 || area.Size.Y <= 0)
            return new Rect2I();

        bool geometryChanged = brush.Operation != TerrainBrushOperation.PaintTexture;
        switch (brush.Operation)
        {
            case TerrainBrushOperation.PaintTexture:
                ApplyTexture(document, area, mouseWorld, brush);
                break;
            case TerrainBrushOperation.HeightRaise:
            case TerrainBrushOperation.HeightLower:
            case TerrainBrushOperation.HeightFlatten:
            case TerrainBrushOperation.HeightSteep:
            case TerrainBrushOperation.HeightSmooth:
                ApplyHeight(document, area, mouseWorld, brush, frameDelta);
                break;
            case TerrainBrushOperation.Ramp:
                ApplyRamps(document, area, mouseWorld, brush);
                break;
            default:
                ApplyCliffs(document, area, mouseWorld, brush);
                break;
        }
        Rect2I dirty = Expand(document, area, geometryChanged ? 1 : 0);
        _updatedArea = _updatedArea.Size.X == 0 || _updatedArea.Size.Y == 0 ? dirty : _updatedArea.Merge(dirty);
        document.NotifyEdited(dirty, geometryChanged);
        return dirty;
    }

    public TerrainDocument? ApplyEnd()
    {
        _active = false;
        TerrainDocument? before = _before;
        _before = null;
        return before;
    }

    private static int GetSize(TerrainBrush brush) => Math.Clamp(Mathf.RoundToInt(Math.Max(0.5f, brush.Radius) * 2.0f), 1, 249);

    private Rect2I GetArea(TerrainDocument document, Vector2 mouseWorld, TerrainBrush brush)
    {
        Vector2I pos = GetUnclippedPosition(mouseWorld, brush);
        int size = GetSize(brush);
        int left = Math.Clamp(pos.X, 0, document.TileWidth);
        int top = Math.Clamp(pos.Y, 0, document.TileHeight);
        int right = Math.Clamp(pos.X + size - 1, 0, document.TileWidth);
        int bottom = Math.Clamp(pos.Y + size - 1, 0, document.TileHeight);
        return right < left || bottom < top ? new Rect2I() : new Rect2I(left, top, right - left + 1, bottom - top + 1);
    }

    // HiveWE Brush::contains() using its logical brush size (size / size_granularity).
    private static bool Contains(Vector2I point, TerrainBrush brush)
    {
        if (brush.Square)
            return true;
        int half = GetSize(brush) / 2;
        int dx = point.X - half;
        int dy = point.Y - half;
        return dx * dx + dy * dy <= half * half;
    }

    private void ApplyTexture(TerrainDocument document, Rect2I area, Vector2 mouseWorld, TerrainBrush brush)
    {
        Vector2I pos = GetUnclippedPosition(mouseWorld, brush);
        Vector2I offset = new(Math.Min(pos.X, 0), Math.Min(pos.Y, 0));
        for (int y = area.Position.Y; y < area.End.Y; y++)
        for (int x = area.Position.X; x < area.End.X; x++)
        {
            if (!Contains(new Vector2I(x - area.Position.X, y - area.Position.Y) - offset, brush)) continue;
            document.GroundMaterials[document.CornerIndex(x, y)] = (byte)Math.Clamp(brush.MaterialIndex, 0, 255);
        }
    }

    private void ApplyHeight(TerrainDocument document, Rect2I area, Vector2 mouseWorld, TerrainBrush brush, double frameDelta)
    {
        Vector2I pos = GetUnclippedPosition(mouseWorld, brush);
        Vector2I offset = new(Math.Min(pos.X, 0), Math.Min(pos.Y, 0));
        int areaHeight = area.Size.Y;
        var ground = new float[area.Size.X * areaHeight];
        var water = new float[ground.Length];
        for (int y = area.Position.Y; y < area.End.Y; y++)
        for (int x = area.Position.X; x < area.End.X; x++)
        {
            int source = document.CornerIndex(x, y);
            int local = (x - area.Position.X) * areaHeight + (y - area.Position.Y);
            ground[local] = document.GroundHeights[source];
            water[local] = document.WaterHeights[source];
        }

        for (int y = area.Position.Y; y < area.End.Y; y++)
        for (int x = area.Position.X; x < area.End.X; x++)
        {
            if (!Contains(new Vector2I(x - area.Position.X, y - area.Position.Y) - offset, brush)) continue;
            int index = document.CornerIndex(x, y);
            float nextGround = document.GroundHeights[index];
            float nextWater = document.WaterHeights[index];
            int centerX = area.Position.X + Mathf.FloorToInt(area.Size.X * 0.5f);
            int centerY = area.Position.Y + Mathf.FloorToInt(area.Size.Y * 0.5f);
            float distance = new Vector2(centerX - x, centerY - y).Length();
            // Unit conversion only: HiveWE stores normalized terrain units; Godot document stores world units.
            float delta = Math.Max(0.0f, 1.0f - distance / GetSize(brush) * Mathf.Sqrt(2.0f)) * (float)frameDelta * brush.Strength;
            switch (brush.Operation)
            {
                case TerrainBrushOperation.HeightRaise: nextGround += delta; nextWater += delta; break;
                case TerrainBrushOperation.HeightLower: nextGround -= delta; nextWater -= delta; break;
                case TerrainBrushOperation.HeightFlatten: nextGround = _deformationGround; nextWater = _deformationWater; break;
                case TerrainBrushOperation.HeightSteep: break; // HiveWE deformation::ripple is intentionally a no-op.
                case TerrainBrushOperation.HeightSmooth:
                    nextGround = Smooth(document, area, ground, x, y, nextGround, true);
                    nextWater = Smooth(document, area, water, x, y, nextWater, false);
                    break;
            }
            document.GroundHeights[index] = Math.Clamp(nextGround, -8192.0f, 8192.0f);
            document.WaterHeights[index] = Math.Clamp(nextWater, -8192.0f, 8192.0f);
        }
    }

    private static float Smooth(TerrainDocument document, Rect2I area, float[] previous, int x, int y, float current, bool ground)
    {
        float sum = 0.0f;
        int count = 0;
        for (int ny = Math.Max(0, y - 1); ny <= Math.Min(document.TileHeight, y + 1); ny++)
        for (int nx = Math.Max(0, x - 1); nx <= Math.Min(document.TileWidth, x + 1); nx++)
        {
            if (nx == x && ny == y) continue;
            bool inArea = nx >= area.Position.X && nx < area.End.X && ny >= area.Position.Y && ny < area.End.Y;
            int local = inArea ? (nx - area.Position.X) * area.Size.Y + (ny - area.Position.Y) : -1;
            sum += local >= 0 ? previous[local] : (ground ? document.GroundHeights : document.WaterHeights)[document.CornerIndex(nx, ny)];
            count++;
        }
        return count == 0 ? current : 0.97f * current + 0.03f * (sum / count);
    }

    private void ApplyCliffs(TerrainDocument document, Rect2I area, Vector2 mouseWorld, TerrainBrush brush)
    {
        Vector2I pos = GetUnclippedPosition(mouseWorld, brush);
        Vector2I offset = new(Math.Min(pos.X, 0), Math.Min(pos.Y, 0));
        var changed = new Queue<Vector2I>();
        for (int y = area.Position.Y; y < area.End.Y; y++)
        for (int x = area.Position.X; x < area.End.X; x++)
        {
            if (!Contains(new Vector2I(x - area.Position.X, y - area.Position.Y) - offset, brush)) continue;
            int index = document.CornerIndex(x, y);
            document.CornerFlags[index] &= unchecked((byte)~TerrainDocument.FlagRamp);
            document.CliffLevels[index] = _layerHeight;
            document.CliffMaterials[index] = (byte)Math.Clamp(brush.CliffMaterialIndex, 0, 255);
            if (brush.Operation == TerrainBrushOperation.ShallowWater)
            {
                document.CornerFlags[index] |= TerrainDocument.FlagWater;
                document.WaterHeights[index] = (document.CliffLevels[index] - 1) * document.CliffStep;
            }
            else if (brush.Operation == TerrainBrushOperation.DeepWater)
            {
                document.CornerFlags[index] |= TerrainDocument.FlagWater;
                document.WaterHeights[index] = document.CliffLevels[index] * document.CliffStep;
            }
            else if ((document.CornerFlags[index] & TerrainDocument.FlagWater) != 0 && document.WaterHeights[index] < document.GetWorldHeight(x, y))
                document.CornerFlags[index] &= unchecked((byte)~TerrainDocument.FlagWater);
            changed.Enqueue(new Vector2I(x, y));
        }
        NormalizeCliffSteps(document, changed, brush.CliffMaterialIndex);
    }

    // Iterative equivalent of HiveWE CliffOperator::check_nearby(); prevents recursion overflow.
    private static void NormalizeCliffSteps(TerrainDocument document, Queue<Vector2I> changed, int cliffMaterial)
    {
        while (changed.Count > 0)
        {
            Vector2I source = changed.Dequeue();
            int sourceIndex = document.CornerIndex(source.X, source.Y);
            for (int y = Math.Max(0, source.Y - 1); y <= Math.Min(document.TileHeight, source.Y + 1); y++)
            for (int x = Math.Max(0, source.X - 1); x <= Math.Min(document.TileWidth, source.X + 1); x++)
            {
                int index = document.CornerIndex(x, y);
                document.CliffMaterials[index] = (byte)Math.Clamp(cliffMaterial, 0, 255);
                int difference = document.CliffLevels[sourceIndex] - document.CliffLevels[index];
                if (Math.Abs(difference) <= 2) continue;
                document.CliffLevels[index] = (byte)Math.Clamp(document.CliffLevels[sourceIndex] - Math.Clamp(difference, -2, 2), 0, 15);
                document.CornerFlags[index] &= unchecked((byte)~TerrainDocument.FlagRamp);
                changed.Enqueue(new Vector2I(x, y));
            }
        }
    }

    private void ApplyRamps(TerrainDocument document, Rect2I area, Vector2 mouseWorld, TerrainBrush brush)
    {
        Vector2I pos = GetUnclippedPosition(mouseWorld, brush);
        Vector2I offset = new(Math.Min(pos.X, 0), Math.Min(pos.Y, 0));
        for (int y = area.Position.Y; y < area.End.Y; y++)
        for (int x = area.Position.X; x < area.End.X; x++)
        {
            if (!Contains(new Vector2I(x - area.Position.X, y - area.Position.Y) - offset, brush)) continue;
            int horizontal = mouseWorld.X > x ? 1 : mouseWorld.X < x ? -1 : 0;
            int vertical = mouseWorld.Y > y ? 1 : mouseWorld.Y < y ? -1 : 0;
            UpdateRamp(document, x, y, horizontal, vertical, brush.CliffMaterialIndex);
        }
    }

    // Directly follows HiveWE's 3-corner horizontal/vertical/diagonal ramp rule.
    private static void UpdateRamp(TerrainDocument d, int x, int y, int horizontal, int vertical, int material)
    {
        if (!Inside(d, x, y)) return;
        int origin = d.CliffLevels[d.CornerIndex(x, y)];
        int target = origin - 1;
        bool Valid(int px, int py) => Inside(d, px, py) && d.CliffLevels[d.CornerIndex(px, py)] == target;
        bool DirectionValid(int dx, int dy)
        {
            if ((dx == 0 && dy == 0) || !Inside(d, x + 2 * dx, y + 2 * dy)) return false;
            for (int step = 1; step <= 2; step++) if (!Valid(x + step * dx, y + step * dy)) return false;
            return Inside(d, x + dy, y + dx) && Inside(d, x - dy, y - dx) &&
                d.CliffLevels[d.CornerIndex(x + dy, y + dx)] <= origin && d.CliffLevels[d.CornerIndex(x - dy, y - dx)] <= origin;
        }
        bool horizontalOk = horizontal != 0 && DirectionValid(horizontal, 0);
        bool verticalOk = vertical != 0 && DirectionValid(0, vertical);
        bool diagonalOk = horizontal != 0 && vertical != 0;
        for (int dx = 0; dx <= 2 && diagonalOk; dx++)
        for (int dy = 0; dy <= 2 && diagonalOk; dy++)
            if ((dx != 0 || dy != 0) && !Valid(x + dx * horizontal, y + dy * vertical)) diagonalOk = false;
        void SetRamp(int px, int py)
        {
            if (!Inside(d, px, py)) return;
            int index = d.CornerIndex(px, py);
            d.CornerFlags[index] |= TerrainDocument.FlagRamp;
            d.CliffMaterials[index] = (byte)Math.Clamp(material, 0, 255);
        }
        if (horizontalOk) for (int step = 0; step <= 2; step++) SetRamp(x + step * horizontal, y);
        if (verticalOk) for (int step = 0; step <= 2; step++) SetRamp(x, y + step * vertical);
        if (diagonalOk) for (int dx = 0; dx <= 2; dx++) for (int dy = 0; dy <= 2; dy++) SetRamp(x + dx * horizontal, y + dy * vertical);
    }

    private byte GetTargetLayer(TerrainDocument document, int center, TerrainBrushOperation operation)
    {
        int layer = document.CliffLevels[center];
        bool water = (document.CornerFlags[center] & TerrainDocument.FlagWater) != 0;
        float finalGround = document.GroundHeights[center] + layer * document.CliffStep;
        switch (operation)
        {
            case TerrainBrushOperation.CliffLowerTwo: layer -= 2; break;
            case TerrainBrushOperation.CliffLowerOne: layer--; break;
            // Warcraft-editor validation rule requested for this panel: these are
            // fixed cliff grades, not repeatable relative increments. Therefore a
            // second click on grade 2 must not turn it into grade 3/4.
            case TerrainBrushOperation.CliffRaiseOne: layer = Math.Max(layer, 1); break;
            case TerrainBrushOperation.CliffRaiseTwo: layer = Math.Max(layer, 2); break;
            case TerrainBrushOperation.ShallowWater:
                if (!water) layer--; else if (document.WaterHeights[center] > finalGround + document.CliffStep) layer++;
                break;
            case TerrainBrushOperation.DeepWater:
                if (!water) layer -= 2; else if (document.WaterHeights[center] < finalGround + document.CliffStep) layer--;
                break;
        }
        return (byte)Math.Clamp(layer, 0, 15);
    }

    private static Rect2I Expand(TerrainDocument document, Rect2I area, int amount)
    {
        int left = Math.Max(0, area.Position.X - amount);
        int top = Math.Max(0, area.Position.Y - amount);
        int right = Math.Min(document.TileWidth, area.End.X - 1 + amount);
        int bottom = Math.Min(document.TileHeight, area.End.Y - 1 + amount);
        return new Rect2I(left, top, right - left + 1, bottom - top + 1);
    }

    private static bool Inside(TerrainDocument document, int x, int y) => x >= 0 && y >= 0 && x <= document.TileWidth && y <= document.TileHeight;
}
