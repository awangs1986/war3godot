using System.Text;

namespace W3Editor.Formats;

/// <summary>One placed doodad or unit (W3 world coordinates).</summary>
public sealed class W3Placement
{
    public string Id = "";
    public int Variation;
    public float X, Y, Z;          // W3 axes: X east, Y north, Z up
    public float Angle;            // radians
    public float ScaleX = 1, ScaleY = 1, ScaleZ = 1;
    public string SkinId = "";
    public int Flags = 2;          // 2 = normal visibility/solid
    public int PlayerOwner;
    public int Life = 100;
    public bool IsUnit;
    public int CreationNumber;

    /// <summary>
    /// Original on-disk record bytes (fidelity preservation). Fields we do not
    /// model — item drop tables, unit gold/hero stats/abilities — live only
    /// here; writers patch the editable head fields into a copy of this blob.
    /// Null for records created in the editor.
    /// </summary>
    public byte[]? RawRecord;
    /// <summary>
    /// d1map placement extension payload. War3 readers leave it empty; the
    /// native bridge preserves it across editor load/save and undo operations.
    /// </summary>
    public byte[] NativeExt = Array.Empty<byte>();
    /// <summary>
    /// Original index in a unified d1map placement stream. Negative means the
    /// placement was created in the editor and is appended on native save.
    /// </summary>
    public int NativeOrder = -1;

    public bool IsStartLocation => Id == "sloc";

    public W3Placement Clone() => (W3Placement)MemberwiseClone();
}

/// <summary>
/// Parsed war3map.doo / war3mapUnits.doo with enough raw context to write a
/// byte-faithful file back (unmodified records round-trip exactly).
/// </summary>
public sealed class W3DooDocument
{
    public bool IsUnits;
    public int FileVersion = 8;   // HiveWE write_version
    public int Subversion = 11;   // HiveWE write_subversion
    public bool HasSkinIds;
    public List<W3Placement> Placements = new();
    /// <summary>Doodads only: raw "special/terrain doodads" trailer (version + records), re-emitted verbatim.</summary>
    public byte[] SpecialTrailer = Array.Empty<byte>();

    public int NextCreationNumber()
    {
        int max = -1;
        foreach (var p in Placements)
            max = Math.Max(max, p.CreationNumber);
        return max + 1;
    }

    /// <summary>
    /// Converts War3's separate terrain-doodad records into ordinary
    /// engine-facing placements for one-way d1map export. HiveWE stores their
    /// positions as corner-grid coordinates, not world-space floats.
    /// </summary>
    public IReadOnlyList<W3Placement> ReadSpecialPlacements(W3EMap terrain)
    {
        var result = new List<W3Placement>();
        if (IsUnits || SpecialTrailer.Length == 0)
            return result;
        if (SpecialTrailer.Length < 8)
            throw new InvalidDataException("special doodad trailer is truncated");

        using var ms = new MemoryStream(SpecialTrailer, writable: false);
        using var reader = new BinaryReader(ms);
        int version = reader.ReadInt32();
        if (version != 0)
            throw new InvalidDataException(
                $"unsupported special doodad version {version}");
        int count = reader.ReadInt32();
        if (count < 0 || count > (SpecialTrailer.Length - 8) / 16)
            throw new InvalidDataException(
                $"invalid special doodad count {count}");
        if (SpecialTrailer.Length != 8 + count * 16)
            throw new InvalidDataException(
                "special doodad trailer has trailing or truncated bytes");

        for (int i = 0; i < count; i++)
        {
            string id = Encoding.ASCII.GetString(reader.ReadBytes(4));
            int variation = reader.ReadInt32();
            int x = reader.ReadInt32();
            int y = reader.ReadInt32();
            int cx = Math.Clamp(x, 0, terrain.Width - 1);
            int cy = Math.Clamp(y, 0, terrain.Height - 1);
            result.Add(new W3Placement
            {
                Id = id,
                Variation = variation,
                X = terrain.CenterOffsetX + x * W3EMap.TileSize,
                Y = terrain.CenterOffsetY + y * W3EMap.TileSize,
                Z = terrain.Corners[cy * terrain.Width + cx].WorldY(),
                IsUnit = false,
                CreationNumber = int.MinValue + i,
                NativeExt = Encoding.ASCII.GetBytes("W3SD"),
            });
        }
        return result;
    }

    /// <summary>Blank doodads document matching what WE writes for an empty map.</summary>
    public static W3DooDocument CreateBlankDoodads(bool reforged) => new()
    {
        IsUnits = false,
        FileVersion = 8,
        Subversion = 11,
        HasSkinIds = reforged,
        // Special-doodads trailer for an empty map: version 0, zero entries.
        SpecialTrailer = new byte[8],
    };

    public static W3DooDocument CreateBlankUnits(bool reforged) => new()
    {
        IsUnits = true,
        FileVersion = 8,
        Subversion = 11,
        HasSkinIds = reforged,
    };

    public byte[] Write()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("W3do"));
        w.Write(FileVersion);
        w.Write(Subversion);
        w.Write(Placements.Count);
        foreach (var p in Placements)
        {
            if (IsUnits)
                WriteUnit(w, p);
            else
                WriteDoodad(w, p);
        }
        if (!IsUnits)
        {
            // Special/terrain doodads: re-emit verbatim; empty = version 0 + zero entries.
            w.Write(SpecialTrailer.Length >= 8 ? SpecialTrailer : new byte[8]);
        }
        return ms.ToArray();
    }

    private static void WriteFourcc(BinaryWriter w, string id)
    {
        Span<byte> b = stackalloc byte[4];
        for (int i = 0; i < 4; i++)
            b[i] = (byte)(i < id.Length ? id[i] : ' ');
        w.Write(b);
    }

    /// <summary>Head fields shared by both record kinds (fixed offsets from record start).</summary>
    private void WriteHead(BinaryWriter w, W3Placement p)
    {
        WriteFourcc(w, p.Id);
        w.Write(p.Variation);
        w.Write(p.X); w.Write(p.Y); w.Write(p.Z);
        w.Write(p.Angle);
        w.Write(p.ScaleX); w.Write(p.ScaleY); w.Write(p.ScaleZ);
        if (HasSkinIds)
            WriteFourcc(w, string.IsNullOrEmpty(p.SkinId) ? p.Id : p.SkinId);
        w.Write((byte)p.Flags);
    }

    private int HeadSize => 36 + (HasSkinIds ? 4 : 0) + 1; // through flags byte

    private void WriteDoodad(BinaryWriter w, W3Placement p)
    {
        if (p.RawRecord != null)
        {
            // Patch head + trailing creation number into the original bytes.
            var raw = (byte[])p.RawRecord.Clone();
            using var patch = new MemoryStream(raw);
            using var pw = new BinaryWriter(patch);
            WriteHead(pw, p);
            pw.Write((byte)p.Life);
            BitConverter.GetBytes(p.CreationNumber).CopyTo(raw, raw.Length - 4);
            w.Write(raw);
            return;
        }
        WriteHead(w, p);
        w.Write((byte)p.Life);
        if (FileVersion >= 8)
        {
            w.Write(-1); // item table pointer: none
            w.Write(0);  // dropped item sets
        }
        w.Write(p.CreationNumber);
    }

    private void WriteUnit(BinaryWriter w, W3Placement p)
    {
        if (p.RawRecord != null)
        {
            var raw = (byte[])p.RawRecord.Clone();
            using var patch = new MemoryStream(raw);
            using var pw = new BinaryWriter(patch);
            WriteHead(pw, p);
            pw.Write(p.PlayerOwner);
            BitConverter.GetBytes(p.CreationNumber).CopyTo(raw, raw.Length - 4);
            w.Write(raw);
            return;
        }
        // Defaults mirror HiveWE units.ixx `struct Unit` field initializers.
        WriteHead(w, p);
        w.Write(p.PlayerOwner);
        w.Write((byte)0); w.Write((byte)0); // unknown1/2
        w.Write(-1);      // health: default
        w.Write(-1);      // mana: default
        if (Subversion >= 11)
            w.Write(0xFFFF); // item table pointer
        w.Write(0);       // dropped item sets
        w.Write(12500);   // gold (only meaningful for mines)
        w.Write(-1f);     // target acquisition
        w.Write(1);       // level
        if (Subversion >= 11)
        {
            w.Write(0); w.Write(0); w.Write(0); // str/agi/int
        }
        w.Write(0);       // inventory items
        w.Write(0);       // modified abilities
        w.Write(0);       // random type 0
        w.Write(0);       //   level+item class payload
        w.Write(-1);      // custom color
        w.Write(-1);      // waygate
        w.Write(p.CreationNumber);
    }
}

/// <summary>
/// war3map.doo / war3mapUnits.doo reader — byte-level port of
/// addons/w3kit/w3_doo_parser.gd (layout aligned with HiveWE doodads.ixx/units.ixx).
/// Each parsed record keeps its raw byte span for byte-faithful writes.
/// </summary>
public static class W3DooFile
{
    private sealed class Reader
    {
        public readonly byte[] D;
        public int P;
        public Reader(byte[] d) => D = d;
        public int Remaining => D.Length - P;
        public string Fourcc() { var s = Encoding.ASCII.GetString(D, P, 4); P += 4; return s; }
        public int I32() { var v = BitConverter.ToInt32(D, P); P += 4; return v; }
        public float F32() { var v = BitConverter.ToSingle(D, P); P += 4; return v; }
        public byte U8() => D[P++];
        public void Skip(int n) => P += n;
    }

    public static List<W3Placement> ParseDoodads(byte[] data, bool hasSkinIds) =>
        ParseDoodadsDocument(data, hasSkinIds).Placements;

    public static List<W3Placement> ParseUnits(byte[] data, bool hasSkinIds) =>
        ParseUnitsDocument(data, hasSkinIds).Placements;

    public static W3DooDocument ParseDoodadsDocument(byte[] data, bool hasSkinIds)
    {
        var doc = new W3DooDocument { IsUnits = false, HasSkinIds = hasSkinIds, FileVersion = 8, Subversion = 11 };
        if (data.Length < 12) return doc;
        var r = new Reader(data);
        if (r.Fourcc() != "W3do") return doc;
        doc.FileVersion = r.I32();
        doc.Subversion = r.I32();
        int count = Math.Clamp(r.I32(), 0, 100000);
        for (int i = 0; i < count; i++)
        {
            if (r.Remaining < (hasSkinIds ? 46 : 42)) break;
            int recordStart = r.P;
            var p = new W3Placement { IsUnit = false };
            p.Id = r.Fourcc();
            p.Variation = r.I32();
            p.X = r.F32(); p.Y = r.F32(); p.Z = r.F32();
            p.Angle = r.F32();
            p.ScaleX = r.F32(); p.ScaleY = r.F32(); p.ScaleZ = r.F32();
            p.SkinId = hasSkinIds ? r.Fourcc() : p.Id;
            p.Flags = r.U8();
            p.Life = r.U8();
            if (doc.FileVersion >= 8)
            {
                if (r.Remaining < 8) break;
                r.I32(); // item table pointer
                int itemSets = Math.Clamp(r.I32(), 0, 256);
                bool setsOk = true;
                for (int s = 0; s < itemSets && setsOk; s++)
                {
                    if (r.Remaining < 4) { setsOk = false; break; }
                    int n = Math.Clamp(r.I32(), 0, 256);
                    for (int it = 0; it < n; it++)
                    {
                        if (r.Remaining < 8) { setsOk = false; break; }
                        r.Fourcc(); r.I32();
                    }
                }
                if (!setsOk) break;
            }
            if (r.Remaining < 4) break;
            p.CreationNumber = r.I32();
            p.RawRecord = data[recordStart..r.P];
            doc.Placements.Add(p);
        }
        // Special/terrain doodads (HiveWE second section) — keep raw, re-emit verbatim.
        if (r.Remaining >= 4)
            doc.SpecialTrailer = data[r.P..];
        return doc;
    }

    public static W3DooDocument ParseUnitsDocument(byte[] data, bool hasSkinIds)
    {
        var doc = new W3DooDocument { IsUnits = true, HasSkinIds = hasSkinIds, FileVersion = 8, Subversion = 11 };
        if (data.Length < 12) return doc;
        var r = new Reader(data);
        if (r.Fourcc() != "W3do") return doc;
        doc.FileVersion = r.I32();
        doc.Subversion = r.I32();
        int subversion = doc.Subversion;
        int count = Math.Clamp(r.I32(), 0, 100000);
        for (int i = 0; i < count; i++)
        {
            if (r.Remaining < (hasSkinIds ? 54 : 50)) break;
            int recordStart = r.P;
            var p = new W3Placement { IsUnit = true };
            p.Id = r.Fourcc();
            p.Variation = r.I32();
            p.X = r.F32(); p.Y = r.F32(); p.Z = r.F32();
            p.Angle = r.F32();
            p.ScaleX = r.F32(); p.ScaleY = r.F32(); p.ScaleZ = r.F32();
            p.SkinId = hasSkinIds ? r.Fourcc() : p.Id;
            p.Flags = r.U8();
            p.PlayerOwner = r.I32();
            // HiveWE: unknown1 + unknown2 — TWO bytes (classic parse desync trap).
            if (r.Remaining < 2) break;
            r.U8(); r.U8();
            if (r.Remaining < 8) break;
            r.I32(); r.I32(); // health, mana
            if (subversion >= 11)
            {
                if (r.Remaining < 4) break;
                r.I32(); // item table pointer
            }
            if (r.Remaining < 4) break;
            int itemSets = Math.Clamp(r.I32(), 0, 256);
            bool setsOk = true;
            for (int s = 0; s < itemSets && setsOk; s++)
            {
                if (r.Remaining < 4) { setsOk = false; break; }
                int n = Math.Clamp(r.I32(), 0, 256);
                for (int it = 0; it < n; it++)
                {
                    if (r.Remaining < 8) { setsOk = false; break; }
                    r.Fourcc(); r.I32();
                }
            }
            if (!setsOk) break;
            if (r.Remaining < 12) break;
            r.I32(); // gold
            r.F32(); // target acquisition
            r.I32(); // level
            if (subversion >= 11)
            {
                if (r.Remaining < 12) break;
                r.I32(); r.I32(); r.I32(); // str/agi/int
            }
            if (r.Remaining < 4) break;
            int inv = Math.Clamp(r.I32(), 0, 256);
            bool invOk = true;
            for (int k = 0; k < inv; k++)
            {
                if (r.Remaining < 8) { invOk = false; break; }
                r.I32(); r.Fourcc();
            }
            if (!invOk) break;
            if (r.Remaining < 4) break;
            int abil = Math.Clamp(r.I32(), 0, 256);
            bool abilOk = true;
            for (int a = 0; a < abil; a++)
            {
                if (r.Remaining < 12) { abilOk = false; break; }
                r.Fourcc(); r.I32(); r.I32();
            }
            if (!abilOk) break;
            if (r.Remaining < 4) break;
            int randomType = r.I32();
            if (randomType == 0)
            {
                if (r.Remaining < 4) break;
                r.Skip(4);
            }
            else if (randomType == 1)
            {
                if (r.Remaining < 8) break;
                r.Skip(8);
            }
            else if (randomType == 2)
            {
                if (r.Remaining < 4) break;
                int nrand = Math.Clamp(r.I32(), 0, 256);
                if (r.Remaining < nrand * 8) break;
                r.Skip(nrand * 8);
            }
            // other values (e.g. -1): no trailing payload
            if (r.Remaining < 12) break;
            r.I32(); // custom color
            r.I32(); // waygate
            p.CreationNumber = r.I32();
            p.RawRecord = data[recordStart..r.P];
            doc.Placements.Add(p);
        }
        return doc;
    }
}
