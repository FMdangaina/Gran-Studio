namespace GTFormats.Mdl3;

/// <summary>
/// MDL3 (ModelSet3) top-level header, big-endian, 0xE4 bytes.
/// Field names start from Nenkai's PDTools (MIT) and are checked by `gtdev mdl3-survey`;
/// names with "unk" are not understood yet.
/// </summary>
public sealed class Mdl3Header
{
    public const string Magic = "MDL3";
    public const int Size = 0xE4;

    public enum Kind { Count16, Offset32, Value16, Value32 }

    public sealed record Field(int Offset, Kind Kind, string Name);

    /// <summary>A table: count field + offset field (+ the count may be absent).</summary>
    public sealed record Table(string Name, int CountOffset, int OffsetOffset);

    public static readonly Field[] Fields =
    [
        new(0x04, Kind.Value32, "fileSize"),
        new(0x08, Kind.Value32, "relocPtr"),
        new(0x0C, Kind.Value16, "version"),
        new(0x0E, Kind.Value16, "runtimeFlags"),
        new(0x10, Kind.Count16, "modelCount"),
        new(0x12, Kind.Count16, "modelKeyCount"),
        new(0x14, Kind.Count16, "shapeCount"),
        new(0x16, Kind.Count16, "shapeKeyCount"),
        new(0x18, Kind.Count16, "fvfCount"),
        new(0x1A, Kind.Count16, "boneCount"),
        new(0x1C, Kind.Value16, "unk1C"),
        new(0x1E, Kind.Count16, "registerValCount"),
        new(0x20, Kind.Value16, "vmStackSize"),
        new(0x22, Kind.Count16, "count22"),
        new(0x24, Kind.Value16, "unk24"),
        new(0x26, Kind.Count16, "count26"),
        new(0x28, Kind.Count16, "count28"),
        new(0x2A, Kind.Count16, "count2A"),
        new(0x2C, Kind.Count16, "count2C"),
        new(0x2E, Kind.Value16, "unk2E"),
        new(0x30, Kind.Offset32, "modelsOffset"),
        new(0x34, Kind.Offset32, "modelKeysOffset"),
        new(0x38, Kind.Offset32, "shapesOffset"),
        new(0x3C, Kind.Offset32, "shapeKeysOffset"),
        new(0x40, Kind.Offset32, "fvfOffset"),
        new(0x44, Kind.Offset32, "materialsOffset"),
        new(0x48, Kind.Offset32, "textureSetOffset"),
        new(0x4C, Kind.Offset32, "shadersOffset"),
        new(0x50, Kind.Offset32, "bonesOffset"),
        new(0x54, Kind.Offset32, "offset54"),
        new(0x58, Kind.Offset32, "registerValOffset"),
        new(0x5C, Kind.Offset32, "offset5C"),
        new(0x60, Kind.Value32, "unk60"),
        new(0x64, Kind.Offset32, "vmBytecodeOffset"),
        new(0x68, Kind.Value32, "vmBytecodeSize"),
        new(0x6C, Kind.Offset32, "vmInstanceOffset"),
        new(0x70, Kind.Value32, "unk70"),
        new(0x74, Kind.Value32, "unk74"),
        new(0x78, Kind.Offset32, "offset78"),
        new(0x7C, Kind.Value32, "unk7C"),
        new(0x80, Kind.Value32, "unk80"),
        new(0x84, Kind.Value32, "unk84"),
        new(0x88, Kind.Offset32, "offset88"),
        new(0x8C, Kind.Count16, "count8C"),
        new(0x8E, Kind.Count16, "textureKeyCount"),
        new(0x90, Kind.Offset32, "textureKeysOffset"),
        new(0x94, Kind.Value32, "unk94"),
        new(0x98, Kind.Count16, "wingDataCount"),
        new(0x9A, Kind.Count16, "wingKeyCount"),
        new(0x9C, Kind.Offset32, "wingDataOffset"),
        new(0xA0, Kind.Offset32, "wingKeysOffset"),
        new(0xA4, Kind.Offset32, "offsetA4"),
        new(0xA8, Kind.Value32, "unkA8"),
        new(0xAC, Kind.Offset32, "shapeStreamMapOffset"),
        new(0xB0, Kind.Offset32, "vmDataOffset"),
        new(0xB4, Kind.Value32, "unkB4"),
        new(0xB8, Kind.Offset32, "vmDataOffset2"),
        new(0xBC, Kind.Offset32, "offsetBC"),
        new(0xC0, Kind.Offset32, "offsetC0"),
        new(0xC4, Kind.Count16, "countC0"),
        new(0xC6, Kind.Value16, "unkC6"),
        new(0xC8, Kind.Value16, "unkC8"),
        new(0xCA, Kind.Count16, "packedMeshKeyCount"),
        new(0xCC, Kind.Offset32, "packedMeshKeysOffset"),
        new(0xD0, Kind.Offset32, "packedMeshHeaderOffset"),
        new(0xD4, Kind.Offset32, "offsetD4"),
        new(0xD8, Kind.Value32, "unkD8"),
        new(0xDC, Kind.Value32, "unkDC"),
        new(0xE0, Kind.Offset32, "carBodyStreamOffset"),
    ];

    /// <summary>Count/offset pairs as named in PDTools; the survey checks each one.</summary>
    public static readonly Table[] Tables =
    [
        new("models", 0x10, 0x30),
        new("modelKeys", 0x12, 0x34),
        new("shapes", 0x14, 0x38),
        new("shapeKeys", 0x16, 0x3C),
        new("fvf", 0x18, 0x40),
        new("bones", 0x1A, 0x50),
        new("registerVals", 0x1E, 0x58),
        new("t5C", 0x22, 0x5C),
        new("t78", 0x26, 0x78),
        new("tA4", 0x28, 0xA4),
        new("t54", 0x2A, 0x54),
        new("t88", 0x2C, 0x88),
        new("t88b", 0x8C, 0x88),
        new("textureKeys", 0x8E, 0x90),
        new("wingData", 0x98, 0x9C),
        new("wingKeys", 0x9A, 0xA0),
        new("tC0", 0xC4, 0xC0),
        new("packedMeshKeys", 0xCA, 0xCC),
    ];

    private readonly BigEndianReader _r;

    public Mdl3Header(BigEndianReader r)
    {
        if (r.Length < Size || r.Magic(0) != Magic)
            throw new InvalidDataException("Not an MDL3 file");
        _r = r;
    }

    public uint Get(Field f) => f.Kind is Kind.Count16 or Kind.Value16 ? _r.U16(f.Offset) : _r.U32(f.Offset);
    public uint Get(int offset) => Get(Fields.First(f => f.Offset == offset));

    public uint FileSize => _r.U32(0x04);
    public ushort Version => _r.U16(0x0C);
}
