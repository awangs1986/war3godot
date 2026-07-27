using System.Text;
using System.Text.Json;
using W3Editor.Kernel;
using W3Editor.Modules.Data;
using W3Editor.Services;

namespace W3Editor.Mcp;

internal static class EditorDataAccept
{
    public static int Run(string[] args)
    {
        string root=GodotLauncher.FindProjectRoot()??throw new InvalidOperationException("project root not found");
        string map=args.Length>=2?Path.GetFullPath(args[1]):Path.Combine(root,"out","tierb-data-accept");
        string report=args.Length>=3?Path.GetFullPath(args[2]):Path.Combine(root,"out","editor-data-accept.json");
        var checks=new Dictionary<string,bool>();

        var sounds=new SoundsPart{Version=3};
        sounds.Sounds.Add(new SoundRecord{Name="AcceptSound",File="Sound\\Interface\\Warning.wav",Flags=3,Volume=100,Pitch=1.25f});
        sounds.MarkDirty();byte[] soundBytes=SoundBinary.Write(sounds);SoundsPart parsed=SoundBinary.Read(soundBytes);
        checks["w3sSemanticEdit"]=parsed.Version==3&&parsed.Sounds.Count==1&&parsed.Sounds[0].Flags==3&&parsed.Sounds[0].Pitch==1.25f;
        parsed.OriginalBytes=soundBytes;parsed.ClearDirty();checks["w3sRepeatedSaveExact"]=soundBytes.SequenceEqual(SoundBinary.Write(parsed));
        var delta=new SoundsDelta(parsed);parsed.Sounds[0].Volume=7;parsed.MarkDirty();delta.Apply(parsed);checks["w3sUndo"]=parsed.Sounds[0].Volume==100;

        byte[] imp=File.Exists(Path.Combine(map,"war3map.imp"))?File.ReadAllBytes(Path.Combine(map,"war3map.imp")):MakeImp();
        ImportsPart imports=ImportBinary.Read(imp);checks["impUntouchedExact"]=imp.SequenceEqual(ImportBinary.Write(imports));
        imports.Entries.Add(new ImportEntry(13,"war3mapImported\\accept.dat"));imports.MarkDirty();
        checks["impSemanticEdit"]=ImportBinary.Read(ImportBinary.Write(imports)).Entries.Any(e=>e.Path.EndsWith("accept.dat"));

        string mixed="// comment\r\nglobals\r\nendglobals\r\n";byte[] textBytes=Encoding.UTF8.GetBytes(mixed);
        EditableTextFile textFile=TextFilesCodec.ParseForAcceptance("war3map.j",textBytes);
        checks["textUntouchedExact"]=textBytes.SequenceEqual(TextFilesCodec.BytesForAcceptance(textFile));
        textFile.Text+="function x takes nothing returns nothing\r\nendfunction\r\n";textFile.Dirty=true;
        checks["textEditable"]=Encoding.UTF8.GetString(TextFilesCodec.BytesForAcceptance(textFile)).Contains("function x");

        ModulesFile manifest=ModuleHost.ReadManifest(Path.Combine(root,"standalone","modules.json"));
        checks["noSkeletonManifest"]=manifest.Modules.All(m=>!m.Tier.Equals("Skeleton",StringComparison.OrdinalIgnoreCase));
        bool pass=checks.Values.All(x=>x);
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        File.WriteAllText(report,JsonSerializer.Serialize(new{map,checks,pass},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(report);return pass?0:1;
    }
    private static byte[] MakeImp(){using var s=new MemoryStream();using var w=new BinaryWriter(s);w.Write(1u);w.Write(1u);w.Write((byte)13);w.Write(Encoding.UTF8.GetBytes("war3mapImported\\x.dat"));w.Write((byte)0);return s.ToArray();}
}
