using System;
using System.Text;
using Godot;
using W3Compat.IO;

namespace Diablo1Like.W3.IO;

/// <summary>Godot 侧二进制读取器，语义对齐旧 w3_binary_reader.gd。</summary>
[GlobalClass]
public partial class W3BinaryReaderBridge : RefCounted
{
    private byte[] _data = Array.Empty<byte>();
    private int _pos;
    private bool _failed;
    private string _errorMessage = "";

    public int Pos
    {
        get => _pos;
        set => _pos = Mathf.Clamp(value, 0, _data.Length);
    }

    public bool Failed => _failed;
    public string ErrorMessage => _errorMessage;

    // GDS 属性名兼容
    public int pos
    {
        get => _pos;
        set => Pos = value;
    }

    public bool failed => _failed;
    public string error_message => _errorMessage;

    public W3BinaryReaderBridge() { }

    public W3BinaryReaderBridge(byte[] data) => SetData(data);

    public void SetData(byte[] data)
    {
        _data = data ?? Array.Empty<byte>();
        _pos = 0;
        _failed = false;
        _errorMessage = "";
    }

    public void set_data(byte[] data) => SetData(data);

    public int Remaining() => Math.Max(0, _data.Length - _pos);
    public int remaining() => Remaining();

    public bool CanRead(int byteCount) =>
        byteCount >= 0 && _pos >= 0 && _pos + byteCount <= _data.Length;

    public bool can_read(int byteCount) => CanRead(byteCount);

    public void Seek(int p) => Pos = p;
    public void seek(int p) => Seek(p);

    public int ReadU8()
    {
        if (!Require(1)) return 0;
        return _data[_pos++];
    }

    public int read_u8() => ReadU8();

    public int ReadI16()
    {
        if (!Require(2)) return 0;
        short v = BitConverter.ToInt16(_data, _pos);
        _pos += 2;
        return v;
    }

    public int read_i16() => ReadI16();

    public int ReadU16()
    {
        if (!Require(2)) return 0;
        ushort v = BitConverter.ToUInt16(_data, _pos);
        _pos += 2;
        return v;
    }

    public int read_u16() => ReadU16();

    public int ReadI32()
    {
        if (!Require(4)) return 0;
        int v = BitConverter.ToInt32(_data, _pos);
        _pos += 4;
        return v;
    }

    public int read_i32() => ReadI32();

    public long ReadU32()
    {
        if (!Require(4)) return 0;
        uint v = BitConverter.ToUInt32(_data, _pos);
        _pos += 4;
        return v;
    }

    public long read_u32() => ReadU32();

    public float ReadF32()
    {
        if (!Require(4)) return 0f;
        float v = BitConverter.ToSingle(_data, _pos);
        _pos += 4;
        return v;
    }

    public float read_f32() => ReadF32();

    public byte[] ReadBytes(int n)
    {
        if (!Require(n)) return Array.Empty<byte>();
        var slice = new byte[n];
        Array.Copy(_data, _pos, slice, 0, n);
        _pos += n;
        return slice;
    }

    public byte[] read_bytes(int n) => ReadBytes(n);

    public string ReadFourCc() => FourCcFromBytes(ReadBytes(4));
    public string read_fourcc() => ReadFourCc();

    public string ReadChar() => char.ToString((char)ReadU8());
    public string read_char() => ReadChar();

    public string ReadCString()
    {
        int start = _pos;
        while (_pos < _data.Length && _data[_pos] != 0)
            _pos++;
        string s = CStringFromPadded(Slice(start, _pos - start));
        if (_pos < _data.Length)
            _pos++;
        return s;
    }

    public string read_cstring() => ReadCString();

    public string ReadFixedCString(int n) => CStringFromPadded(ReadBytes(n));
    public string read_fixed_cstring(int n) => ReadFixedCString(n);

    public static string FourCcFromBytes(byte[] data)
    {
        var sb = new StringBuilder(4);
        int n = Math.Min(4, data?.Length ?? 0);
        for (int i = 0; i < n; i++)
            sb.Append((char)data![i]);
        return sb.ToString();
    }

    public static string fourcc_from_bytes(byte[] data) => FourCcFromBytes(data);

    public static string CStringFromPadded(byte[] data)
    {
        if (data == null || data.Length == 0)
            return "";
        var sb = new StringBuilder();
        foreach (byte b in data)
        {
            if (b == 0) break;
            sb.Append((char)b);
        }
        return sb.ToString().Trim();
    }

    public static string cstring_from_padded(byte[] data) => CStringFromPadded(data);

    /// <summary>供需要 span 语义的核心解析一次性消费。</summary>
    public static BinarySpanReader CreateSpanReader(byte[] data) => new(data);

    private byte[] Slice(int start, int len)
    {
        if (len <= 0) return Array.Empty<byte>();
        var o = new byte[len];
        Array.Copy(_data, start, o, 0, len);
        return o;
    }

    private bool Require(int count)
    {
        if (CanRead(count)) return true;
        _failed = true;
        if (string.IsNullOrEmpty(_errorMessage))
            _errorMessage = $"read past end at {_pos}: need {count} byte(s), size={_data.Length}";
        _pos = _data.Length;
        return false;
    }
}
