using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using W3Editor.Contracts;

namespace W3Editor.Modules.Data;

internal sealed class ProtectionSettings
{
    public bool RemoveEditorFiles { get; set; } = true;
    public bool RemoveMetadata { get; set; } = true;
    public bool MinifyScript { get; set; }
    public bool JunkHeaderOffset { get; set; }
    public bool EncryptImports { get; set; }
    public bool AskEachExport { get; set; } = true;
}

internal static class EditorSettingsStore
{
    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"W3Editor");
    private static string ProtectionPath => Path.Combine(Folder,"protection.json");
    private static string InstallPath => Path.Combine(Folder,"warcraft-install.txt");
    public static ProtectionSettings LoadProtection(){try{return JsonSerializer.Deserialize<ProtectionSettings>(File.ReadAllBytes(ProtectionPath))??new();}catch{return new();}}
    public static void SaveProtection(ProtectionSettings settings){Directory.CreateDirectory(Folder);File.WriteAllBytes(ProtectionPath,JsonSerializer.SerializeToUtf8Bytes(settings,new JsonSerializerOptions{WriteIndented=true}));}
    public static string LoadInstall(){try{return File.ReadAllText(InstallPath).Trim();}catch{return "";}}
    public static void SaveInstall(string path){Directory.CreateDirectory(Folder);File.WriteAllText(InstallPath,path);}
}

internal sealed class ProtectionWindow : Window
{
    private static readonly HashSet<string> EditorOnly=new(StringComparer.OrdinalIgnoreCase){"war3map.doo","war3map.w3c","war3map.w3s","war3map.imp","war3mapUnits.doo","war3map.w3r","war3map.wct","war3map.wtg","conversation.json","terrain_pathing.json"};
    private readonly IModuleContext _ctx;private readonly CheckBox _remove=new(){Content="移除仅编辑器使用的文件"},_metadata=new(){Content="省略 MPQ metadata（文件夹导出时不适用）",IsEnabled=false},_minify=new(){Content="压缩 war3map.j / war3map.lua"},_junk=new(){Content="偏移 MPQ header（文件夹导出时不适用）",IsEnabled=false},_encrypt=new(){Content="加密导入资源（文件夹导出时不适用）",IsEnabled=false},_ask=new(){Content="每次导出前询问"};private readonly TextBlock _status=WindowUi.Hint("");
    public ProtectionWindow(IModuleContext ctx){_ctx=ctx;Title="地图保护";Width=620;Height=500;var root=new StackPanel{Margin=new Thickness(18),Spacing=10};root.Children.Add(WindowUi.Hint("保护只能增加打开难度，不能真正阻止逆向。当前编辑器对文件夹地图提供可验证的安全副本；MPQ metadata、加密和 junk header 会在 W3X 导出器接入后启用。"));foreach(var c in new[]{_remove,_metadata,_minify,_junk,_encrypt,_ask})root.Children.Add(c);root.Children.Add(WindowUi.Button("保存设置",SaveSettings));root.Children.Add(WindowUi.Button("导出受保护文件夹…",Export));root.Children.Add(_status);Content=root;Load();}
    private void Load(){var s=EditorSettingsStore.LoadProtection();_remove.IsChecked=s.RemoveEditorFiles;_metadata.IsChecked=s.RemoveMetadata;_minify.IsChecked=s.MinifyScript;_junk.IsChecked=s.JunkHeaderOffset;_encrypt.IsChecked=s.EncryptImports;_ask.IsChecked=s.AskEachExport;}
    private ProtectionSettings Capture()=>new(){RemoveEditorFiles=_remove.IsChecked==true,RemoveMetadata=_metadata.IsChecked==true,MinifyScript=_minify.IsChecked==true,JunkHeaderOffset=_junk.IsChecked==true,EncryptImports=_encrypt.IsChecked==true,AskEachExport=_ask.IsChecked==true};
    private void SaveSettings(){EditorSettingsStore.SaveProtection(Capture());_status.Text="设置已保存";}
    private async void Export(){if(_ctx.Document is not{}doc){_status.Text="请先打开地图";return;}var picks=await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions{Title="选择受保护副本输出文件夹",AllowMultiple=false});string? output=picks.FirstOrDefault()?.TryGetLocalPath();if(output==null)return;SaveSettings();string root=Path.GetFullPath(doc.Folder);string dest=Path.GetFullPath(output);if(dest.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){_status.Text="输出目录不能位于源地图内部";return;}Directory.CreateDirectory(dest);int copied=0,skipped=0;foreach(string path in Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories)){string rel=Path.GetRelativePath(root,path).Replace('\\','/');if(rel.Contains(".tmp-")||rel.Contains(".bak-")||Path.GetFileName(rel)==".editor-generation.json")continue;if(_remove.IsChecked==true&&(EditorOnly.Contains(rel)||EditorOnly.Contains(Path.GetFileName(rel)))){skipped++;continue;}string target=Path.Combine(dest,rel.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(target)!);if(_minify.IsChecked==true&&(rel.Equals("war3map.j",StringComparison.OrdinalIgnoreCase)||rel.Equals("war3map.lua",StringComparison.OrdinalIgnoreCase)))File.WriteAllText(target,Minify(File.ReadAllText(path),rel.EndsWith(".lua",StringComparison.OrdinalIgnoreCase)),new UTF8Encoding(false));else File.Copy(path,target,true);copied++;}_status.Text=$"已导出 {copied} 个文件，移除 {skipped} 个编辑器文件：{dest}";}
    internal static string Minify(string text,bool lua){var output=new StringBuilder();foreach(string raw in text.Replace("\r\n","\n").Split('\n')){string line=raw.Trim();if(line.Length==0)continue;if(!lua&&line.StartsWith("//"))continue;if(lua&&line.StartsWith("--")&&!line[2..].StartsWith("["))continue;output.AppendLine(line);}return output.ToString();}
}

internal sealed class SwitchWarcraftWindow : Window
{
    private readonly TextBox _path=new(){IsReadOnly=true};private readonly TextBlock _status=WindowUi.Hint("");
    public SwitchWarcraftWindow(){Title="切换 Warcraft 安装";Width=650;Height=280;var root=new StackPanel{Margin=new Thickness(18),Spacing=10};root.Children.Add(WindowUi.Hint("选择 Warcraft III 安装目录。路径保存在编辑器自己的 LocalAppData，不会修改 Godot 的 local_paths.json。切换后重启编辑器生效；CASC 直接读取将在归档导出阶段接入。"));root.Children.Add(_path);root.Children.Add(WindowUi.Button("选择目录…",Choose));root.Children.Add(_status);Content=root;_path.Text=EditorSettingsStore.LoadInstall();}
    private async void Choose(){var picks=await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions{Title="选择 Warcraft III 目录",AllowMultiple=false});string? path=picks.FirstOrDefault()?.TryGetLocalPath();if(path==null)return;bool plausible=File.Exists(Path.Combine(path,"Warcraft III.exe"))||Directory.Exists(Path.Combine(path,"_retail_"))||Directory.Exists(Path.Combine(path,"Data"));if(!plausible){_status.Text="目录中未找到 Warcraft III.exe、_retail_ 或 Data";return;}EditorSettingsStore.SaveInstall(path);_path.Text=path;_status.Text="已保存；重启编辑器后用于 Warcraft 数据源。";}
}
