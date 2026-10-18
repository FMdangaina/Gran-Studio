using GTFormats;
using GTFormats.Mdl3;

/// <summary>
/// shds-vp &lt;file&gt; &lt;material index&gt;...: research dump of the SHDS vertex program of materials: inputs, the +10
/// table and the constants (0x10 records as words, name).
/// </summary>
static class ShdsVp
{
    public static int Run(string[] args)
    {
        var m = Mdl3File.Load(args[0]);
        var r = m.Data;
        int shds = (int)r.U32(0x4C);
        int defs = (int)r.U32(shds + 0x1C), vps = (int)r.U32(shds + 0x20);
        foreach (var a in args.Skip(1))
        {
            int mi = int.Parse(a);
            var mat = m.Materials[mi];
            var md = m.MaterialData[mat.DataIndex];
            int def = md.ShaderDefinition;
            int vp = r.S16(defs + def * 0x0C + 8);
            int at = vps + vp * 0x28;
            Console.WriteLine($"m{mi} {mat.Name}: def {def} '{r.CString((int)r.U32(defs + def * 0x0C))}' vp {vp} code @{r.U32(at):X} size {r.U32(at + 4)}");
            Console.WriteLine("  vp record: " + string.Join(" ", Enumerable.Range(0, 10).Select(k => r.U32(at + k * 4).ToString("X8"))));
            int n10 = r.U16(at + 8), nin = r.U16(at + 0x0A), ncon = r.U16(at + 0x0C);
            int t10 = (int)r.U32(at + 0x10), tin = (int)r.U32(at + 0x14), tcon = (int)r.U32(at + 0x18);
            string Rec(int p) => string.Join(" ", Enumerable.Range(0, 4).Select(k => r.U32(p + k * 4).ToString("X8"))) + "  " + SafeName(r, (int)r.U32(p));
            for (int i = 0; i < n10; i++) Console.WriteLine($"  t10 {i}: {Rec(t10 + i * 0x10)}");
            for (int i = 0; i < nin; i++) Console.WriteLine($"  in  {i}: {Rec(tin + i * 0x10)}");
            for (int i = 0; i < ncon; i++) Console.WriteLine($"  con {i}: {Rec(tcon + i * 0x10)}");

            // passes of the shader reference: each with its own vertex/fragment program
            int mdAt = (int)r.U32(0xE4 + 0x0C) + mat.DataIndex * 0x28;
            int sref = (int)r.U32(mdAt + 0x14);
            int passes = (int)r.U32(sref + 0x0C), np = r.U16(sref + 0x10);
            for (int p = 0; p < np; p++)
            {
                int pa = passes + p * 0x20;
                int n0 = r.U16(pa), n1 = r.U16(pa + 2), l0 = (int)r.U32(pa + 4), l1 = (int)r.U32(pa + 8);
                int pvp = (int)r.U32(pa + 0x0C), pfp = (int)r.U32(pa + 0x10);
                Console.WriteLine($"  pass {p}: vp @{pvp:X} (index {(pvp - vps) / 0x28}) fp @{pfp:X}  u16s {r.U16(pa + 0x14)} {r.U16(pa + 0x16)} {r.U16(pa + 0x18)} {r.U16(pa + 0x1A)}");
                Console.WriteLine($"     list0: {string.Join(" | ", Enumerable.Range(0, n0).Select(i => $"{r.U32(l0 + i * 8):X8} {r.U32(l0 + i * 8 + 4):X8} {SafeName(r, (int)r.U32(l0 + i * 8))}"))}");
                Console.WriteLine($"     list1: {string.Join(" | ", Enumerable.Range(0, n1).Select(i => $"{r.U32(l1 + i * 8):X8} {r.U32(l1 + i * 8 + 4):X8} {SafeName(r, (int)r.U32(l1 + i * 8))}"))}");
                // VP microcode: the instructions that read input attribute 8 (map12); NV40 VP: d1 bits 8-11 input,
                // 12-21 constant index, 22-26 vector opcode (1 MOV, 2 MUL, 3 ADD, 4 MAD), 27-31 scalar opcode
                {
                    int vcode = (int)r.U32(pvp), vsize = (int)r.U32(pvp + 4);
                    string[] vops = ["NOP", "MOV", "MUL", "ADD", "MAD", "DP3", "DPH", "DP4", "DST", "MIN", "MAX", "SLT", "SGE", "ARL", "FRC", "FLR", "SEQ", "SFL", "SGT", "SLE", "SNE", "STR", "SSG", "op17", "op18", "ARR", "ARA", "TXL"];
                    for (int k = 0; k < vsize; k += 16)
                    {
                        uint[] d = [.. Enumerable.Range(0, 4).Select(w => r.U32(vcode + k + w * 4))];
                        int inp = (int)((d[1] >> 8) & 0xF), con = (int)((d[1] >> 12) & 0x3FF), vop = (int)((d[1] >> 22) & 0x1F);
                        Console.WriteLine($"     vp {k / 16,2}: {string.Join(" ", d.Select(x => x.ToString("X8")))}  vec {(vop < vops.Length ? vops[vop] : vop.ToString())} in {inp} const {con}");
                    }
                }
                int pin = r.U16(pvp + 0x0A), pcon = r.U16(pvp + 0x0C);
                for (int i = 0; i < pin; i++) Console.WriteLine($"     in  {i}: {Rec((int)r.U32(pvp + 0x14) + i * 0x10)}");
                for (int i = 0; i < pcon; i++) Console.WriteLine($"     con {i}: {Rec((int)r.U32(pvp + 0x18) + i * 0x10)}");
                // fragment program record (0x48): +08 u16 count of +10, +0A u8 count of +38, +0B u8 count of +14, +0C u16 count of +18
                Console.WriteLine($"     fp record: {string.Join(" ", Enumerable.Range(0, 18).Select(k => r.U32(pfp + k * 4).ToString("X8")))}");
                int f10 = r.U16(pfp + 8), f38 = r.U8(pfp + 0x0A), f14 = r.U8(pfp + 0x0B), f18 = r.U16(pfp + 0x0C);
                foreach (var (label, n, off) in new[] { ("t10", f10, 0x10), ("smp", f14, 0x14), ("con", f18, 0x18), ("t38", f38, 0x38) })
                    for (int i = 0; i < n; i++) Console.WriteLine($"     fp {label} {i}: {Rec((int)r.U32(pfp + off) + i * 0x10)}");
                // microcode: SHDS +40 records (0x20: u32 = fp +00, u32 offset, u32 size)
                int n40 = r.U16(shds + 0x34), t40 = (int)r.U32(shds + 0x40);
                for (int i = 0; i < n40; i++)
                    if (r.U32(t40 + i * 0x20) == r.U32(pfp))
                    {
                        int code = (int)r.U32(t40 + i * 0x20 + 4), size = (int)r.U32(t40 + i * 0x20 + 8);
                        Console.WriteLine($"     fp microcode @{code:X} size {size}");
                        foreach (var line in RsxFp.Disassemble(r, code, size)) Console.WriteLine("       " + line);
                    }
            }
        }
        return 0;
    }

    static string SafeName(BigEndianReader r, int p)
    {
        try { return p > 0 && p < r.Data.Length ? r.CString(p) : ""; } catch { return ""; }
    }
}
