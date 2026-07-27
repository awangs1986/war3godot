using System.Text;
using System.Text.RegularExpressions;
using W3Editor.Contracts;

namespace W3Editor.Modules.Data;

/// <summary>Language-neutral trigger node kinds. BASIC is the primary future backend; JASS is compatibility.</summary>
public enum TriggerNodeKind { Function, Call, Set, Return, Comment, Opaque }

public sealed record TriggerNode(TriggerNodeKind Kind, string Text, int SourceLine = 0);

public sealed class TriggerFunction
{
    public string Name { get; init; } = "";
    public List<string> Parameters { get; } = new();
    public List<TriggerNode> Body { get; } = new();
}

/// <summary>
/// Stable IR boundary between GUI triggers and script backends. It deliberately
/// keeps unsupported JASS lines as opaque nodes instead of guessing semantics.
/// </summary>
public sealed class TriggerIrDocument
{
    public List<TriggerFunction> Functions { get; } = new();
    public List<TriggerNode> TopLevel { get; } = new();

    public static TriggerIrDocument ParseJass(string source)
    {
        var document = new TriggerIrDocument();
        TriggerFunction? current = null;
        string[] lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.Trim();
            if (trimmed.StartsWith("function ", StringComparison.OrdinalIgnoreCase))
            {
                Match match = Regex.Match(trimmed, @"^function\s+([A-Za-z_][\w]*)\s+takes\s*(.*?)\s+returns\s+(.+)$", RegexOptions.IgnoreCase);
                current = new TriggerFunction { Name = match.Success ? match.Groups[1].Value : trimmed[9..].Trim() };
                if (match.Success && !string.Equals(match.Groups[2].Value, "nothing", StringComparison.OrdinalIgnoreCase))
                    current.Parameters.AddRange(match.Groups[2].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                document.Functions.Add(current);
                continue;
            }
            if (trimmed.Equals("endfunction", StringComparison.OrdinalIgnoreCase)) { current = null; continue; }
            TriggerNode node = Classify(trimmed, i + 1);
            if (current != null) current.Body.Add(node); else document.TopLevel.Add(node);
        }
        return document;
    }

    private static TriggerNode Classify(string line, int sourceLine)
    {
        if (line.Length == 0) return new(TriggerNodeKind.Opaque, line, sourceLine);
        if (line.StartsWith("//", StringComparison.Ordinal)) return new(TriggerNodeKind.Comment, line, sourceLine);
        if (Regex.IsMatch(line, @"^call\s+", RegexOptions.IgnoreCase)) return new(TriggerNodeKind.Call, line, sourceLine);
        if (Regex.IsMatch(line, @"^set\s+", RegexOptions.IgnoreCase)) return new(TriggerNodeKind.Set, line, sourceLine);
        if (line.StartsWith("return", StringComparison.OrdinalIgnoreCase)) return new(TriggerNodeKind.Return, line, sourceLine);
        return new(TriggerNodeKind.Opaque, line, sourceLine);
    }
}

public interface ITriggerScriptBackend
{
    string Language { get; }
    string Generate(TriggerIrDocument document);
}

/// <summary>First BASIC backend slice: safe lowering plus explicit opaque preservation.</summary>
public sealed class BasicTriggerBackend : ITriggerScriptBackend
{
    public string Language => "BASIC";
    public string Generate(TriggerIrDocument document)
    {
        var output = new StringBuilder();
        foreach (TriggerNode node in document.TopLevel)
            EmitNode(output, node, 0);
        foreach (TriggerFunction function in document.Functions)
        {
            output.Append("Sub ").Append(function.Name).Append('(').Append(string.Join(", ", function.Parameters)).AppendLine(")");
            foreach (TriggerNode node in function.Body) EmitNode(output, node, 1);
            output.AppendLine("End Sub");
        }
        return output.ToString();
    }

    private static void EmitNode(StringBuilder output, TriggerNode node, int indent)
    {
        string pad = new(' ', indent * 4);
        switch (node.Kind)
        {
            case TriggerNodeKind.Comment: output.Append(pad).AppendLine(node.Text); break;
            case TriggerNodeKind.Call: output.Append(pad).AppendLine(node.Text[4..].Trim() + "() "); break;
            case TriggerNodeKind.Set: output.Append(pad).AppendLine(node.Text[4..].Trim()); break;
            case TriggerNodeKind.Return: output.Append(pad).AppendLine("Exit Sub"); break;
            case TriggerNodeKind.Opaque: output.Append(pad).Append("' JASS opaque: ").AppendLine(node.Text); break;
        }
    }
}

public sealed class TriggerWorkspacePart : EditableFilesPart
{
    public TriggerWorkspacePart() : base("triggers") { }
    public TriggerIrDocument ParseJass() => Files.TryGetValue("war3map.j", out EditableTextFile? file)
        ? TriggerIrDocument.ParseJass(file.Text) : new TriggerIrDocument();
    public string GenerateBasic() => new BasicTriggerBackend().Generate(ParseJass());
    public bool HasOpaqueJass => ParseJass().Functions.SelectMany(f => f.Body).Concat(ParseJass().TopLevel)
        .Any(node => node.Kind == TriggerNodeKind.Opaque);
}

public sealed class TriggerWorkspaceCodec : IPartCodec
{
    public string PartId => "triggers";
    public int Order => 44;
    public IReadOnlyList<string> FileNames { get; } = new[] { "war3map.wtg", "war3map.wct", "war3map.j", "war3map.lua" };
    public bool CanHandle(W3Editor.Contracts.MapFormatKind format) => format is W3Editor.Contracts.MapFormatKind.War3Folder or W3Editor.Contracts.MapFormatKind.D1MapFolder;
    public W3Editor.Contracts.IDocumentPart? Read(W3Editor.Contracts.IPartReadContext ctx)
    {
        var part = new TriggerWorkspacePart();
        foreach (string name in FileNames)
            if (ctx.ReadFile(name) is { } bytes) part.Add(TextFilesCodec.Parse(name, bytes));
        return part;
    }
    public void Write(W3Editor.Contracts.IDocumentPart part, W3Editor.Contracts.IPartWriteContext ctx)
    {
        if (part is not TriggerWorkspacePart triggers) throw new ArgumentException("Expected trigger workspace.");
        foreach (EditableTextFile file in triggers.Files.Values)
        {
            byte[] bytes = TextFilesCodec.Bytes(file);
            ctx.Emit(file.Name, bytes);
            file.OriginalBytes = bytes;
        }
    }
}
