using Godot;
using W3Compat.Formats;

namespace Diablo1Like.W3.Formats;

/// <summary>把共享 WPM 模型暴露给 GDScript，字段对齐旧 w3_wpm.gd。</summary>
[GlobalClass]
public partial class W3PathingMapBridge : RefCounted
{
    private W3PathingMap? _map;

    public int Version => _map?.Version ?? 0;
    public int Width => _map?.Width ?? 0;
    public int Height => _map?.Height ?? 0;

    // GDS 字段别名
    public int version => Version;
    public int width => Width;
    public int height => Height;

    /// <summary>与 GDS `pathing` 字段一致：cell 标志字节。</summary>
    public byte[] pathing => GetCells();

    /// <summary>从字节加载；失败返回 null（对齐 GDS static load_bytes）。</summary>
    public static W3PathingMapBridge? LoadBytesStatic(byte[]? data)
    {
        W3PathingMap? map = W3PathingMap.TryLoadBytes(data);
        if (map == null)
        {
            if (data != null && data.Length > 0)
                GD.PushError("W3Wpm: load failed (bad magic or truncated)");
            return null;
        }
        return new W3PathingMapBridge { _map = map };
    }

    public static W3PathingMapBridge? load_bytes(byte[]? data) => LoadBytesStatic(data);

    /// <summary>从磁盘路径加载；失败返回 null。</summary>
    public static W3PathingMapBridge? LoadFileStatic(string path)
    {
        string global = ProjectSettings.GlobalizePath(path);
        if (!System.IO.File.Exists(global))
            return null;
        return LoadBytesStatic(System.IO.File.ReadAllBytes(global));
    }

    public static W3PathingMapBridge? load_file(string path) => LoadFileStatic(path);

    /// <summary>实例方法：加载到本对象。</summary>
    public bool LoadBytes(byte[] data)
    {
        _map = W3PathingMap.TryLoadBytes(data);
        if (_map == null)
        {
            GD.PushError("W3PathingMapBridge: load failed");
            return false;
        }
        return true;
    }

    public bool load_bytes_into(byte[] data) => LoadBytes(data);

    public bool LoadFile(string path)
    {
        string global = ProjectSettings.GlobalizePath(path);
        if (!System.IO.File.Exists(global))
            return false;
        return LoadBytes(System.IO.File.ReadAllBytes(global));
    }

    public bool load_file_into(string path) => LoadFile(path);

    /// <summary>返回 cell 标志字节数组（拷贝）。</summary>
    public byte[] GetCells()
    {
        if (_map == null)
            return System.Array.Empty<byte>();
        var copy = new byte[_map.Cells.Length];
        System.Array.Copy(_map.Cells, copy, copy.Length);
        return copy;
    }

    public byte[] get_cells() => GetCells();

    public int GetCell(int x, int y) => _map?.GetCell(x, y) ?? 0;
    public int get_cell(int x, int y) => GetCell(x, y);
}
