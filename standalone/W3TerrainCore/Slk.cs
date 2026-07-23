using System.Globalization;

namespace Diablo1Like.W3.Terrain;

/// <summary>
/// Minimal SYLK cell scanners shared by Terrain.slk / CliffTypes.slk /
/// Water.slk parsing. Matches addons/w3kit SLK field handling: ;X14 / ;Y2 for
/// integers, K"text" or K123 for values.
/// </summary>
public static class Slk
{
    public static int Int(string line, string key)
    {
        int start = 0;
        while (true)
        {
            int i = line.IndexOf(key, start, StringComparison.Ordinal);
            if (i < 0) return -1;
            if (i > 0 && line[i - 1] != ';') { start = i + 1; continue; }
            int j = i + key.Length;
            if (j >= line.Length || line[j] < '0' || line[j] > '9') { start = i + 1; continue; }
            int k = j;
            while (k < line.Length && line[k] >= '0' && line[k] <= '9') k++;
            return int.Parse(line[j..k], CultureInfo.InvariantCulture);
        }
    }

    public static string Str(string line, string key)
    {
        int start = 0;
        while (true)
        {
            int i = line.IndexOf(key, start, StringComparison.Ordinal);
            if (i < 0) return "";
            int after = i + key.Length;
            if (after >= line.Length) return "";
            char ch = line[after];
            if (i > 0 && line[i - 1] == ';' && (ch == '"' || ch == '-' || (ch >= '0' && ch <= '9')))
                return line[after..].Trim();
            start = i + 1;
        }
    }

    public static float Float(Dictionary<string, string> row, string key, float fallback)
        => row.TryGetValue(key, out string? value)
            && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            ? parsed
            : fallback;
}

/// <summary>One TerrainArt/Terrain.slk row (ground tile id → atlas path).</summary>
public sealed record GroundTileInfo(string Id, string Dir, string File, string Comment)
{
    public string ClassicRelPath => $"{Dir}/{File}.blp";
    /// <summary>HiveWE HD GroundTexture resolver: Reforged terrain diffuse lives beside the classic path.</summary>
    public string ReforgedRelPath => $"{Dir}/{File}_diffuse.dds";
}

/// <summary>One TerrainArt/CliffTypes.slk row.</summary>
public sealed record CliffTypeInfo(string Id, string CliffModelDir, string RampModelDir, string TextureDir, string TextureFile, string GroundTile)
{
    public static readonly CliffTypeInfo Fallback = new("CLdi", "Cliffs", "CliffTrans", "ReplaceableTextures/Cliff", "Cliff0", "Ldrt");
}

public static class TerrainSlkTables
{
    public static Dictionary<string, GroundTileInfo> ParseTerrain(string slkText)
    {
        var tiles = new Dictionary<string, GroundTileInfo>(StringComparer.Ordinal);
        int idX = -1, dirX = -1, fileX = -1, commentX = -1;
        int currentY = -1;
        var row = new Dictionary<int, string>();
        void Commit()
        {
            if (idX < 0 || !row.TryGetValue(idX, out string? id) || id.Length != 4)
                return;
            string dir = row.GetValueOrDefault(dirX, "").Replace('\\', '/');
            string file = row.GetValueOrDefault(fileX, "");
            if (dir.Length == 0 || file.Length == 0)
                return;
            tiles[id] = new GroundTileInfo(id, dir, file, row.GetValueOrDefault(commentX, id));
        }
        foreach (string rawLine in slkText.Split('\n'))
        {
            string line = rawLine.Trim();
            if (!line.StartsWith("C;", StringComparison.Ordinal)) continue;
            int y = Slk.Int(line, "Y");
            int x = Slk.Int(line, "X");
            string value = Slk.Str(line, "K").Trim('"');
            if (y > 0 && y != currentY)
            {
                Commit();
                currentY = y;
                row.Clear();
            }
            if (x <= 0) continue;
            if (currentY == 1)
            {
                switch (value)
                {
                    case "tileID": idX = x; break;
                    case "dir": dirX = x; break;
                    case "file": fileX = x; break;
                    case "comment": commentX = x; break;
                }
            }
            else if (currentY > 1)
                row[x] = value;
        }
        Commit();
        return tiles;
    }

    public static Dictionary<string, CliffTypeInfo> ParseCliffTypes(string slkText)
    {
        var types = new Dictionary<string, CliffTypeInfo>(StringComparer.OrdinalIgnoreCase);
        int idX = -1, modelX = -1, rampX = -1, texDirX = -1, texFileX = -1, groundX = -1;
        int currentY = -1;
        var row = new Dictionary<int, string>();
        void Commit()
        {
            if (idX < 0 || !row.TryGetValue(idX, out string? id) || id.Length == 0)
                return;
            types[id] = new CliffTypeInfo(
                id,
                row.GetValueOrDefault(modelX, "Cliffs"),
                row.GetValueOrDefault(rampX, "CliffTrans"),
                row.GetValueOrDefault(texDirX, "ReplaceableTextures/Cliff").Replace('\\', '/'),
                row.GetValueOrDefault(texFileX, "Cliff0"),
                row.GetValueOrDefault(groundX, ""));
        }
        foreach (string rawLine in slkText.Split('\n'))
        {
            string line = rawLine.Trim();
            if (!line.StartsWith("C;", StringComparison.Ordinal)) continue;
            int y = Slk.Int(line, "Y");
            int x = Slk.Int(line, "X");
            string value = Slk.Str(line, "K").Trim('"');
            if (y > 0 && y != currentY)
            {
                Commit();
                currentY = y;
                row.Clear();
            }
            if (x <= 0) continue;
            if (currentY == 1)
            {
                switch (value)
                {
                    case "cliffID": idX = x; break;
                    case "cliffModelDir": modelX = x; break;
                    case "rampModelDir": rampX = x; break;
                    case "texDir": texDirX = x; break;
                    case "texFile": texFileX = x; break;
                    case "groundTile": groundX = x; break;
                }
            }
            else if (currentY > 1)
                row[x] = value;
        }
        Commit();
        return types;
    }
}
