using GTFormats.Mdl3;

/// <summary>shape-dump &lt;file&gt; &lt;shape&gt; [n]: the shape record fields and the first n u16 index values (plain shapes).</summary>
static class ShapeDump
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("shape-dump <file> <shape> [n]"); return 1; }
        var m = Mdl3File.Load(args[0]);
        var sh = m.Shapes[int.Parse(args[1])];
        int n = args.Length > 2 ? int.Parse(args[2]) : 120;
        var r = m.Data;
        Console.WriteLine($"flags {sh.Flags:X4} fvf {sh.FvfIndex} ({(sh.FvfIndex >= 0 ? m.Fvfs[sh.FvfIndex].Name : "-")}) material {sh.MaterialIndex} v {sh.VertexCount} @{sh.VerticesOffset:X} " +
                          $"tri length {sh.TriLength} @{sh.TriOffset:X} +20 {sh.Unk20:X8} +24 {sh.Unk24:X4} +26 {sh.TriCount} bbox @{sh.BBoxOffset:X} packed @{sh.PackedRefOffset:X}");
        try { Console.WriteLine("   packed frame: " + string.Join(' ', Mdl3PackedShape.CarFrame(r).Select(f => f.ToString(System.Globalization.CultureInfo.InvariantCulture)))); } catch { }
        if (Mdl3PackedShape.IsPacked(r, sh))
        {
            var ps = Mdl3PackedShape.Read(r, sh);
            foreach (var a in ps.Desc.Input.Attributes) Console.WriteLine($"   packed input {a.Id}: type {a.Type} count {a.Count} offset {a.Offset}");
            Console.WriteLine($"   segment 0 position format size {ps.Segments[0].PositionFormatSize}");
        }
        if (sh.FvfIndex >= 0)
        {
            int fa = (int)r.U32(0x40) + sh.FvfIndex * Mdl3Fvf.Size;
            for (int k = 0; k < Mdl3Fvf.Size; k += 0x18)
                Console.WriteLine($"   fvf +{k:X2}: " + string.Join(' ', Enumerable.Range(k, Math.Min(0x18, Mdl3Fvf.Size - k)).Select(i => r.U8(fa + i).ToString("X2"))));
            foreach (var e in m.Fvfs[sh.FvfIndex].Elements) Console.WriteLine($"   element {e}");
        }
        if (sh.TriOffset == 0) return 0;
        var idx = Enumerable.Range(0, (int)Math.Min(sh.TriLength, n)).Select(i => r.U16((int)sh.TriOffset + i * 2)).ToList();
        Console.WriteLine(string.Join(' ', idx));
        var all = Enumerable.Range(0, (int)sh.TriLength).Select(i => r.U16((int)sh.TriOffset + i * 2)).ToList();
        Console.WriteLine($"max {all.Max()} (v {sh.VertexCount}), values >= v: {all.Count(x => x >= sh.VertexCount)}, 0xFFFF: {all.Count(x => x == 0xFFFF)}");
        return 0;
    }
}
