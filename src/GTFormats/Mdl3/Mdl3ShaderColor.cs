using System.Numerics;

namespace GTFormats.Mdl3;

/// <summary>
/// The base colour of a GT6 material without a diffuse texture and without colour constants (ROADMAP 12c): Polyphony's
/// compiler bakes the colour into the fragment program as an inline constant. The fragment program of the material's
/// first pass (shader reference -> pass -> SHDS fragment program -> SHDS +40 microcode) is scanned for MOV / MUL / MAD
/// instructions writing .xyz with an inline constant; the first constant in 0..1 that is not black or white is the
/// colour (mirror 0.30 0.32 0.31, glass 0.27 0.30 0.27, chrome 0.56, rubber 0.20). Constants the game fills at run time
/// (light, fog, exposure) are 0 in the file and are skipped. RSX words have their 16-bit halves swapped; NV40 fragment
/// instruction layout from the public nouveau nvfx headers (see tools/gtdev/RsxFp.cs).
/// </summary>
public static class Mdl3ShaderColor
{
    /// <summary>The baked colour, or null when none is found (no fragment program, or only 0 / 1 constants).</summary>
    public static Vector3? Of(Mdl3File m, int material)
    {
        foreach (var (_, value) in Candidates(m, material))
            if (value.X <= 1 && value.Y <= 1 && value.Z <= 1 && value.X >= 0 && value.Y >= 0 && value.Z >= 0 &&
                value != Vector3.Zero && value != Vector3.One)
                return value == new Vector3(1, 0, 1) ? null : value;   // magenta = Maya's lambert2 default (no shader set)
        return null;
    }

    /// <summary>Every MOV / MUL / MAD writing .xyz from an inline constant: instruction index and the constant as read (swizzled).</summary>
    public static List<(int Pc, Vector3 Value)> Candidates(Mdl3File m, int material)
    {
        var list = new List<(int, Vector3)>();
        try
        {
            if (FragmentProgram(m, material) is not var (code, size)) return list;
            var r = m.Data;
            uint W(int at) { uint v = r.U32(at); return (v >> 16) | (v << 16); }
            for (int pc = 0; pc + 16 <= size;)
            {
                uint d0 = W(code + pc);
                int op = (int)((d0 >> 24) & 0x3F);
                bool xyz = ((d0 >> 9) & 7) == 7;
                int constSwizzle = -1;
                for (int s = 1; s <= 3 && op != 0; s++)
                {
                    uint d = W(code + pc + s * 4);
                    if ((d & 3) == 2) { constSwizzle = (int)((d >> 9) & 0xFF); break; }
                }
                pc += 16;
                if (constSwizzle < 0) continue;
                if (pc + 16 > size) break;
                var c = new float[4];
                for (int k = 0; k < 4; k++) c[k] = BitConverter.UInt32BitsToSingle(W(code + pc + k * 4));
                pc += 16;
                if (xyz && op is 1 or 2 or 4)
                    list.Add((pc / 16 - 2, new Vector3(c[constSwizzle & 3], c[(constSwizzle >> 2) & 3], c[(constSwizzle >> 4) & 3])));
            }
        }
        catch { }
        return list;
    }

    /// <summary>Microcode offset and size of the fragment program of the material's first pass, null when not found.</summary>
    public static (int Code, int Size)? FragmentProgram(Mdl3File m, int material)
    {
        var r = m.Data;
        if (material < 0 || material >= m.Materials.Count) return null;
        var mat = m.Materials[material];
        if (mat.DataIndex < 0 || mat.DataIndex >= m.MaterialData.Count) return null;
        int shds = (int)r.U32(0x4C);
        if (shds == 0) return null;
        int mdAt = (int)r.U32(0xE4 + 0x0C) + mat.DataIndex * 0x28;
        int sref = (int)r.U32(mdAt + 0x14);
        if (sref == 0 || r.U16(sref + 0x10) == 0) return null;
        int pass = (int)r.U32(sref + 0x0C);
        int fp = (int)r.U32(pass + 0x10);
        if (fp == 0) return null;
        int n40 = r.U16(shds + 0x34), t40 = (int)r.U32(shds + 0x40);
        for (int i = 0; i < n40; i++)
            if (r.U32(t40 + i * 0x20) == r.U32(fp))
                return ((int)r.U32(t40 + i * 0x20 + 4), (int)r.U32(t40 + i * 0x20 + 8));
        return null;
    }
}
