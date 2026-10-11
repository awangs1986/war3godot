using System.Text.Json;
using W3Editor.Modules.Data;
using W3Editor.Services;

namespace W3Editor.Mcp;

internal static class TriggerIrAccept
{
    public static int Run(string[] args)
    {
        string sourceMap = args.Length >= 2 ? Path.GetFullPath(args[1]) : Path.Combine(GodotLauncher.FindProjectRoot()!, "testdata", "hivewe_sample");
        string jassPath = Path.Combine(sourceMap, "war3map.j");
        string source = File.Exists(jassPath)
            ? File.ReadAllText(jassPath)
            : "function Test takes nothing returns nothing\ncall DoSomething()\nset x = 1\nendfunction\n";
        TriggerIrDocument ir = TriggerIrDocument.ParseJass(source);
        string basic = new BasicTriggerBackend().Generate(ir);
        var workspace = new TriggerWorkspacePart();
        byte[] original = System.Text.Encoding.UTF8.GetBytes(source);
        workspace.Add(EditableTextFileFor("war3map.j", original));
        workspace.Add(EditableTextFileFor("war3map.wtg", new byte[] { 0x00, 0xFF, 0x10 }));
        bool opaque = ir.Functions.SelectMany(f => f.Body).Concat(ir.TopLevel).Any(n => n.Kind == TriggerNodeKind.Opaque);
        var result = new
        {
            sourceMap,
            functions = ir.Functions.Count,
            nodes = ir.Functions.Sum(f => f.Body.Count) + ir.TopLevel.Count,
            opaqueNodesPreserved = opaque,
            basicGenerated = basic.Contains("Sub ", StringComparison.Ordinal) && basic.Contains("End Sub", StringComparison.Ordinal),
            jassOriginalBytes = TextFilesCodec.BytesForAcceptance(workspace.Files["war3map.j"]).SequenceEqual(original),
            wtgOriginalBytes = TextFilesCodec.BytesForAcceptance(workspace.Files["war3map.wtg"]).SequenceEqual(new byte[] { 0x00, 0xFF, 0x10 }),
            pass = ir.Functions.Count > 0 && basic.Length > 0 && opaque &&
                   TextFilesCodec.BytesForAcceptance(workspace.Files["war3map.j"]).SequenceEqual(original) &&
                   TextFilesCodec.BytesForAcceptance(workspace.Files["war3map.wtg"]).SequenceEqual(new byte[] { 0x00, 0xFF, 0x10 }),
        };
        string output = args.Length >= 3 ? Path.GetFullPath(args[2]) : Path.Combine(EditorPaths.OutDir, "trigger-ir-accept.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output);
        return result.pass ? 0 : 1;
    }

    private static EditableTextFile EditableTextFileFor(string name, byte[] bytes) =>
        TextFilesCodec.ParseForAcceptance(name, bytes);
}
