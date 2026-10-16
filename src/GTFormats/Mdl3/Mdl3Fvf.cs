namespace GTFormats.Mdl3;

/// <summary>RSX vertex attribute types (CELL_GCM_VERTEX_*).</summary>
public enum RsxVertexType : byte
{
    S1 = 1,      // normalized signed short
    F = 2,       // float
    SF = 3,      // half float
    UB = 4,      // normalized unsigned byte
    S32K = 5,    // signed short, not normalized
    CMP = 6,     // 11/11/10-bit packed vector in one u32
    UB256 = 7,   // unsigned byte, not normalized
}

/// <summary>One attribute of a flexible vertex format: 8 bytes (u32 name offset, u8 start, u8 count, u8 type, u8 array index).</summary>
public sealed record Mdl3FvfElement(string Name, int Start, int Count, RsxVertexType Type, int ArrayIndex)
{
    /// <summary>Bytes the attribute takes in the vertex.</summary>
    public int ByteSize => Type switch
    {
        RsxVertexType.F => 4 * Count,
        RsxVertexType.S1 or RsxVertexType.SF or RsxVertexType.S32K => 2 * Count,
        RsxVertexType.UB or RsxVertexType.UB256 => Count,
        RsxVertexType.CMP => 4,
        _ => -1,
    };
}

/// <summary>
/// Flexible vertex format record, 0x78 bytes, in the table at header 0x40 (count at 0x18).
/// Layout from Nenkai's PDTools (MIT), checked by `gtdev mdl3-fvf`.
/// </summary>
public sealed class Mdl3Fvf
{
    public const int Size = 0x78;

    public string Name = "";
    public uint Unk04;
    public int VertexSize;              // +19
    public uint ArrayDefOffset;         // +74
    public List<Mdl3FvfElement> Elements = [];

    // Array definition (0x10 bytes at ArrayDefOffset): elements with ArrayIndex > 0 live in extra arrays
    // at vertices + ArrayDataOffset + ArrayElementSize * index, with ArrayVertexSize per vertex.
    public uint ArrayDataOffset, ArrayLength, ArrayElementSize;
    public int ArrayVertexSize, ArrayVertexSizeDefault;

    /// <summary>Byte offset of an element's value for vertex v, relative to the shape's vertex data.</summary>
    public long ElementOffset(Mdl3FvfElement e, int v) => e.ArrayIndex == 0
        ? (long)v * VertexSize + e.Start
        : ArrayDataOffset + (long)ArrayElementSize * e.ArrayIndex + (long)v * ArrayVertexSize + e.Start;

    public static Mdl3Fvf Read(BigEndianReader r, int at)
    {
        var f = new Mdl3Fvf
        {
            Name = r.CString((int)r.U32(at)),
            Unk04 = r.U32(at + 4),
            VertexSize = r.U8(at + 0x19),
            ArrayDefOffset = r.U32(at + 0x74),
        };
        if (f.ArrayDefOffset != 0)
        {
            int a = (int)f.ArrayDefOffset;
            f.ArrayDataOffset = r.U32(a);
            f.ArrayLength = r.U32(a + 4);
            f.ArrayElementSize = r.U32(a + 8);
            f.ArrayVertexSize = r.U8(a + 0x0C);
            f.ArrayVertexSizeDefault = r.U8(a + 0x0D);
        }
        int elementsAt = (int)r.U32(at + 8);
        int count = r.U8(at + 0x18);
        for (int i = 0; i < count; i++)
        {
            int e = elementsAt + i * 8;
            f.Elements.Add(new Mdl3FvfElement(r.CString((int)r.U32(e)), r.U8(e + 4), r.U8(e + 5), (RsxVertexType)r.U8(e + 6), r.U8(e + 7)));
        }
        return f;
    }

    public Mdl3FvfElement? this[string name] => Elements.FirstOrDefault(e => e.Name == name);
}
