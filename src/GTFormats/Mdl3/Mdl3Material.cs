namespace GTFormats.Mdl3;

/// <summary>Sampler key of a material: sampler name -> texture info index, or an external texture (bit 15).</summary>
public sealed record Mdl3SamplerKey(string Sampler, int Index)
{
    /// <summary>Bit 15 set: a texture the game provides at runtime (e.g. the paint colour-shift map), not in the file.</summary>
    public bool External => (Index & 0x8000) != 0;
}

/// <summary>
/// Material record, 0x34 bytes (only +00..+0F used on disk, the rest is runtime space, 0 in every car file).
/// Verified by `gtdev mdl3-matcheck` on all GT5/GT6 car files.
/// </summary>
public sealed class Mdl3Material
{
    public const int Size = 0x34;

    public string Name = "";        // +00 may be empty
    public int DataIndex;           // +04 material data (shader constants + shader reference)
    public int StateIndex;          // +06 RSX render state block (blend/alpha/stencil/depth/cull)
    public ushort Flags;            // +08 GT5 1/3, GT6 1/3/9/B
    public List<Mdl3SamplerKey> Samplers = [];  // +0A count, +0C offset (8-byte keys, sorted by name)

    public static Mdl3Material Read(BigEndianReader r, int at)
    {
        uint name = r.U32(at);
        var m = new Mdl3Material
        {
            Name = name == 0 ? "" : r.CString((int)name),
            DataIndex = r.S16(at + 4),
            StateIndex = r.S16(at + 6),
            Flags = r.U16(at + 8),
        };
        int count = r.U16(at + 0x0A); int keys = (int)r.U32(at + 0x0C);
        for (int i = 0; i < count; i++)
            m.Samplers.Add(new(r.CString((int)r.U32(keys + i * 8)), (int)r.U32(keys + i * 8 + 4)));
        return m;
    }

    public Mdl3SamplerKey? this[string sampler] => Samplers.FirstOrDefault(s => s.Sampler == sampler);
}

/// <summary>
/// Material data record, 0x28 bytes: named shader constants (vec4 slots) and the shader reference.
/// </summary>
public sealed class Mdl3MaterialData
{
    public const int Size = 0x28;

    public string Name = "";        // +00 shader family, e.g. Flex, SimpleFill
    public uint Index;              // +04
    public int ShaderDefinition;    // via +14: shader reference (0x28) -> +04 SHDS definition index
    public Dictionary<string, float[]> Constants = [];   // +0A count, +10 keys (name -> slot), +0C vec4 values

    public static Mdl3MaterialData Read(BigEndianReader r, int at)
    {
        var d = new Mdl3MaterialData
        {
            Name = r.CString((int)r.U32(at)),
            Index = r.U32(at + 4),
            ShaderDefinition = (int)r.U32((int)r.U32(at + 0x14) + 4),
        };
        int count = r.U16(at + 0x0A); int keys = (int)r.U32(at + 0x10); int values = (int)r.U32(at + 0x0C);
        for (int i = 0; i < count; i++)
        {
            int slot = (int)r.U32(keys + i * 8 + 4);
            d.Constants[r.CString((int)r.U32(keys + i * 8))] = [.. Enumerable.Range(0, 4).Select(c => r.F32(values + slot * 16 + c * 4))];
        }
        return d;
    }
}

/// <summary>
/// Texture info, 0x44 bytes: an RSX command buffer that sets one texture unit
/// (0x00201A00: 8 registers from 0x1A00 TEXTURE_OFFSET; 0x00041840: TEXTURE_CONTROL3; v14 adds 0x00040B00: TEXTURE_CONTROL2),
/// then the TXS3 image index (+3A) and the texture name (+40).
/// </summary>
public sealed class Mdl3TextureInfo
{
    public const int Size = 0x44;

    public string Name = "";
    public int Format;              // FORMAT register bits 8-15 (CELL_GCM_TEXTURE_*, with LN/UN flags)
    public int Mipmaps;             // FORMAT register bits 16-23
    public int WrapS, WrapT;        // ADDRESS register bits 0-3 / 8-11: 1 wrap, 2 mirror, 3 clamp to edge, 4 border...
    public int Width, Height;       // IMAGE_RECT
    public uint Remap;              // +14 CONTROL1: 0xAAE4 identity (DXT), 0xAA1B reversed (A8R8G8B8, bytes B,G,R,A)
    public int Pitch;               // +28 CONTROL3 bits 0-19 (row bytes of linear images; depth in bits 20-31)
    public int ImageIndex;          // +3A: TXS3 buffer index

    public static Mdl3TextureInfo Read(BigEndianReader r, int at)
    {
        uint address = r.U32(at + 0x0C);
        return new Mdl3TextureInfo
        {
            Name = r.CString((int)r.U32(at + 0x40)),
            Format = r.U8(at + 0x0A),
            Mipmaps = r.U8(at + 0x09),
            WrapS = (int)(address & 0xF),
            WrapT = (int)((address >> 8) & 0xF),
            Width = r.U16(at + 0x1C),
            Height = r.U16(at + 0x1E),
            Remap = r.U32(at + 0x14),
            Pitch = (int)(r.U32(at + 0x28) & 0xFFFFF),
            ImageIndex = r.U16(at + 0x3A),
        };
    }
}
