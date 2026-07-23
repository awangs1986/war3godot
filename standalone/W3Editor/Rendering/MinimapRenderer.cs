using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using W3Editor.Formats;

namespace W3Editor.Rendering;

/// <summary>Optional overlay layers composited on top of the base terrain preview.</summary>
public sealed class OverlayOptions
{
    public W3PathingMap? Pathing;
    public bool ShowNoWalk;
    public bool ShowNoFly;
    public bool ShowNoBuild;
    public IReadOnlyList<W3Placement>? Doodads;
    public IReadOnlyList<W3Placement>? Units;
    public bool ShowDoodads;
    public bool ShowUnits;
}

/// <summary>
/// Top-down preview bitmap generated straight from w3e corner data.
/// One pixel per corner: ground texture base color, height shading,
/// cliff layer lightening, water overlay, boundary darkening.
/// Overlays: wpm pathing tint + doodad/unit dots.
/// </summary>
public static class MinimapRenderer
{
    // Representative colors for common ground tileset fourcc codes.
    private static readonly Dictionary<string, uint> TileColors = new()
    {
        // Lordaeron Summer
        ["Ldrt"] = 0xFF9A6B3F, ["Ldro"] = 0xFF8A6A4A, ["Ldrg"] = 0xFF6E7B3C,
        ["Lrok"] = 0xFF8A7A72, ["Lgrs"] = 0xFF2E8B2E, ["Lgrd"] = 0xFF3E7B34,
        // Lordaeron Fall / Winter
        ["Fdrt"] = 0xFF8F6C42, ["Fdro"] = 0xFF836546, ["Fdrg"] = 0xFF7C7B40,
        ["Frok"] = 0xFF87786F, ["Fgrs"] = 0xFF7F8F35, ["Fgrd"] = 0xFF6F8038,
        ["Wdrt"] = 0xFF8C7A64, ["Wdro"] = 0xFF80705C, ["Wsng"] = 0xFFE8E8F0,
        ["Wsnw"] = 0xFFF2F2FA, ["Wrok"] = 0xFF8D8288, ["Wgrs"] = 0xFF5F7548,
        // Barrens
        ["Bdrt"] = 0xFFA5793F, ["Bdrh"] = 0xFF9C7038, ["Bdrr"] = 0xFF8E6832,
        ["Bdrg"] = 0xFF7E7038, ["Bdsr"] = 0xFFB08850, ["Bflr"] = 0xFF97744B,
        ["Bgrr"] = 0xFF6D7C3B, ["Bgrs"] = 0xFF5C7A33,
        // Ashenvale
        ["Adrt"] = 0xFF6E5A40, ["Adrd"] = 0xFF63523C, ["Agrs"] = 0xFF33622D,
        ["Agrd"] = 0xFF2C5A2B, ["Arck"] = 0xFF6C6660, ["Avin"] = 0xFF4A6631,
        ["Alvd"] = 0xFF3F5E2E,
        // Village / Cityscape 常见地块
        ["Vdrt"] = 0xFF94713F, ["Vdrr"] = 0xFF87683C, ["Vgrs"] = 0xFF4A7A31,
        ["Vgrt"] = 0xFF52783A, ["Vcrp"] = 0xFF9C8A42, ["Vcbp"] = 0xFF8E8072,
        ["Vstp"] = 0xFF9A9186,
    };

    /// <summary>Representative ARGB color for a ground tile fourcc (UI chips).</summary>
    public static uint GetTileColor(string fourcc) =>
        TileColors.TryGetValue(fourcc, out var c) ? c : HashColor(fourcc);

    public static WriteableBitmap Render(W3EMap map, int scale = 2, OverlayOptions? overlays = null)
    {
        scale = Math.Clamp(scale, 1, 8);
        int w = map.Width * scale;
        int h = map.Height * scale;
        var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Opaque);

        // Normalize height shading against the map's own range.
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var c in map.Corners)
        {
            float y = c.WorldY();
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }
        float range = Math.Max(1f, maxY - minY);

        using var fb = bmp.Lock();
        unsafe
        {
            var px = (uint*)fb.Address;
            int stride = fb.RowBytes / 4;

            for (int cy = 0; cy < map.Height; cy++)
            {
                for (int cx = 0; cx < map.Width; cx++)
                {
                    var c = map.Corners[cy * map.Width + cx];
                    uint argb = CornerColor(map, c, minY, range);
                    // w3e rows run south→north; bitmap rows run top→bottom.
                    int py0 = (map.Height - 1 - cy) * scale;
                    int px0 = cx * scale;
                    for (int dy = 0; dy < scale; dy++)
                        for (int dx = 0; dx < scale; dx++)
                            px[(py0 + dy) * stride + px0 + dx] = argb;
                }
            }

            if (overlays != null)
                DrawOverlays(map, overlays, px, stride, w, h, scale);
        }
        return bmp;
    }

    private static unsafe void DrawOverlays(W3EMap map, OverlayOptions o, uint* px, int stride, int w, int h, int scale)
    {
        // ---- wpm pathing tint (cell = 32 world units = scale/4 pixels) ----
        var grid = o.Pathing;
        if (grid != null && (o.ShowNoWalk || o.ShowNoFly || o.ShowNoBuild))
        {
            float cellPx = scale / 4f;
            for (int gy = 0; gy < grid.Height; gy++)
            {
                for (int gx = 0; gx < grid.Width; gx++)
                {
                    int v = grid.Cells[gy * grid.Width + gx];
                    uint tint = 0;
                    // Priority: walk > build > fly (walk is what gameplay feels).
                    if (o.ShowNoWalk && (v & W3PathingMap.Unwalkable) != 0) tint = 0xFFE0483C;
                    else if (o.ShowNoBuild && (v & W3PathingMap.Unbuildable) != 0) tint = 0xFFE8B84B;
                    else if (o.ShowNoFly && (v & W3PathingMap.Unflyable) != 0) tint = 0xFF4C8FD6;
                    if (tint == 0) continue;

                    // wpm rows also run south→north.
                    int x0 = (int)(gx * cellPx);
                    int x1 = Math.Max(x0 + 1, (int)((gx + 1) * cellPx));
                    int yTop = (int)(h - (gy + 1) * cellPx);
                    int y1 = Math.Max(yTop + 1, (int)(h - gy * cellPx));
                    for (int y = Math.Max(0, yTop); y < Math.Min(h, y1); y++)
                        for (int x = Math.Max(0, x0); x < Math.Min(w, x1); x++)
                            px[y * stride + x] = Blend(px[y * stride + x], tint, 0.45f);
                }
            }
        }

        // ---- placement dots ----
        if (o.ShowDoodads && o.Doodads != null)
        {
            int r = Math.Max(1, scale / 4);
            foreach (var p in o.Doodads)
                DrawDot(map, p, 0xFF9BE07A, r, px, stride, w, h, scale);
        }
        if (o.ShowUnits && o.Units != null)
        {
            int r = Math.Max(1, scale / 2);
            foreach (var p in o.Units)
            {
                uint color = p.IsStartLocation ? 0xFFFFD75E
                    : p.PlayerOwner >= 24 ? 0xFFE0483C   // neutral hostile/passive
                    : 0xFF6FB7FF;                        // player-owned
                DrawDot(map, p, color, p.IsStartLocation ? r + 2 : r, px, stride, w, h, scale);
            }
        }
    }

    private static unsafe void DrawDot(W3EMap map, W3Placement p, uint color, int radius,
        uint* px, int stride, int w, int h, int scale)
    {
        // World → corner-grid coords → pixels (flip Y like the base pass).
        float fx = (p.X - map.CenterOffsetX) / W3EMap.TileSize;
        float fy = (p.Y - map.CenterOffsetY) / W3EMap.TileSize;
        int cxPix = (int)(fx * scale) + scale / 2;
        int cyPix = (int)((map.Height - 1 - fy) * scale) + scale / 2;
        if (cxPix < -radius || cyPix < -radius || cxPix >= w + radius || cyPix >= h + radius)
            return;
        int r2 = radius * radius;
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > r2) continue;
                int x = cxPix + dx, y = cyPix + dy;
                if (x < 0 || y < 0 || x >= w || y >= h) continue;
                px[y * stride + x] = color;
            }
        }
    }

    private static uint Blend(uint dst, uint src, float a)
    {
        byte dr = (byte)((dst >> 16) & 0xFF), dg = (byte)((dst >> 8) & 0xFF), db = (byte)(dst & 0xFF);
        byte sr = (byte)((src >> 16) & 0xFF), sg = (byte)((src >> 8) & 0xFF), sb = (byte)(src & 0xFF);
        byte r = (byte)(dr + (sr - dr) * a);
        byte g = (byte)(dg + (sg - dg) * a);
        byte b = (byte)(db + (sb - db) * a);
        return 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
    }

    private static uint CornerColor(W3EMap map, W3Corner c, float minY, float range)
    {
        uint baseColor = 0xFF777777;
        if (c.GroundTexture >= 0 && c.GroundTexture < map.GroundTilesets.Count)
        {
            string id = map.GroundTilesets[c.GroundTexture];
            if (!TileColors.TryGetValue(id, out baseColor))
                baseColor = HashColor(id);
        }

        float shade = 0.72f + 0.28f * ((c.WorldY() - minY) / range);
        // Cliff layers read better with a per-layer step on top of smooth height.
        shade *= 1f + 0.035f * (c.LayerHeight - 2);
        shade = Math.Clamp(shade, 0.45f, 1.25f);

        byte r = (byte)Math.Clamp(((baseColor >> 16) & 0xFF) * shade, 0, 255);
        byte g = (byte)Math.Clamp(((baseColor >> 8) & 0xFF) * shade, 0, 255);
        byte b = (byte)Math.Clamp((baseColor & 0xFF) * shade, 0, 255);

        if (c.Water)
        {
            // Blend towards water blue; deeper water = stronger blend.
            float depth = Math.Clamp((c.WaterHeight - c.GroundHeight - (c.LayerHeight - 2)) * 0.8f + 0.35f, 0.35f, 0.85f);
            r = (byte)(r * (1 - depth) + 0x2B * depth);
            g = (byte)(g * (1 - depth) + 0x59 * depth);
            b = (byte)(b * (1 - depth) + 0x9E * depth);
        }
        if (c.Boundary || c.MapEdge)
        {
            r = (byte)(r * 0.45f); g = (byte)(g * 0.45f); b = (byte)(b * 0.45f);
        }
        if (c.Blight)
        {
            r = (byte)Math.Min(255, r * 0.8f + 40); g = (byte)(g * 0.55f); b = (byte)Math.Min(255, b * 0.8f + 30);
        }
        return 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
    }

    private static uint HashColor(string id)
    {
        unchecked
        {
            uint hsh = 2166136261;
            foreach (char ch in id) { hsh ^= ch; hsh *= 16777619; }
            byte r = (byte)(96 + (hsh & 0x3F));
            byte g = (byte)(96 + ((hsh >> 8) & 0x3F));
            byte b = (byte)(96 + ((hsh >> 16) & 0x3F));
            return 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
        }
    }
}
