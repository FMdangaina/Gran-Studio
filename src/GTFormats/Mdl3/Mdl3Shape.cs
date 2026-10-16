namespace GTFormats.Mdl3;

/// <summary>
/// Shape record, 0x30 bytes, in the table at header 0x38 (count at 0x14).
/// One drawable mesh with one material. Layout checked by `gtdev mdl3-records` on all car files.
/// </summary>
public sealed class Mdl3Shape
{
    public const int Size = 0x30;

    public ushort Flags;            // +00
    public short FvfIndex;          // +02 vertex format; -1 (v14) for packed shapes
    public short MaterialIndex;     // +04
    public ushort Unk06;            // +06 always 8 in v8, 0 in v14
    public uint VertexCount;        // +08
    public uint VerticesOffset;     // +0C 0 when the data is elsewhere (packed, or GT6 body_s)
    public uint Unk10;              // +10 always 0
    public uint TriLength;          // +14
    public uint TriOffset;          // +18
    public uint Unk1C;              // +1C always 0
    public uint Unk20;              // +20 v8: 0000FFFF
    public ushort Unk24;            // +24 v8: 0/1; v14: FF00 on packed shapes
    public ushort TriCount;         // +26
    public uint BBoxOffset;         // +28 8 corners (v8); always 0 in v14
    public uint PackedRefOffset;    // +2C packed mesh reference, 0 for plain shapes

    public static Mdl3Shape Read(BigEndianReader r, int at) => new()
    {
        Flags = r.U16(at),
        FvfIndex = r.S16(at + 0x02),
        MaterialIndex = r.S16(at + 0x04),
        Unk06 = r.U16(at + 0x06),
        VertexCount = r.U32(at + 0x08),
        VerticesOffset = r.U32(at + 0x0C),
        Unk10 = r.U32(at + 0x10),
        TriLength = r.U32(at + 0x14),
        TriOffset = r.U32(at + 0x18),
        Unk1C = r.U32(at + 0x1C),
        Unk20 = r.U32(at + 0x20),
        Unk24 = r.U16(at + 0x24),
        TriCount = r.U16(at + 0x26),
        BBoxOffset = r.U32(at + 0x28),
        PackedRefOffset = r.U32(at + 0x2C),
    };
}
