using System.Buffers.Binary;
using System.Text;

namespace W3Compat.IO;

/// <summary>基于只读字节跨度的小端二进制读取器（对齐 GDS w3_binary_reader 能力）。</summary>
public ref struct BinarySpanReader
{
    private readonly ReadOnlySpan<byte> _data;

    /// <summary>使用指定字节创建读取器。</summary>
    public BinarySpanReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        Position = 0;
        Failed = false;
        ErrorMessage = "";
    }

    /// <summary>当前读取位置。</summary>
    public int Position { get; private set; }

    /// <summary>是否发生过越界读取。</summary>
    public bool Failed { get; private set; }

    /// <summary>首次失败时的错误信息。</summary>
    public string ErrorMessage { get; private set; }

    /// <summary>尚未读取的字节数。</summary>
    public int Remaining => Math.Max(0, _data.Length - Position);

    /// <summary>是否还能读取指定字节数。</summary>
    public bool CanRead(int count) =>
        count >= 0 && Position >= 0 && count <= _data.Length - Position;

    /// <summary>跳转到指定位置（钳制到合法范围）。</summary>
    public void Seek(int position) =>
        Position = Math.Clamp(position, 0, _data.Length);

    /// <summary>读取一个无符号 8 位整数。</summary>
    public byte ReadU8()
    {
        if (!Require(1))
            return 0;
        return _data[Position++];
    }

    /// <summary>读取一个小端有符号 16 位整数。</summary>
    public short ReadI16()
    {
        if (!Require(sizeof(short)))
            return 0;
        short value = BinaryPrimitives.ReadInt16LittleEndian(_data[Position..]);
        Position += sizeof(short);
        return value;
    }

    /// <summary>读取一个小端无符号 16 位整数。</summary>
    public ushort ReadU16()
    {
        if (!Require(sizeof(ushort)))
            return 0;
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_data[Position..]);
        Position += sizeof(ushort);
        return value;
    }

    /// <summary>读取一个小端有符号 32 位整数。</summary>
    public int ReadI32()
    {
        if (!Require(sizeof(int)))
            return 0;
        int value = BinaryPrimitives.ReadInt32LittleEndian(_data[Position..]);
        Position += sizeof(int);
        return value;
    }

    /// <summary>读取一个小端无符号 32 位整数。</summary>
    public uint ReadU32()
    {
        if (!Require(sizeof(uint)))
            return 0;
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(_data[Position..]);
        Position += sizeof(uint);
        return value;
    }

    /// <summary>读取一个小端单精度浮点数。</summary>
    public float ReadF32() => BitConverter.Int32BitsToSingle(ReadI32());

    /// <summary>读取指定长度的原始字节；越界时返回空跨度。</summary>
    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        if (!Require(count))
            return ReadOnlySpan<byte>.Empty;
        ReadOnlySpan<byte> value = _data.Slice(Position, count);
        Position += count;
        return value;
    }

    /// <summary>读取四字节原始码，按单字节字符映射为字符串（避免 Unicode 警告）。</summary>
    public string ReadFourCC()
    {
        ReadOnlySpan<byte> bytes = ReadBytes(4);
        if (bytes.Length == 0)
            return string.Empty;
        var sb = new StringBuilder(bytes.Length);
        foreach (byte b in bytes)
            sb.Append((char)b);
        return sb.ToString();
    }

    /// <summary>读取单字节为字符字符串。</summary>
    public string ReadChar() => char.ToString((char)ReadU8());

    /// <summary>读取以 NUL 结尾的 ASCII/字节串（逐字节，不喂 UTF-8 解码器）。</summary>
    public string ReadCString()
    {
        int start = Position;
        while (Position < _data.Length && _data[Position] != 0)
            Position++;
        string value = CStringFromPadded(_data[start..Position]);
        if (Position < _data.Length)
            Position++;
        return value;
    }

    /// <summary>读取固定宽度字段，遇 NUL 停止（MDX name/path）。</summary>
    public string ReadFixedCString(int width) => CStringFromPadded(ReadBytes(width));

    /// <summary>解码填充 C 字符串，不把尾部 NUL 交给 UTF-8 解析器。</summary>
    public static string CStringFromPadded(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return "";
        var sb = new StringBuilder();
        foreach (byte b in data)
        {
            if (b == 0)
                break;
            sb.Append((char)b);
        }
        return sb.ToString().Trim();
    }

    private bool Require(int count)
    {
        if (CanRead(count))
            return true;
        Failed = true;
        if (string.IsNullOrEmpty(ErrorMessage))
            ErrorMessage = $"read past end at {Position}: need {count} byte(s), size={_data.Length}";
        Position = _data.Length;
        return false;
    }
}
