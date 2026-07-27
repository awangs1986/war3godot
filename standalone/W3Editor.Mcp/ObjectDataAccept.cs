using System.Text.Json;
using W3Editor.Modules.Objects;

namespace W3Editor.Mcp;

internal static class ObjectDataAccept
{
    public static int Run(string[] args)
    {
        if (args.Length < 2 || !Directory.Exists(args[1]))
            throw new ArgumentException("--object-data-accept <map-folder> [report.json]");
        string folder = Path.GetFullPath(args[1]);
        string reportPath = args.Length >= 3 ? Path.GetFullPath(args[2]) : Path.Combine(folder, "object-data-accept.json");
        var fileReports = new List<object>();
        var part = new ObjectDataPart();
        int parsed = 0;
        int modificationCount = 0;
        bool untouchedExact = true;

        foreach (ObjectKinds.Definition def in ObjectKinds.All)
        foreach (bool skin in new[] { false, true })
        {
            string name = ObjectKinds.FileName(def.Kind, skin);
            string path = Path.Combine(folder, name);
            if (!File.Exists(path)) continue;
            byte[] source = File.ReadAllBytes(path);
            ObjectDataFile file = ObjectModificationBinary.Read(name, source, def.OptionalInts);
            bool exact = source.SequenceEqual(ObjectModificationBinary.Write(file));
            untouchedExact &= exact;
            int count = file.OriginalTable.Concat(file.CustomTable).Sum(x => x.Modifications.Count);
            modificationCount += count;
            parsed++;
            part.AddFile(file);
            fileReports.Add(new
            {
                name, file.Version, bytes = source.Length, exact,
                originals = file.OriginalTable.Count, customs = file.CustomTable.Count,
                modifications = count,
            });
        }

        bool editedSemantic = true;
        bool undoExact = true;
        bool repeatedSaveExact = true;
        ObjectDataFile? editable = part.Files.Values.FirstOrDefault(file =>
            file.OriginalTable.Concat(file.CustomTable).Any(record => record.Modifications.Count > 0));
        if (editable != null)
        {
            ObjectDataSnapshot before = part.Capture();
            ObjectModification mod = editable.OriginalTable.Concat(editable.CustomTable)
                .First(record => record.Modifications.Count > 0).Modifications[0];
            string expected = mod.ValueType switch
            {
                0 => (mod.IntValue + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                1 or 2 => (mod.FloatValue + 0.25f).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                _ => mod.StringValue + "_accept",
            };
            mod.ValueText = expected;
            part.Touch(editable);
            byte[] changedBytes = ObjectModificationBinary.Write(editable);
            ObjectDataFile reparsed = ObjectModificationBinary.Read(
                editable.FileName, changedBytes, editable.OptionalInts);
            editedSemantic = reparsed.OriginalTable.Concat(reparsed.CustomTable)
                .First(record => record.Modifications.Count > 0).Modifications[0].ValueText == expected;
            editable.OriginalBytes = changedBytes.ToArray();
            editable.IsDirty = false;
            repeatedSaveExact = changedBytes.SequenceEqual(ObjectModificationBinary.Write(editable));

            new ObjectDataSnapshotDelta(before).Apply(part);
            undoExact = before.Files.All(pair =>
                part.Files.TryGetValue(pair.Key, out ObjectDataFile? restored)
                && pair.Value.SequenceEqual(ObjectModificationBinary.Write(restored)));
        }

        bool pass = parsed > 0 && untouchedExact && editedSemantic && undoExact && repeatedSaveExact;
        var report = new
        {
            source = folder, parsed, modifications = modificationCount,
            gates = new { untouchedExact, editedSemantic, undoExact, repeatedSaveExact },
            pass, files = fileReports,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(reportPath);
        return pass ? 0 : 1;
    }
}
