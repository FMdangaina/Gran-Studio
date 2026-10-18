using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using GTFormats.Mdl3;

/// <summary>
/// s1-probe &lt;files.tsv&gt; &lt;groupRegex&gt; [maxShapes]: research on plain shapes with S1 (signed 16-bit) positions:
/// raw value range per axis (/32767) against the shape bbox (v8 +28) or nothing (v14), with the shape name and format.
/// </summary>
static class S1Probe
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("s1-probe <files.tsv> <groupRegex> [maxShapes]"); return 1; }
        var rx = new Regex(args[1]);
        int max = args.Length > 2 ? int.Parse(args[2]) : 30, shown = 0;
        var ci = CultureInfo.InvariantCulture;
        foreach (var c in File.ReadLines(args[0]).Select(l => l.Split('\t')).Where(c => rx.IsMatch(c[1])))
        {
            if (int.Parse(c[2]) < 6) continue;
            var m = Mdl3File.Load(c[0]);
            var r = m.Data;
            for (int s = 0; s < m.Shapes.Count && shown < max; s++)
            {
                var sh = m.Shapes[s];
                if (sh.FvfIndex < 0 || sh.VerticesOffset == 0) continue;
                var fvf = m.Fvfs[sh.FvfIndex];
                if (fvf["position"] is not { Type: RsxVertexType.S1 } pos) continue;
                var lo = new Vector3(float.MaxValue); var hi = new Vector3(float.MinValue);
                for (int v = 0; v < sh.VertexCount; v++)
                {
                    int at = (int)(sh.VerticesOffset + fvf.ElementOffset(pos, v));
                    var q = new Vector3(r.S16(at), r.S16(at + 2), pos.Count > 2 ? r.S16(at + 4) : 0);
                    lo = Vector3.Min(lo, q); hi = Vector3.Max(hi, q);
                }
                string box = "no bbox";
                if (sh.BBoxOffset != 0)
                {
                    var b = Enumerable.Range(0, 8).Select(i => new Vector3(r.F32((int)sh.BBoxOffset + i * 12), r.F32((int)sh.BBoxOffset + i * 12 + 4), r.F32((int)sh.BBoxOffset + i * 12 + 8))).ToArray();
                    var bl = b.Aggregate(Vector3.Min); var bh = b.Aggregate(Vector3.Max);
                    box = string.Create(ci, $"bbox {bl:F4} .. {bh:F4}, bbox size / raw size {(bh - bl) / (hi - lo):F7}");
                }
                string name = m.ShapeNames.TryGetValue(s, out var n) ? n[0] : "?";
                Console.WriteLine(string.Create(ci, $"{c[0]} s{s} fvf {sh.FvfIndex} x{pos.Count} {fvf.Name} size {fvf.VertexSize} v {sh.VertexCount}: raw {lo} .. {hi}; {box}  {name}"));
                shown++;
            }
            if (shown >= max) break;
        }
        return 0;
    }
}
