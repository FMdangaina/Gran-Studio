namespace GTFormats.Mdl3;

/// <summary>
/// Render state block, 0xA0 bytes (material +06 index; table at material block +10, count u8 at +05): an RSX command
/// buffer. `0x00780304` = 30 registers from 0x304 (NV40: 0x304 alpha test enable, 0x308 alpha func, 0x30C alpha ref,
/// 0x310 blend enable, 0x314 / 0x318 blend source / destination factors (colour | alpha &lt;&lt; 16), 0x320 blend
/// equation, 0x324 colour mask...), then `0x000C0A6C` = depth func, depth mask (write), depth test enable, then
/// `0x0004183C` = cull face enable. GL enums: 0x302 / 0x303 SRC_ALPHA / ONE_MINUS_SRC_ALPHA, 1 ONE, 0x207 ALWAYS.
/// </summary>
public sealed class Mdl3RenderState
{
    public const int Size = 0xA0;

    public bool AlphaTest, Blend, DepthWrite = true, DepthTest = true, Cull;
    public int AlphaFunc, AlphaRef, SrcFactor, DstFactor;
    public bool Valid;            // the expected command words were found

    public static Mdl3RenderState Read(BigEndianReader r, int at)
    {
        var s = new Mdl3RenderState();
        if (r.U32(at) != 0x00780304) return s;
        uint Reg(int reg) => r.U32(at + 4 + (reg - 0x304));
        s.AlphaTest = Reg(0x304) != 0;
        s.AlphaFunc = (int)Reg(0x308);
        s.AlphaRef = (int)Reg(0x30C);
        s.Blend = Reg(0x310) != 0;
        s.SrcFactor = (int)(Reg(0x314) & 0xFFFF);
        s.DstFactor = (int)(Reg(0x318) & 0xFFFF);
        int d = at + 4 + 30 * 4;
        if (r.U32(d) == 0x000C0A6C)
        {
            s.DepthWrite = r.U32(d + 8) != 0;
            s.DepthTest = r.U32(d + 12) != 0;
            if (r.U32(d + 16) == 0x0004183C) s.Cull = r.U32(d + 20) != 0;
            s.Valid = true;
        }
        return s;
    }

    /// <summary>Blended with the alpha of its texture (glass, lamp covers, decals): SRC_ALPHA / ONE_MINUS_SRC_ALPHA.</summary>
    public bool AlphaBlend => Blend && SrcFactor == 0x302 && DstFactor == 0x303;

    public override string ToString() =>
        $"{(Blend ? $"blend {SrcFactor:X}/{DstFactor:X}" : "opaque")}{(AlphaTest ? $" alpha test {AlphaFunc:X} {AlphaRef}" : "")}{(DepthWrite ? "" : " no depth write")}{(Cull ? " cull" : "")}{(Valid ? "" : " (?)")}";
}
