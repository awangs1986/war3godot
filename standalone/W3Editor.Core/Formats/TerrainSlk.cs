using System.Text;

namespace W3Editor.Formats;

/// <summary>One ground tile entry from TerrainArt/Terrain.slk.</summary>
public sealed record TerrainTile(string Id, string Comment)
{
    /// <summary>Tileset letter (first char of the fourcc, e.g. 'L' = Lordaeron Summer).</summary>
    public char Tileset => Id.Length > 0 ? Id[0] : '?';
}

/// <summary>
/// Minimal Terrain.slk catalog — port of addons/w3kit/w3_terrain_slk.gd,
/// extended to keep the human-readable comment column for the TileSetter UI.
/// </summary>
public static class TerrainSlk
{
    /// <summary>Parses Terrain.slk text into tile entries (columns: tileID, comment).</summary>
    public static List<TerrainTile> Parse(string text)
    {
        var tiles = new List<TerrainTile>();
        int idCol = -1, commentCol = -1;
        int curY = -1;
        var row = new Dictionary<int, string>();

        void Commit()
        {
            if (idCol < 0 || !row.TryGetValue(idCol, out var id) || id.Length != 4)
                return;
            row.TryGetValue(commentCol, out var comment);
            tiles.Add(new TerrainTile(id, comment ?? id));
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("C;")) continue;
            int y = SlkInt(line, "Y");
            int x = SlkInt(line, "X");
            string k = SlkStr(line, "K").Trim('"');
            if (y > 0 && y != curY)
            {
                Commit();
                curY = y;
                row.Clear();
            }
            if (x <= 0) continue;
            if (curY == 1)
            {
                if (k == "tileID") idCol = x;
                else if (k == "comment") commentCol = x;
            }
            else if (curY > 1)
            {
                row[x] = k;
            }
        }
        Commit();
        return tiles;
    }

    public static List<TerrainTile>? TryLoad(string assetRoot)
    {
        var path = Path.Combine(assetRoot, "TerrainArt", "Terrain.slk");
        if (!File.Exists(path)) return null;
        try
        {
            return Parse(File.ReadAllText(path, Encoding.UTF8));
        }
        catch
        {
            return null;
        }
    }

    // SLK fields look like ;X14; or ;Y2; — letter immediately followed by digits.
    private static int SlkInt(string line, string key)
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
            return int.Parse(line[j..k]);
        }
    }

    // SLK uses K"text" or K123 — no semicolon after the letter.
    private static string SlkStr(string line, string key)
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
}
