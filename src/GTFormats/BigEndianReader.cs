using System.Buffers.Binary;
using System.Text;

namespace GTFormats;

/// <summary>Reads big-endian (PS3) values from a byte buffer at absolute offsets.</summary>
public sealed class BigEndianReader
{
    public byte[] Data { get; }

    public BigEndianReader(byte[] data) => Data = data;

    public int Length => Data.Length;

    public byte U8(int offset) => Data[offset];
    public ushort U16(int offset) => BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan(offset));
    public short S16(int offset) => BinaryPrimitives.ReadInt16BigEndian(Data.AsSpan(offset));
    public uint U32(int offset) => BinaryPrimitives.ReadUInt32BigEndian(Data.AsSpan(offset));
    public int S32(int offset) => BinaryPrimitives.ReadInt32BigEndian(Data.AsSpan(offset));
    public float F32(int offset) => BinaryPrimitives.ReadSingleBigEndian(Data.AsSpan(offset));
    public Half F16(int offset) => BinaryPrimitives.ReadHalfBigEndian(Data.AsSpan(offset));

    public string Magic(int offset) => Encoding.ASCII.GetString(Data, offset, 4);

    /// <summary>Null-terminated ASCII string starting at offset.</summary>
    public string CString(int offset)
    {
        int end = Array.IndexOf(Data, (byte)0, offset);
        if (end < 0) end = Data.Length;
        return Encoding.ASCII.GetString(Data, offset, end - offset);
    }
}
