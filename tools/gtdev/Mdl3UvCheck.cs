using System.Numerics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using GTFormats;
using GTFormats.Mdl3;

/// <summary>
/// gtdev mdl3-uv &lt;files.tsv&gt; &lt;groupRegex&gt; &lt;out.txt&gt; [maxFilesToList]
/// For every plain shape: range of the first UV set, the diffuse texture size of its material and the vertex
/// program (hash of its microcode) the material uses. Groups the UV ranges by UV type and vertex program,
/// to find where the UV scale comes from.
/// </summary>
static class Mdl3UvCheck
{
    sealed class Group
    {
        public int Shapes;
        public List<float> Span = [];        // max(|u|,|v|) per shape
        public Dictionary<string, int> Examples = [];
    }

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.WriteLine("mdl3-uv <files.tsv> <groupRegex> <out.txt> [listFiles]"); return 1; }
        var rx = new Regex(args[1]);
        int listFiles = args.Length > 3 ? int.Parse(args[3]) : 0;
        var files = File.ReadAllLines(args[0]).Select(l => l.Split('\t')).Where(p => rx.IsMatch(p[1])).ToList();
        using var w = new StreamWriter(args[2]);
        var groups = new SortedDictionary<string, Group>();
        var wrapStats = new SortedDictionary<string, int>();
        int listed = 0;

        foreach (var p in files)
        {
            var f = Mdl3File.Load(p[0]);
            var r = f.Data;
            bool list = listed++ < listFiles;
            if (list) w.WriteLine($"#### {p[0]}");
            for (int si = 0; si < f.Shapes.Count; si++)
            {
                var s = f.Shapes[si];
                if (s.VertexCount == 0 || s.VerticesOffset == 0 || s.FvfIndex < 0) continue;
                var fvf = f.Fvfs[s.FvfIndex];
                var uv = fvf["map1"] ?? fvf["map12"];
                if (uv is null) continue;

                var lo = new Vector2(float.MaxValue); var hi = new Vector2(float.MinValue);
                for (int v = 0; v < s.VertexCount; v++)
                {
                    var q = Decode(r, (int)(s.VerticesOffset + fvf.ElementOffset(uv, v)), uv);
                    lo = Vector2.Min(lo, q); hi = Vector2.Max(hi, q);
                }
                var (vp, tex, shader) = Chain(r, s.MaterialIndex);
                // Wrap modes of every texture the material samples (texInfo +0C = RSX TEXTURE_ADDRESS: wrapS bits 0-3, wrapT 8-11).
                bool outside = lo.X < -0.001f || lo.Y < -0.001f || hi.X > 1.001f || hi.Y > 1.001f;
                string wraps = string.Join(",", SamplerWraps(r, s.MaterialIndex).Distinct().Order());
                string wk = $"{uv.Type} {(outside ? "outside 0..1" : "inside 0..1")} {(lo.X < -0.001f || lo.Y < -0.001f ? "neg" : "")} wrap {wraps}";
                wrapStats[wk] = wrapStats.GetValueOrDefault(wk) + 1;
                string key = $"{uv.Name} {uv.Type}x{uv.Count} vp {vp}";
                if (!groups.TryGetValue(key, out var g)) groups[key] = g = new Group();
                g.Shapes++;
                float span = MathF.Max(MathF.Max(MathF.Abs(lo.X), MathF.Abs(hi.X)), MathF.Max(MathF.Abs(lo.Y), MathF.Abs(hi.Y)));
                g.Span.Add(span);
                if (g.Examples.Count < 3) g.Examples[$"{Path.GetFileName(Path.GetDirectoryName(p[0]))}\\{Path.GetFileName(p[0])} shape {si}"] = 1;
                if (list)
                    w.WriteLine($"shape {si,4} mat {s.MaterialIndex,4} {uv.Name} {uv.Type}x{uv.Count} u {lo.X,9:0.####}..{hi.X,9:0.####} v {lo.Y,9:0.####}..{hi.Y,9:0.####}  tex {tex}  shader {shader} vp {vp}");
            }
        }

        w.WriteLine("\n================ shapes by UV type, UV range and wrap modes (S/T) of the material's textures (1 wrap, 2 mirror, 3 clamp to edge)");
        foreach (var (k, n) in wrapStats) w.WriteLine($"{n,7}  {k}");
        w.WriteLine("\n================ UV span (max |u|,|v| per shape) by UV type and vertex program");
        foreach (var (k, g) in groups.OrderByDescending(x => x.Value.Shapes))
        {
            g.Span.Sort();
            float P(double q) => g.Span[(int)((g.Span.Count - 1) * q)];
            w.WriteLine($"{g.Shapes,6}  {k,-40} span min {P(0):0.####} p10 {P(0.1):0.####} median {P(0.5):0.####} p90 {P(0.9):0.####} max {P(1):0.####}   e.g. {string.Join(", ", g.Examples.Keys)}");
        }
        Console.WriteLine($"{files.Count} files -> {args[2]}");
        return 0;
    }

    /// <summary>
    /// material -> material data -> shader reference -> SHDS definition -> vertex program.
    /// Returns a short hash of the program microcode, the diffuse texture size and the shader def id.
    /// </summary>
    static (string vp, string tex, string shader) Chain(BigEndianReader r, int material)
    {
        const int mat = 0xE4, shds = 0xFC;
        int m = (int)r.U32(mat + 8) + material * 0x34;
        string tex = "-";
        int keyCount = r.U16(m + 0x0A); uint keys = r.U32(m + 0x0C);
        for (int k = 0; k < keyCount; k++)
            if (r.CString((int)r.U32((int)keys + k * 8)) == "diffuseMapSampler")
            {
                int ti = (int)r.U32((int)keys + k * 8 + 4);
                if (ti < r.U16(mat + 6))
                {
                    int t = (int)r.U32(mat + 0x14) + ti * 0x44;
                    tex = $"{r.U16(t + 0x1C)}x{r.U16(t + 0x1E)}";
                }
            }
        int d = (int)r.U32(mat + 0x0C) + r.S16(m + 4) * 0x28;
        uint sr = r.U32(d + 0x14);
        if (sr == 0) return ("none", tex, "-");
        int defId = (int)r.U32((int)sr + 4);
        int def = (int)r.U32(shds + 0x1C) + defId * 0x0C;
        int prog = r.S16(def + 8);
        int pr = (int)r.U32(shds + 0x20) + prog * 0x28;
        uint code = r.U32(pr), size = r.U32(pr + 4);
        string hash = Convert.ToHexString(SHA1.HashData(r.Data.AsSpan((int)code, (int)size)))[..8];
        return (hash, tex, $"{defId}/p{prog}");
    }

    static IEnumerable<string> SamplerWraps(BigEndianReader r, int material)
    {
        const int mat = 0xE4;
        int m = (int)r.U32(mat + 8) + material * 0x34;
        int keyCount = r.U16(m + 0x0A); uint keys = r.U32(m + 0x0C);
        for (int k = 0; k < keyCount; k++)
        {
            int ti = (int)r.U32((int)keys + k * 8 + 4);
            if (ti >= r.U16(mat + 6)) { yield return "badindex"; continue; }
            uint addr = r.U32((int)r.U32(mat + 0x14) + ti * 0x44 + 0x0C);
            yield return $"{addr & 0xF}/{(addr >> 8) & 0xF}";
        }
    }

    static Vector2 Decode(BigEndianReader r, int at, Mdl3FvfElement e) => e.Type switch
    {
        RsxVertexType.F => new(r.F32(at), r.F32(at + 4)),
        RsxVertexType.S1 => new(r.S16(at) / 32767f, r.S16(at + 2) / 32767f),
        RsxVertexType.UB => new(r.U8(at) / 255f, r.U8(at + 1) / 255f),
        RsxVertexType.SF => new((float)r.F16(at), (float)r.F16(at + 2)),
        _ => new(float.NaN),
    };
}
