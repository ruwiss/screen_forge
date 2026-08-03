using System.IO;
using System.Text;

namespace ScreenForge.Translate;

/// <summary>Minimal protobuf wire encoder (only what Lens request needs).</summary>
internal sealed class ProtoWriter
{
    private readonly MemoryStream _ms = new();

    public byte[] ToArray() => _ms.ToArray();

    public void WriteVarint(ulong value)
    {
        while (value >= 0x80)
        {
            _ms.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        _ms.WriteByte((byte)value);
    }

    public void WriteTag(int fieldNumber, int wireType) => WriteVarint((ulong)((fieldNumber << 3) | wireType));

    public void WriteBytes(int fieldNumber, ReadOnlySpan<byte> data)
    {
        WriteTag(fieldNumber, 2);
        WriteVarint((ulong)data.Length);
        _ms.Write(data);
    }

    public void WriteString(int fieldNumber, string value)
        => WriteBytes(fieldNumber, Encoding.UTF8.GetBytes(value));

    public void WriteMessage(int fieldNumber, Action<ProtoWriter> build)
    {
        var inner = new ProtoWriter();
        build(inner);
        WriteBytes(fieldNumber, inner.ToArray());
    }

    public void WriteUInt64(int fieldNumber, ulong value)
    {
        WriteTag(fieldNumber, 0);
        WriteVarint(value);
    }

    public void WriteInt32(int fieldNumber, int value)
    {
        WriteTag(fieldNumber, 0);
        WriteVarint(unchecked((ulong)value));
    }

    public void WriteEnum(int fieldNumber, int value) => WriteInt32(fieldNumber, value);
}
