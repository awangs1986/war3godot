using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using W3Editor.Contracts;

namespace W3Editor.Modules.Data;

internal static class WindowUi
{
    public static Button Button(string text,Action action){var b=new Button{Content=text};b.Click+=(_,_)=>action();return b;}
    public static TextBlock Hint(string text)=>new(){Text=text,Foreground=Brushes.Gray,TextWrapping=TextWrapping.Wrap};
}

internal sealed class TextPartWindow : Window
{
    private readonly IModuleContext _ctx; private readonly string _partId; private readonly string[] _names;
    private readonly TabControl _tabs=new(); private readonly TextBlock _status=WindowUi.Hint("");
    public TextPartWindow(IModuleContext ctx,string partId,string[] names)
    { _ctx=ctx;_partId=partId;_names=names;Title=partId;Width=1000;Height=720;Content=Build();Reload();ctx.DocumentChanged+=Changed;Closed+=(_,_)=>ctx.DocumentChanged-=Changed;}
    private Control Build(){var root=new DockPanel{Margin=new Thickness(10)};var bar=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};bar.Children.Add(WindowUi.Button("应用全部",Apply));bar.Children.Add(WindowUi.Button("重新载入",Reload));bar.Children.Add(WindowUi.Button("保存地图",Save));DockPanel.SetDock(bar,Dock.Top);root.Children.Add(bar);DockPanel.SetDock(_status,Dock.Bottom);root.Children.Add(_status);root.Children.Add(_tabs);return root;}
    private void Changed(IMapDocument? _)=>Reload();
    private void Reload(){EditableFilesPart? part=_ctx.Document?.GetPart(_partId) as EditableFilesPart;_tabs.ItemsSource=_names.Select(name=>{string text=part?.Files.GetValueOrDefault(name)?.Text??"";return new TabItem{Header=name,Content=new TextBox{Text=text,AcceptsReturn=true,AcceptsTab=true,TextWrapping=TextWrapping.NoWrap,FontFamily=FontFamily.Parse("Consolas"),Tag=name}};}).ToArray();_status.Text=part==null?"请先打开地图。":$"{_partId} · {part.Files.Count} 个文件";}
    private void Apply(){if(_ctx.Document is not { } doc||doc.GetPart(_partId) is not EditableFilesPart part)return;using IEditTransaction tx=doc.BeginEdit("编辑 "+_partId);tx.RecordBefore(part,new EditableFilesDelta(part));foreach(TabItem tab in _tabs.Items.OfType<TabItem>()){if(tab.Content is not TextBox box||box.Tag is not string name)continue;EditableTextFile file=part.Ensure(name);file.Text=box.Text??"";file.Dirty=true;}tx.Commit();_status.Text="已应用（未保存）";}
    private async void Save(){try{Apply();await _ctx.Capabilities.Get<IEditorShellActions>()!.ExecuteAsync("op.save");_status.Text="已保存";}catch(Exception ex){_status.Text=ex.Message;}}
}

internal sealed class TriggerEditorWindow : Window
{
    private readonly IModuleContext _ctx; private readonly TabControl _tabs=new();private readonly TextBlock _status=WindowUi.Hint("");
    public TriggerEditorWindow(IModuleContext ctx){_ctx=ctx;Title="触发器 / 脚本编辑器";Width=1100;Height=760;Content=Build();Reload();ctx.DocumentChanged+=Changed;Closed+=(_,_)=>ctx.DocumentChanged-=Changed;}
    private Control Build(){var root=new DockPanel{Margin=new Thickness(10)};var bar=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};bar.Children.Add(WindowUi.Button("应用文本",Apply));bar.Children.Add(WindowUi.Button("保存地图",Save));DockPanel.SetDock(bar,Dock.Top);root.Children.Add(bar);DockPanel.SetDock(_status,Dock.Bottom);root.Children.Add(_status);root.Children.Add(_tabs);return root;}
    private void Changed(IMapDocument? _)=>Reload();
    private void Reload(){var items=new List<TabItem>();AddText(items,"triggerstrings","war3map.wts");AddText(items,"triggers","war3map.j");AddText(items,"triggers","war3map.lua");if(_ctx.Document?.GetPart("triggers") is EditableFilesPart t)foreach(string binary in new[]{"war3map.wtg","war3map.wct"})if(t.Files.TryGetValue(binary,out EditableTextFile? f))items.Add(new TabItem{Header=binary+"（只读）",Content=new TextBox{Text=$"二进制文件，保持原始字节。\n大小：{f.OriginalBytes.Length} bytes\n\nGUI Trigger IR 尚未改变这些字节。",IsReadOnly=true,AcceptsReturn=true}});_tabs.ItemsSource=items;_status.Text="JASS/Lua/WTS 可编辑；WTG/WCT 当前只读并 byte-exact 保留。";}
    private void AddText(List<TabItem> items,string partId,string name){if(_ctx.Document?.GetPart(partId) is not EditableFilesPart p)return;string text=p.Files.GetValueOrDefault(name)?.Text??"";items.Add(new TabItem{Header=name,Tag=partId,Content=new TextBox{Text=text,Tag=name,AcceptsReturn=true,AcceptsTab=true,FontFamily=FontFamily.Parse("Consolas")}});}
    private void Apply(){if(_ctx.Document is not{}doc)return;foreach(var group in _tabs.Items.OfType<TabItem>().Where(t=>t.Tag is string).GroupBy(t=>(string)t.Tag!)){if(doc.GetPart(group.Key)is not EditableFilesPart part)continue;using IEditTransaction tx=doc.BeginEdit("编辑 "+group.Key);tx.RecordBefore(part,new EditableFilesDelta(part));foreach(TabItem tab in group)if(tab.Content is TextBox box&&box.Tag is string name){var f=part.Ensure(name);f.Text=box.Text??"";f.Dirty=true;}tx.Commit();}_status.Text="文本已应用（未保存）";}
    private async void Save(){try{Apply();await _ctx.Capabilities.Get<IEditorShellActions>()!.ExecuteAsync("op.save");_status.Text="已保存";}catch(Exception ex){_status.Text=ex.Message;}}
}

internal sealed class SoundsWindow : Window
{
    private readonly IModuleContext _ctx;private readonly ListBox _list=new();private readonly TextBox _name=new(),_file=new(),_eax=new();private readonly NumericUpDown _volume=new(){Minimum=0,Maximum=127},_pitch=new(){Minimum=0,Maximum=10,Increment=.05m};private readonly CheckBox _loop=new(){Content="循环"},_is3d=new(){Content="3D"},_music=new(){Content="音乐"};private readonly TextBlock _status=WindowUi.Hint("");
    public SoundsWindow(IModuleContext ctx){_ctx=ctx;Title="声音编辑器";Width=950;Height=650;Content=Build();Reload();_list.SelectionChanged+=(_,_)=>Select();ctx.DocumentChanged+=Changed;Closed+=(_,_)=>ctx.DocumentChanged-=Changed;}
    private Control Build(){var root=new Grid{Margin=new Thickness(10)};root.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(300)));root.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1,GridUnitType.Star)));var left=new DockPanel();var bar=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};bar.Children.Add(WindowUi.Button("新增",Add));bar.Children.Add(WindowUi.Button("删除",Delete));DockPanel.SetDock(bar,Dock.Top);left.Children.Add(bar);left.Children.Add(_list);root.Children.Add(left);var form=new StackPanel{Spacing=8,Margin=new Thickness(12)};foreach((string label,Control c) in new[]{("名称",(Control)_name),("文件",_file),("EAX",_eax),("音量",_volume),("音高",_pitch)}){form.Children.Add(new TextBlock{Text=label});form.Children.Add(c);}var checks=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12};checks.Children.Add(_loop);checks.Children.Add(_is3d);checks.Children.Add(_music);form.Children.Add(checks);form.Children.Add(WindowUi.Button("应用",Apply));form.Children.Add(WindowUi.Button("保存地图",Save));form.Children.Add(_status);Grid.SetColumn(form,1);root.Children.Add(form);return root;}
    private SoundsPart? Part=>_ctx.Document?.GetPart<SoundsPart>();private void Changed(IMapDocument? _)=>Reload();private void Reload(){_list.ItemsSource=Part?.Sounds.Select((s,i)=>new SoundRow(s,i)).ToArray()??Array.Empty<SoundRow>();_status.Text=Part is{}p?$"w3s v{p.Version} · {p.Sounds.Count} 个声音":"请先打开地图";}
    private void Select(){if(_list.SelectedItem is not SoundRow r)return;var s=r.Sound;_name.Text=s.Name;_file.Text=s.File;_eax.Text=s.EaxEffect;_volume.Value=s.Volume;_pitch.Value=(decimal)s.Pitch;_loop.IsChecked=(s.Flags&1)!=0;_is3d.IsChecked=(s.Flags&2)!=0;_music.IsChecked=(s.Flags&8)!=0;}
    private void Edit(string label,Action<SoundsPart> action){if(_ctx.Document is not{}doc||Part is not{}p)return;using var tx=doc.BeginEdit(label);tx.RecordBefore(p,new SoundsDelta(p));action(p);p.MarkDirty();tx.Commit();Reload();}
    private void Add()=>Edit("新增声音",p=>p.Sounds.Add(new SoundRecord{Name="NewSound",File="Sound\\Interface\\Warning.wav"}));private void Delete(){if(_list.SelectedItem is SoundRow r)Edit("删除声音",p=>p.Sounds.Remove(r.Sound));}
    private void Apply(){if(_list.SelectedItem is not SoundRow r)return;Edit("编辑声音",_=>{var s=r.Sound;s.Name=_name.Text??"";s.File=_file.Text??"";s.EaxEffect=_eax.Text??"";s.Volume=(uint)(_volume.Value??127);s.Pitch=(float)(_pitch.Value??1);s.Flags=(s.Flags&~11u)|(_loop.IsChecked==true?1u:0)|(_is3d.IsChecked==true?2u:0)|(_music.IsChecked==true?8u:0);});}
    private async void Save(){try{await _ctx.Capabilities.Get<IEditorShellActions>()!.ExecuteAsync("op.save");_status.Text="已保存";}catch(Exception ex){_status.Text=ex.Message;}}private sealed record SoundRow(SoundRecord Sound,int Index){public override string ToString()=>$"{Sound.Name} — {Sound.File}";}
}

internal sealed class ImportsWindow : Window
{
    private readonly IModuleContext _ctx;private readonly ListBox _list=new();private readonly TextBlock _status=WindowUi.Hint("");
    public ImportsWindow(IModuleContext ctx){_ctx=ctx;Title="导入管理器";Width=800;Height=600;var root=new DockPanel{Margin=new Thickness(10)};var bar=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};bar.Children.Add(WindowUi.Button("新增路径",Add));bar.Children.Add(WindowUi.Button("删除表项",Delete));bar.Children.Add(WindowUi.Button("保存地图",Save));DockPanel.SetDock(bar,Dock.Top);root.Children.Add(bar);DockPanel.SetDock(_status,Dock.Bottom);root.Children.Add(_status);root.Children.Add(_list);Content=root;Reload();ctx.DocumentChanged+=Changed;Closed+=(_,_)=>ctx.DocumentChanged-=Changed;}
    private ImportsPart? Part=>_ctx.Document?.GetPart<ImportsPart>();private void Changed(IMapDocument? _)=>Reload();private void Reload(){_list.ItemsSource=Part?.Entries.Select((e,i)=>new ImportRow(e,i)).ToArray()??Array.Empty<ImportRow>();_status.Text=Part is{}p?$"imp v{p.Version} · {p.Entries.Count} 项。文件内容仍由地图目录保存。":"请先打开地图";}
    private void Edit(string label,Action<ImportsPart> action){if(_ctx.Document is not{}doc||Part is not{}p)return;using var tx=doc.BeginEdit(label);tx.RecordBefore(p,new ImportsDelta(p));action(p);p.MarkDirty();tx.Commit();Reload();}
    private void Add()=>Edit("新增导入表项",p=>p.Entries.Add(new ImportEntry(13,"war3mapImported\\new_file.dat")));private void Delete(){if(_list.SelectedItem is ImportRow r)Edit("删除导入表项",p=>p.Entries.Remove(r.Entry));}
    private async void Save(){try{await _ctx.Capabilities.Get<IEditorShellActions>()!.ExecuteAsync("op.save");_status.Text="已保存";}catch(Exception ex){_status.Text=ex.Message;}}private sealed record ImportRow(ImportEntry Entry,int Index){public override string ToString()=>$"0x{Entry.Flags:X2}  {Entry.Path}";}
}

internal sealed class ModelInspectorWindow : Window
{
    private readonly IModuleContext _ctx;private readonly TextBlock _summary=new(){TextWrapping=TextWrapping.Wrap};private readonly ListBox _chunks=new();
    public ModelInspectorWindow(IModuleContext ctx){_ctx=ctx;Title="MDX / MDL 模型检查器";Width=900;Height=650;var root=new DockPanel{Margin=new Thickness(10)};var open=WindowUi.Button("打开 MDX/MDL…",Open);DockPanel.SetDock(open,Dock.Top);root.Children.Add(open);DockPanel.SetDock(_summary,Dock.Top);root.Children.Add(_summary);root.Children.Add(_chunks);Content=root;}
    private async void Open(){var files=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{Title="打开模型",AllowMultiple=false,FileTypeFilter=new[]{new FilePickerFileType("Warcraft 模型"){Patterns=new[]{"*.mdx","*.mdl"}}}});string? path=files.FirstOrDefault()?.TryGetLocalPath();if(path==null)return;byte[] bytes=File.ReadAllBytes(path);_summary.Text=$"{path}\n{bytes.Length:N0} bytes";if(Path.GetExtension(path).Equals(".mdx",StringComparison.OrdinalIgnoreCase))_chunks.ItemsSource=ReadChunks(bytes);else _chunks.ItemsSource=File.ReadLines(path).Where(line=>line.Contains("Anim ")||line.Contains("Bitmap")||line.Contains("Geoset")||line.Contains("Bone ")).Take(1000).ToArray();}
    private static string[] ReadChunks(byte[] bytes){var rows=new List<string>();if(bytes.Length<4||Encoding.ASCII.GetString(bytes,0,4)!="MDLX")return new[]{"不是有效 MDLX 文件"};int p=4;while(p+8<=bytes.Length){string tag=Encoding.ASCII.GetString(bytes,p,4);uint size=BitConverter.ToUInt32(bytes,p+4);rows.Add($"{tag}  {size:N0} bytes  @0x{p:X}");long next=(long)p+8+size;if(next>bytes.Length){rows.Add("截断的 chunk");break;}p=(int)next;}return rows.ToArray();}
}
