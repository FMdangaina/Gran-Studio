using System.Globalization;
using System.Numerics;

namespace GTFormats.Mdl3;

/// <summary>UV transform of a material: uv' = uv * Scale + Offset.</summary>
public readonly record struct Mdl3UvTransform(Vector2 Scale, Vector2 Offset)
{
    public static readonly Mdl3UvTransform Identity = new(Vector2.One, Vector2.Zero);
    public bool IsIdentity => Scale == Vector2.One && Offset == Vector2.Zero;
    public Vector2 Apply(Vector2 uv) => uv * Scale + Offset;
}

/// <summary>
/// The texture coordinate transform a material's vertex program applies to its UVs (map12 / map1). The first pass of
/// the shader reference has an RSX vertex program (SHDS record: +00 microcode, +04 size, +0A input count, +14 inputs:
/// u32 name, u16, u16 attribute). The instruction that reads the UV attribute is MOV (as stored), MUL (scale), ADD
/// (offset, or the input twice) or MAD (scale and offset). Its constants are carried in the microcode preamble:
/// +10 u32 preamble size, +14 u32 constant count, +18 the constant registers, then 16-byte values. An instruction
/// reads one constant register, each source with its own swizzle (constants are shared: (0.5, 2, 0, 0) with .yyyy
/// = x2). E.g. GT6 Demio emblem: MUL map12, c[465].xxxx, c[465] = (2, 0, 0, 0); the Demio plate: MOV.
/// NV40 vertex instruction (nouveau nvfx): word 1 bits 8-11 input attribute, 12-21 constant register, 22-26 vector
/// opcode (1 MOV, 2 MUL, 3 ADD, 4 MAD); 17-bit sources: src0 = word1 bits 0-7 + word2 bits 23-31, src1 = word2
/// bits 6-22, src2 = word2 bits 0-5 + word3 bits 21-31; source bits 0-1 register type (2 input, 3 constant),
/// 14-15 / 12-13 component for x / y, bit 16 negate.
/// </summary>
public static class Mdl3UvScale
{
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Mdl3File, Dictionary<int, Mdl3UvTransform>> Cache = new();

    /// <summary>The UV transform of a material (identity when the program copies the UVs or cannot be read).</summary>
    public static Mdl3UvTransform Of(Mdl3File m, int material)
    {
        if (material < 0 || material >= m.Materials.Count) return Mdl3UvTransform.Identity;
        var cache = Cache.GetOrCreateValue(m);
        if (cache.TryGetValue(material, out var t)) return t;
        try { t = Read(m, material, out _); } catch { t = Mdl3UvTransform.Identity; }
        return cache[material] = t;
    }

    /// <summary>The transform and how it was found (for surveys).</summary>
    public static Mdl3UvTransform Explain(Mdl3File m, int material, out string how)
    {
        try { return Read(m, material, out how); } catch (Exception e) { how = "error " + e.GetType().Name; return Mdl3UvTransform.Identity; }
    }

    static Mdl3UvTransform Read(Mdl3File m, int material, out string how)
    {
        var id = Mdl3UvTransform.Identity;
        how = "";
        var r = m.Data;
        var mat = m.Materials[material];
        if (mat.DataIndex < 0 || mat.DataIndex >= m.MaterialData.Count) { how = "no material data"; return id; }
        int md = (int)r.U32(0xE4 + 0x0C) + mat.DataIndex * 0x28;
        int sref = (int)r.U32(md + 0x14);
        if (sref == 0 || r.U16(sref + 0x10) == 0) { how = "no pass"; return id; }
        int pass = (int)r.U32(sref + 0x0C);
        int vp = (int)r.U32(pass + 0x0C);
        int inputs = (int)r.U32(vp + 0x14), nin = r.U16(vp + 0x0A), attr = -1;
        for (int i = 0; i < nin; i++)
        {
            string name = r.CString((int)r.U32(inputs + i * 0x10));
            if (name is "map12" or "map1") { attr = r.U16(inputs + i * 0x10 + 6); break; }
        }
        if (attr < 0) { how = "no uv input"; return id; }
        int code = (int)r.U32(vp), size = (int)r.U32(vp + 4);
        int pre = (int)r.U32(code + 0x10), count = (int)r.U32(code + 0x14);
        if (pre < 0x20 || pre > size || count > 64) { how = "preamble not understood"; return id; }
        int values = code + 0x10 + ((8 + 4 * count + 15) & ~15);
        for (int k = pre; k + 16 <= size; k += 16)
        {
            uint w1 = r.U32(code + k + 4), w2 = r.U32(code + k + 8), w3 = r.U32(code + k + 12);
            if (((w1 >> 8) & 0xF) != attr) continue;
            int op = (int)((w1 >> 22) & 0x1F);
            if (op == 1) { how = "mov"; return id; }
            if (op is not (2 or 3 or 4)) { how = $"other op {op}"; return id; }
            int reg = (int)((w1 >> 12) & 0x3FF);
            float[]? c = null;
            for (int q = 0; q < count; q++)
                if (r.U32(code + 0x18 + q * 4) == reg) c = [.. Enumerable.Range(0, 4).Select(e => r.F32(values + q * 16 + e * 4))];
            uint s0 = ((w1 & 0xFF) << 9) | ((w2 >> 23) & 0x1FF), s1 = (w2 >> 6) & 0x1FFFF, s2 = ((w2 & 0x3F) << 11) | ((w3 >> 21) & 0x7FF);
            bool IsConst(uint s) => (s & 3) == 3;
            bool IsInput(uint s) => (s & 3) == 2;
            Vector2 Value(uint s)
            {
                if (c == null) throw new InvalidDataException("constant not in the preamble");
                float sign = (s >> 16 & 1) != 0 ? -1 : 1;
                return new Vector2(c[(s >> 14) & 3], c[(s >> 12) & 3]) * sign;
            }
            Mdl3UvTransform t;
            try
            {
                t = op switch
                {
                    2 => new(IsConst(s1) ? Value(s1) : Value(s0), Vector2.Zero),                                    // MUL
                    3 => IsInput(s0) && IsInput(s1) ? new(new Vector2(2), Vector2.Zero)                            // ADD
                        : new(Vector2.One, IsConst(s1) ? Value(s1) : Value(s0)),
                    _ => new(IsConst(s1) ? Value(s1) : Value(s0), IsConst(s2) ? Value(s2) : Vector2.Zero),         // MAD
                };
            }
            catch (InvalidDataException) { how = $"op {op}: c{reg} not in the preamble"; return id; }
            if (op != 4 && !IsInput(op == 2 && IsConst(s1) ? s0 : op == 2 ? s1 : s0) && op != 3) { how = $"op {op}: uv not a direct source"; return id; }
            string F(Vector2 v) => $"{v.X.ToString(CultureInfo.InvariantCulture)},{v.Y.ToString(CultureInfo.InvariantCulture)}";
            how = $"{(op == 2 ? "mul" : op == 3 ? "add" : "mad")} scale {F(t.Scale)} offset {F(t.Offset)}";
            bool sane = float.IsFinite(t.Scale.X) && float.IsFinite(t.Scale.Y) && Math.Abs(t.Scale.X) < 1000 && Math.Abs(t.Scale.Y) < 1000 && t.Scale != Vector2.Zero;
            return sane ? t : id;
        }
        how = "uv input not read";
        return id;
    }
}
