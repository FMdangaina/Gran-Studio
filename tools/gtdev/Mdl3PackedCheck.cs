using System.Numerics;
using System.Text.RegularExpressions;
using GTFormats;
using GTFormats.Mdl3;

/// <summary>
/// gtdev mdl3-packed &lt;files.tsv&gt; &lt;groupRegex&gt; &lt;out.txt&gt;
/// Decodes every GT5 (v8) packed shape: segments, stream descriptions, fixed-point positions, compressed triangles,
/// and checks the rules found (values all used, indices in range, triangle sums, index size, grid range, bbox).
/// </summary>
static class Mdl3PackedCheck
{
    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.WriteLine("mdl3-packed <files.tsv> <groupRegex> <out.txt>"); return 1; }
        var rx = new Regex(args[1]);
        var files = File.ReadAllLines(args[0]).Select(l => l.Split('\t')).Where(p => rx.IsMatch(p[1]) && p[2] is "6" or "8").ToList();
        using var w = new StreamWriter(args[2]);
        foreach (var grp in files.GroupBy(p => p[1]).OrderBy(g => g.Key))
        {
            var c = new SortedDictionary<string, int>();
            var ex = new Dictionary<string, string>();
            var kinds = new SortedDictionary<string, int>();
            var names = new SortedDictionary<string, int>();
            var sizeRule = new SortedDictionary<string, int>();
            var nLen = new List<float>();
            void Count(string k, string? e = null)
            {
                c[k] = c.GetValueOrDefault(k) + 1;
                if (e != null && !ex.ContainsKey(k)) ex[k] = e;
            }
            foreach (var p in grp)
            {
                Mdl3File f;
                try { f = Mdl3File.Load(p[0]); } catch { Count("file not loaded"); continue; }
                var r = f.Data;
                float[]? frame = null;
                try { frame = Mdl3PackedShape.CarFrame(r); } catch { }
                for (int s = 0; s < f.Shapes.Count; s++)
                {
                    var sh = f.Shapes[s];
                    if (!Mdl3PackedShape.IsPacked(r, sh)) continue;
                    string where = $"{Path.GetFileName(Path.GetDirectoryName(p[0]))}\\{Path.GetFileName(p[0])} shape {s}";
                    try
                    {
                        var ps = Mdl3PackedShape.Read(r, sh);
                        Count("packed shapes");
                        string layout = $"flag {ps.Desc.Flag} in [{Layout(ps.Desc.Input)}]" + (ps.Desc.Flag == 8 ? $" out [{Layout(ps.Desc.Output)}]" : "");
                        int tris = 0;
                        // output attribute -> name of the shape's FVF element at the same offset
                        if (sh.FvfIndex >= 0 && sh.FvfIndex < f.Fvfs.Count)
                        {
                            var fvf = f.Fvfs[sh.FvfIndex];
                            string k = string.Join(" ", ps.Desc.Output.Attributes.OrderBy(a => a.Id).Select(a => $"{a.Id:X2}:t{a.Type}x{a.Count}")) + "  ->  " +
                                       string.Join(" ", fvf.Elements.Where(e => e.ArrayIndex == 0).OrderBy(e => e.Name).Select(e => $"{e.Name}:{e.Type}x{e.Count}"));
                            names[k] = names.GetValueOrDefault(k) + 1;
                            if (fvf.VertexSize != ps.Desc.Output.Stride) Count("output stride != shape FVF vertex size", where);
                        }
                        var (bmin, bmax) = sh.BBoxOffset != 0 ? Box(r, (int)sh.BBoxOffset) : (Vector3.Zero, Vector3.Zero);
                        int outside = 0;
                        float eps = 1e-3f * Math.Max(1f, (bmax - bmin).Length());
                        foreach (var seg in ps.Segments)
                        {
                            Count("segments");
                            string kind = $"streams {seg.Streams.Length}, position format {seg.PositionFormatSize:X2}, {layout}";
                            kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
                            if (seg.LodVertexCounts.Length > 1)
                            {
                                Count($"segments with {seg.LodVertexCounts.Length} LODs, {seg.MorphStreamCount} extra streams");
                                int cnt = (int)r.U32(seg.Offset + 0x24);
                                for (int lv = 1; lv < seg.LodVertexCounts.Length; lv++)
                                {
                                    if (r.U16(cnt + lv * 8) + r.U16(cnt + lv * 8 + 2) != r.U16(cnt + (lv - 1) * 8)) Count("FAIL LOD counts not nested", where);
                                    var lod = seg.Lod(r, lv);
                                    if (lod.TriangleCount == 0) { Count("LOD levels without triangles (offset 0)"); continue; }
                                    var lt = Mdl3PackedShape.Triangles(r, lod, out int ln, out int lu, out _);
                                    int lb = (r.U16(lod.IndexOffset) * ln + 7) / 8;
                                    bool ok = lu == ln && lt.All(i => i < lod.VertexCount) && lod.IndexSize == 8 + lb + (lod.TriangleCount + 3) / 4 + 2;
                                    Count(ok ? "ok LOD level indices (used, < level vertex count, size rule)" : "FAIL LOD level indices", ok ? null : where + $" level {lv}");
                                }
                            }
                            var t = Mdl3PackedShape.Triangles(r, seg, out int nVal, out int used, out int end);
                            tris += seg.TriangleCount;
                            if (used != nVal) Count("FAIL index values not all used", where);
                            if (r.U16(seg.IndexOffset + 4) != nVal) Count("FAIL index value count repeated differently", where);
                            if (t.Any(i => i >= seg.VertexCount)) Count("FAIL index >= vertex count", where);
                            int valueBytes = (r.U16(seg.IndexOffset) * nVal + 7) / 8;
                            if (seg.IndexSize == 8 + valueBytes + (seg.TriangleCount + 3) / 4 + 2) Count("ok index size = 8 + value bytes + code bytes + 2 (alignment pad not counted)");
                            else Count("FAIL index size rule", where + $" size {seg.IndexSize} data {end - seg.IndexOffset}");
                            {
                                int len = end - seg.IndexOffset;
                                string k = $"size-len {seg.IndexSize - len}, len%2 {len & 1}, codes%4 {seg.TriangleCount & 3}, pad {(8 + valueBytes) & 1}";
                                sizeRule[k] = sizeRule.GetValueOrDefault(k) + 1;
                            }
                            for (int i = 0; i < t.Length; i += 3)
                                if (t[i] == t[i + 1] || t[i + 1] == t[i + 2] || t[i] == t[i + 2]) Count("degenerate triangles", where);
                            if (seg.PositionFormatSize != 0)
                            {
                                var g = Mdl3PackedShape.Grid(r, seg, out int pend);
                                bool over = false;
                                foreach (int v in g) over |= v > 0xFFFF;
                                if (over) Count("FAIL grid value > 65535", where);
                                if (frame == null) Count("FAIL fixed point without a car frame", where);
                            }
                            else if (ps.Desc.Input[1] == null) Count("FAIL no position", where);
                            if (frame != null || seg.PositionFormatSize == 0)
                            {
                                var pos = ps.Positions(r, seg, frame);
                                foreach (var q in pos)
                                    if (sh.BBoxOffset != 0 && Vector3.Min(q - bmin, bmax - q) is var d && Math.Min(d.X, Math.Min(d.Y, d.Z)) < -eps) outside++;
                            }
                            if (ps.Desc.Input[2] is { Type: 4 } na)
                                for (int v = 0; v < seg.VertexCount; v += 3) nLen.Add(Math.Abs(ps.Attribute(r, seg, na, v).Length() - 1));
                            // the input stream must fit before the next stream / the index data
                            if (ps.Desc.Input.Attributes.Count > 0 && (seg.Streams.Length > 1 || seg.PositionFormatSize == 0))
                            {
                                int space = (seg.Streams.Length > 1 ? seg.Streams[1] : seg.IndexOffset) - seg.Streams[0];
                                int need = seg.VertexCount * ps.Desc.Input.Stride;
                                if (space < need || space >= need + 128) Count("stream 0 size does not fit the stride", where + $" need {need} space {space}");
                            }
                        }
                        bool extended = ps.Segments.Any(sg => r.U32(sg.Offset + 0x0C) != 0);
                        if (extended) Count("shapes with extended segments (S+0C block)");
                        if (tris != sh.TriCount) Count($"triangle sum {(tris < sh.TriCount ? "<" : ">")} shape +26, extended segments: {extended}", where + $" {tris} vs {sh.TriCount}");
                        else Count("ok shapes (triangle sum)");
                        if (sh.BBoxOffset != 0) Count(outside == 0 ? "ok positions inside shape bbox" : "positions outside shape bbox", outside == 0 ? null : where + $" {outside} vertices");
                    }
                    catch (Exception e) { Count("FAIL exception " + e.GetType().Name, where + " " + e.Message + " " + e.StackTrace?.Split('\n')[0].Trim()); }
                }
            }
            w.WriteLine($"================ {grp.Key}: {grp.Count()} files");
            foreach (var (k, n) in c) w.WriteLine($"{n,9}  {k}{(ex.TryGetValue(k, out var e) ? "   e.g. " + e : "")}");
            if (nLen.Count > 0)
            {
                nLen.Sort();
                w.WriteLine($"normals X11Y11Z10N: {nLen.Count} sampled, |len-1| median {nLen[nLen.Count / 2]:0.0000}, within 0.02: {nLen.Count(x => x < 0.02f) * 100.0 / nLen.Count:0.00}%");
            }
            w.WriteLine("index size vs data length:");
            foreach (var (k, n) in sizeRule) w.WriteLine($"{n,9}  {k}");
            w.WriteLine("output attributes -> shape FVF elements:");
            foreach (var (k, n) in names) w.WriteLine($"{n,9}  {k}");
            w.WriteLine("segment kinds:");
            foreach (var (k, n) in kinds.OrderByDescending(k => k.Value)) w.WriteLine($"{n,9}  {k}");
            w.WriteLine();
            w.Flush();
        }
        Console.WriteLine($"{files.Count} files -> {args[2]}");
        return 0;
    }

    static string Layout(Mdl3PackedLayout l) =>
        $"stride {l.Stride}: " + string.Join(" ", l.Attributes.Select(a => $"{a.Id:X2}:t{a.Type}x{a.Count}@{a.Offset}"));

    static (Vector3 min, Vector3 max) Box(BigEndianReader r, int at)
    {
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var v = new Vector3(r.F32(at + i * 12), r.F32(at + i * 12 + 4), r.F32(at + i * 12 + 8));
            min = Vector3.Min(min, v); max = Vector3.Max(max, v);
        }
        return (min, max);
    }
}
