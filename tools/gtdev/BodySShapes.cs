using GTFormats.Mdl3;

/// <summary>
/// bodys-shapes &lt;body&gt; [maxRecords]: research dump of the shape tables of the body_s bundles: each 0x20 record as
/// u32 words next to the shape fields in the MDL3 (plain vertex count / tri length, PMSH mesh, declaration, data count).
/// </summary>
static class BodySShapes
{
    public static int Run(string[] args)
    {
        if (args.Length < 1) { Console.Error.WriteLine("bodys-shapes <body> [maxRecords]"); return 1; }
        int max = args.Length > 1 ? int.Parse(args[1]) : 40;
        var m = Mdl3File.Load(args[0]);
        var r = m.Data;
        var map = Mdl3StreamMap.Read(r) ?? throw new InvalidDataException("no stream map");
        var bodyS = File.ReadAllBytes(args[0] + "_s");
        int shown = 0;
        foreach (var (g, item, sub, s) in map.Streams())
        {
            var b = new Mdl3StreamBundle(s.Load(bodyS));
            if (b.ShapeCount == 0) continue;
            Console.WriteLine($"== {g}[{item}]{(sub >= 0 ? "." + sub : "")}: bundle {b.TotalSize:X} header {b.HeaderSize:X} data {b.DataSize:X} shapes {b.ShapeCount} table @{b.ShapeTable:X}");
            for (int i = 0; i < b.ShapeCount && shown < max; i++, shown++)
            {
                int at = b.ShapeTable + i * 0x20;
                var words = Enumerable.Range(0, 8).Select(k => b.R.U32(at + k * 4)).ToArray();
                int si = (int)words[0];
                var sh = m.Shapes[si];
                string info = $"fvf {sh.FvfIndex,3} v {sh.VertexCount,6} triLen {sh.TriLength,6} tris {sh.TriCount,6}";
                int mi = Mdl3Pmsh.MeshIndex(r, sh);
                if (sh.FvfIndex < 0 && m.Pmsh != null && mi >= 0)
                {
                    var me = m.Pmsh.Meshes[mi];
                    var d = m.Pmsh.Declarations[me.Declaration];
                    info += $" | pmsh {mi} decl {me.Declaration} packed [{string.Join(' ', d.Packed.Select(e => e.Name))}] raw stride {d.RawStride} data {me.DataCount}";
                }
                Console.WriteLine($"  {string.Join(' ', words.Select(x => x.ToString("X8")))}  {info}");
                if (shown < 3)
                {
                    foreach (var (label, off, len) in new[] { ("A", (int)words[3], 0x60), ("B", (int)words[4], 0x10), ("D", (int)words[6], 0x30), ("C", (int)words[5], 0x20) })
                        Console.WriteLine($"     {label} @{off:X}: " + string.Join(' ', Enumerable.Range(0, len / 4).Select(k => b.R.U32(off + k * 4).ToString("X8"))));
                }
            }
        }
        return 0;
    }
}
