using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;
using W3Compat.Mdx;
using W3Editor.Services;

namespace W3Editor.Mcp;

internal static class ModelDiagnosticsAccept
{
    public static int Run(string[] args)
    {
        byte[] bytes = BuildFixture();
        MdxModel model = MdxModel.LoadBytes(bytes) ?? throw new InvalidDataException("fixture did not parse");
        MdxResourceReport report = MdxResourceDiagnostics.Analyze(model, path => path.Equals("Textures/exists.blp", StringComparison.OrdinalIgnoreCase));
        var result = new
        {
            version = report.Version,
            sequences = report.Sequences,
            standSequences = report.StandSequences,
            referencedTextures = report.ReferencedTextures,
            missingTextures = report.MissingTextures,
            hasStand = report.HasStand,
            detectsMissingTexture = report.MissingTextures.Contains("Textures/missing.blp"),
            pass = report.HasStand && report.MissingTextures.Contains("Textures/missing.blp") && report.Sequences.Count == 2,
        };
        string output = args.Length >= 2 ? Path.GetFullPath(args[1]) : Path.Combine(EditorPaths.OutDir, "model-diagnostics-accept.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output);
        return result.pass ? 0 : 1;
    }

    private static byte[] BuildFixture()
    {
        using var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes("MDLX"));
        WriteChunk(stream, "VERS", BitConverter.GetBytes(800));
        WriteChunk(stream, "SEQS", Sequence("Stand", 0, 1000).Concat(Sequence("Attack", 1000, 2000)).ToArray());
        byte[] textures = new byte[536];
        BinaryPrimitives.WriteInt32LittleEndian(textures, 0);
        Encoding.ASCII.GetBytes("Textures/exists.blp").CopyTo(textures, 4);
        BinaryPrimitives.WriteInt32LittleEndian(textures.AsSpan(268), 0);
        Encoding.ASCII.GetBytes("Textures/missing.blp").CopyTo(textures, 272);
        WriteChunk(stream, "TEXS", textures);
        return stream.ToArray();
    }

    private static byte[] Sequence(string name, int start, int end)
    {
        byte[] bytes = new byte[132];
        Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(80), start);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(84), end);
        return bytes;
    }

    private static void WriteChunk(Stream stream, string tag, byte[] payload)
    {
        stream.Write(Encoding.ASCII.GetBytes(tag));
        stream.Write(BitConverter.GetBytes(payload.Length));
        stream.Write(payload);
    }
}
