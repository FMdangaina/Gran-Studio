using System.Globalization;
using System.Numerics;
using GTFormats.Mdl3;

/// <summary>
/// pmsh-probe &lt;file&gt; &lt;mesh&gt;: research on one PMSH mesh (data in the MDL3): packed positions with the PDTools
/// dequantization (raw / (2^(bits-1)-1) + 2) * scale + offset, MSB- and LSB-first, against the mesh bbox; the first
/// raw vertices; the index and command data as hex and as fixed-width values.
/// </summary>
static class PmshProbe
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("pmsh-probe <file> <mesh>"); return 1; }
        var m = Mdl3File.Load(args[0]);
        var r = m.Data;
        var p = Mdl3Pmsh.Read(r)!;
        var me = p.Meshes[int.Parse(args[1])];
        var d = me.Data[0];
        var decl = p.Declarations[me.Declaration];
        var layouts = p.Layouts[me.LayoutArray];
        var ci = CultureInfo.InvariantCulture;
        int V = d.VertexCount;
        Console.WriteLine($"mesh {args[1]}: V {V} out {d.OutputVertexCount} idxQ {d.IndexQuadwords} idxBits {d.IndexBits} cmdQ {d.CommandQuadwords} flags {d.Flags:X}");
        if (me.BBoxes.Count > 0)
        {
            var b = me.BBoxes[0];
            Console.WriteLine(string.Create(ci, $"bbox min {b.Aggregate(Vector3.Min)} max {b.Aggregate(Vector3.Max)}"));
        }
        int at = (int)d.PackedVertices;
        for (int li = 0; li < layouts.Count && li < decl.Packed.Count; li++)
        {
            var l = layouts[li];
            foreach (bool msb in new[] { true, false })
            {
                var lo = new Vector4(float.MaxValue); var hi = new Vector4(float.MinValue);
                for (int v = 0; v < V; v++)
                {
                    var q = Read(r.Data, at, v * l.TotalBits, l, msb);
                    lo = Vector4.Min(lo, q); hi = Vector4.Max(hi, q);
                }
                Console.WriteLine(string.Create(ci, $"  layout {li} ({decl.Packed[li].Name}) {(msb ? "MSB" : "LSB")}: min {lo} max {hi}"));
                if (msb) for (int v = 0; v < 4; v++) Console.WriteLine(string.Create(ci, $"     v{v} {Read(r.Data, at, v * l.TotalBits, l, msb)}"));
            }
            at += (V * l.TotalBits + 7) / 8;
        }
        Console.WriteLine($"raw vertices @{d.RawVertices:X} stride {decl.RawStride}: " + string.Join(" | ", Enumerable.Range(0, 3).Select(v =>
            string.Join(' ', Enumerable.Range(0, decl.RawStride).Select(k => r.U8((int)d.RawVertices + v * decl.RawStride + k).ToString("X2"))))));
        Dump("indices", r, (int)d.Indices, d.IndexQuadwords * 16, d.IndexBits, V);
        Dump("commands", r, (int)d.Commands, d.CommandQuadwords * 16, 0, V);
        Console.WriteLine("   decoded: " + string.Join(' ', Mdl3PmshCommands.Decode(r.Data, (int)d.Commands, d.CommandQuadwords * 16, d.IndexBits, p.Entries1C)));
        var tri = Mdl3PmshCommands.Triangles(r.Data, d, out _, out long used, p.Entries1C);
        var edges = new HashSet<long>(); int twice = 0, degenerate = 0;
        for (int i = 0; i + 2 < tri.Length; i += 3)
        {
            if (tri[i] == tri[i + 1] || tri[i + 1] == tri[i + 2] || tri[i] == tri[i + 2]) degenerate++;
            for (int e = 0; e < 3; e++) if (!edges.Add((long)tri[i + e] << 32 | (uint)tri[i + (e + 1) % 3])) twice++;
        }
        Console.WriteLine($"   triangles {tri.Length / 3}, unloaded slots {tri.Count(x => x < 0)}, max vertex {(tri.Length > 0 ? tri.Max() : -1)} (V {V}), " +
                          $"vertices used {tri.Where(x => x >= 0).Distinct().Count()}, index bits {used} of {d.IndexQuadwords * 128}, degenerate {degenerate}, edge twice {twice}");
        // long triangles per op1/op2 batch (edge > 4 x the median edge)
        {
            var decoded = p.Decode(r, me, d);
            var P = decoded.Positions;
            var cmds = Mdl3PmshCommands.Decode(r.Data, (int)d.Commands, d.CommandQuadwords * 16, d.IndexBits, p.Entries1C);
            var lens = new List<float>();
            for (int i = 0; i + 2 < tri.Length; i += 3)
                if (tri[i] >= 0 && tri[i + 1] >= 0 && tri[i + 2] >= 0)
                    lens.Add(MathF.Max(Vector3.Distance(P[tri[i]], P[tri[i + 1]]), MathF.Max(Vector3.Distance(P[tri[i + 1]], P[tri[i + 2]]), Vector3.Distance(P[tri[i]], P[tri[i + 2]]))));
            if (lens.Count > 0)
            {
                float med = lens.OrderBy(x => x).ElementAt(lens.Count / 2);
                int t = 0, batch = 0;
                foreach (var c in cmds.Where(c => c.Opcode is 1 or 2))
                {
                    int n = c.Operands[0] * 4, longOnes = 0, count = 0, first = t;
                    // padding triangles were dropped in tri: count only what is there
                    while (count < n && t < lens.Count) { if (lens[t] > 4 * med) longOnes++; t++; count++; }
                    Console.WriteLine($"   batch {batch++} op{c.Opcode}({c.Operands[0]}) tris {first}..{t - 1}: long {longOnes}");
                }
            }
        }
        int nv = (int)(used / d.IndexBits);
        Console.WriteLine($"   values after the list (from {nv}): " + string.Join(',', Enumerable.Range(nv, 60).Select(i => Bits(r.Data, (int)d.Indices, (long)i * d.IndexBits, d.IndexBits, true))));
        Console.WriteLine($"   bytes after the list (from byte {used / 8}): " + string.Join(' ', Enumerable.Range((int)(used / 8), 48).Select(k => r.U8((int)d.Indices + k).ToString("X2"))));
        if (me.BBoxes.Count > 0) Console.WriteLine("   bbox corners: " + string.Join(" | ", me.BBoxes[0].Select(v => v.ToString("F3", ci))));
        return 0;
    }

    static void Dump(string what, GTFormats.BigEndianReader r, int at, int size, int bits, int V)
    {
        Console.WriteLine($"{what} @{at:X} size {size}:");
        for (int i = 0; i < Math.Min(size, 96); i += 16)
            Console.WriteLine("   " + string.Join(' ', Enumerable.Range(i, Math.Min(16, size - i)).Select(k => r.U8(at + k).ToString("X2"))));
        if (bits == 0) return;
        foreach (bool msb in new[] { true, false })
        {
            int n = size * 8 / bits, over = 0, max = 0;
            var vals = new int[n];
            for (int i = 0; i < n; i++) { vals[i] = (int)Bits(r.Data, at, i * bits, bits, msb); if (vals[i] >= V) over++; max = Math.Max(max, vals[i]); }
            Console.WriteLine($"   {(msb ? "MSB" : "LSB")} {bits}-bit: {n} values, max {max}, >= V: {over}; first {string.Join(',', vals.Take(40))}");
        }
    }

    static Vector4 Read(byte[] data, int at, long bit, Mdl3PmshBitLayout l, bool msb)
    {
        var f = new float[4];
        for (int c = 0; c < 4; c++)
        {
            if (l.Bits[c] == 0) { bit += 0; continue; }
            ulong raw = Bits(data, at, bit, l.Bits[c], msb);
            bit += l.Bits[c];
            f[c] = (raw / (float)((1 << (l.Bits[c] - 1)) - 1) + 2f) * l.Scale[c] + l.Offset[c];
        }
        return new Vector4(f[0], f[1], f[2], f[3]);
    }

    public static ulong Bits(byte[] data, int at, long bit, int n, bool msb)
    {
        ulong v = 0;
        for (int i = 0; i < n; i++)
        {
            long b = bit + i;
            int by = data[at + (int)(b >> 3)];
            int x = msb ? (by >> (7 - (int)(b & 7))) & 1 : (by >> (int)(b & 7)) & 1;
            if (msb) v = (v << 1) | (uint)x; else v |= (ulong)x << i;
        }
        return v;
    }
}
