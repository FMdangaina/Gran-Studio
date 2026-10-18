using System.Globalization;
using GTFormats.Mdl3;

/// <summary>
/// pmsh-dump &lt;file&gt; [out.txt]: GT6 PMSH research dump: declarations, bit layouts, meshes with their smooth data,
/// and per shape the packed reference record (0x40 bytes) next to the shape fields.
/// </summary>
static class PmshDump
{
    public static int Run(string[] args)
    {
        if (args.Length < 1) { Console.Error.WriteLine("pmsh-dump <file> [out.txt]"); return 1; }
        var m = Mdl3File.Load(args[0]);
        var r = m.Data;
        var p = Mdl3Pmsh.Read(r);
        var ci = CultureInfo.InvariantCulture;
        using var w = args.Length > 1 ? new StreamWriter(args[1]) : new StreamWriter(Console.OpenStandardOutput());
        if (p == null) { w.WriteLine("no PMSH"); return 0; }
        w.WriteLine($"{args[0]}: {p.Meshes.Count} meshes, {p.Layouts.Count} layout arrays, {p.Declarations.Count} declarations, {m.Shapes.Count} shapes");
        for (int i = 0; i < p.Declarations.Count; i++)
        {
            var d = p.Declarations[i];
            w.WriteLine($"decl {i}: packed stride {d.PackedStride} raw stride {d.RawStride} +04 {d.Unk04:X8}");
            foreach (var e in d.Packed.Concat(d.Raw))
                w.WriteLine($"    {(e.Packed ? "P" : "R")} {e.Name,-14} id {e.NameId,3} out@{e.OutputOffset,3} {e.Type}x{e.Count}");
        }
        for (int i = 0; i < p.Layouts.Count; i++)
        {
            w.WriteLine($"layout array {i}:");
            foreach (var l in p.Layouts[i])
                w.WriteLine(string.Create(ci, $"    total {l.TotalBits,3} bits {string.Join(',', l.Bits)} ends {string.Join(',', l.BitEnds)} +0C {l.Unk0C} +0E {l.Unk0E} scale {string.Join(' ', l.Scale.Select(f => f.ToString("G6", ci)))} offset {string.Join(' ', l.Offset.Select(f => f.ToString("G6", ci)))}"));
        }
        for (int i = 0; i < p.Meshes.Count; i++)
        {
            var me = p.Meshes[i];
            w.WriteLine(string.Create(ci, $"mesh {i} @{me.Offset:X}: decl {me.Declaration} layouts {me.LayoutArray} +04 {me.Unk04} data {me.DataCount} k {me.K} +0C {me.Unk0C} +18 {me.Unk18} +1C {me.Unk1C} +20 {string.Join(' ', me.Unk20)}"));
            foreach (var d in me.Data)
                w.WriteLine($"    data @{d.Offset:X}: packed {d.PackedVertices:X} +04 {d.Unk04:X} +08 {d.Entries08:X} raw {d.RawVertices:X} +10 {d.Streams10:X} idx {d.Indices:X} cmd {d.Commands:X} +1C {d.Unk1C:X} " +
                            $"flags {d.Flags:X} V {d.VertexCount} +24 {d.Unk24} out {d.OutputVertexCount} +28 {d.Quadwords28} idxQ {d.IndexQuadwords} cmdQ {d.CommandQuadwords} idxBits {d.IndexBits} +2F {d.Unk2F}");
        }
        for (int s = 0; s < m.Shapes.Count; s++)
        {
            var sh = m.Shapes[s];
            string refHex = sh.PackedRefOffset == 0 ? "-" : string.Join(' ', Enumerable.Range(0, 0x10).Select(k => r.U32((int)sh.PackedRefOffset + k * 4).ToString("X8")));
            w.WriteLine($"shape {s,3} flags {sh.Flags:X4} fvf {sh.FvfIndex,3} mat {sh.MaterialIndex,3} v {sh.VertexCount,6} triLen {sh.TriLength,6} tris {sh.TriCount,6} +24 {sh.Unk24:X4} ref {refHex}  {(m.ShapeNames.TryGetValue(s, out var n) ? n[0] : "")}");
        }
        return 0;
    }
}
