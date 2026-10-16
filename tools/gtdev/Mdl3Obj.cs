using System.Globalization;
using System.Text;
using GTFormats.Mdl3;

/// <summary>
/// mdl3-obj &lt;file&gt; &lt;out.obj&gt;: export the plain shapes (vertex buffer inside the MDL3) as an OBJ, one group per shape.
/// Research export to look at the geometry; packed shapes are skipped. Also writes &lt;out&gt;.txt with the index data per shape.
/// </summary>
static class Mdl3Obj
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("mdl3-obj <file> <out.obj>  |  mdl3-obj <files.tsv> <groupRegex> (rank files by plain vertices)");
            return 1;
        }
        if (args[0].EndsWith(".tsv", StringComparison.OrdinalIgnoreCase)) return Rank(args[0], args[1]);
        var m = Mdl3File.Load(args[0]);
        var r = m.Data;
        var obj = new StringBuilder();
        var log = new StringBuilder();
        var ci = CultureInfo.InvariantCulture;
        int vBase = 1, exported = 0, skipped = 0;

        for (int s = 0; s < m.Shapes.Count; s++)
        {
            var sh = m.Shapes[s];
            string name = m.ShapeNames.TryGetValue(s, out var n) ? n[0] : $"shape{s}";
            if (sh.VerticesOffset == 0 || sh.TriOffset == 0 || sh.FvfIndex < 0)
            {
                skipped++;
                continue;
            }
            var fvf = m.Fvfs[sh.FvfIndex];
            var pos = fvf["position"];
            if (pos is null) { skipped++; continue; }

            // Index buffer: u16 values at TriOffset. TriLength/TriCount meaning is what we look at here.
            int idxCount = (int)sh.TriLength;
            var idx = new int[idxCount];
            int max = 0, restarts = 0;
            for (int i = 0; i < idxCount; i++)
            {
                idx[i] = r.U16((int)sh.TriOffset + i * 2);
                if (idx[i] == 0xFFFF) restarts++;
                else max = Math.Max(max, idx[i]);
            }
            var tris = Triangles(idx, sh.TriCount, out string mode);
            log.AppendLine($"{s,4} v={sh.VertexCount,6} triLen={sh.TriLength,6} triCount={sh.TriCount,6} flags={sh.Flags:X4} " +
                           $"maxIdx={max,6} restarts={restarts,4} mode={mode,-6} tris={tris.Count,6} first={string.Join(',', idx.Take(12))}  {name}");

            obj.AppendLine($"g s{s:D4}_{Short(name)}");
            for (int v = 0; v < sh.VertexCount; v++)
            {
                int at = (int)(sh.VerticesOffset + fvf.ElementOffset(pos, v));
                obj.AppendLine(string.Create(ci, $"v {r.F32(at)} {r.F32(at + 4)} {r.F32(at + 8)}"));
            }
            foreach (var (a, b, c) in tris)
                if (a < sh.VertexCount && b < sh.VertexCount && c < sh.VertexCount)
                    obj.AppendLine($"f {a + vBase} {b + vBase} {c + vBase}");
            vBase += (int)sh.VertexCount;
            exported++;
        }

        File.WriteAllText(args[1], obj.ToString());
        File.WriteAllText(Path.ChangeExtension(args[1], ".txt"), log.ToString());
        Console.WriteLine($"{exported} shapes exported, {skipped} skipped (packed or no data) -> {args[1]}");
        return 0;
    }

    /// <summary>Top 20 files by share of plain vertices (the ones an OBJ export shows best).</summary>
    static int Rank(string tsv, string groupRegex)
    {
        var re = new System.Text.RegularExpressions.Regex(groupRegex);
        var rows = new List<(string path, long plain, int plainShapes, int packedShapes)>();
        foreach (var line in File.ReadLines(tsv).Skip(1))
        {
            var c = line.Split('\t');
            if (!re.IsMatch(c[1])) continue;
            var m = Mdl3File.Load(c[0]);
            long plain = 0; int ps = 0, pk = 0;
            foreach (var sh in m.Shapes)
            {
                if (sh.VerticesOffset != 0) { plain += sh.VertexCount; ps++; }
                else pk++;
            }
            rows.Add((c[0], plain, ps, pk));
        }
        foreach (var x in rows.OrderByDescending(x => x.plainShapes - x.packedShapes * 4).Take(20))
            Console.WriteLine($"{x.plain,8} plain verts  {x.plainShapes,4} plain / {x.packedShapes,4} packed  {x.path}");
        return 0;
    }

    /// <summary>List when the buffer holds exactly 3 indices per triangle, otherwise strip with 0xFFFF restart.</summary>
    static List<(int, int, int)> Triangles(int[] idx, int triCount, out string mode)
    {
        var tris = new List<(int, int, int)>();
        if (idx.Length == triCount * 3 && !idx.Contains(0xFFFF))
        {
            mode = "list";
            for (int i = 0; i + 2 < idx.Length; i += 3) tris.Add((idx[i], idx[i + 1], idx[i + 2]));
            return tris;
        }
        mode = "strip";
        int run = 0;
        for (int i = 0; i < idx.Length; i++)
        {
            if (idx[i] == 0xFFFF) { run = 0; continue; }
            run++;
            if (run < 3) continue;
            int a = idx[i - 2], b = idx[i - 1], c = idx[i];
            if (a == b || b == c || a == c) continue;   // degenerate joins
            tris.Add((run & 1) == 1 ? (a, b, c) : (b, a, c));
        }
        return tris;
    }

    static string Short(string path)
    {
        var last = path.Split('|')[^1];
        return new string(last.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
    }
}
