using System.Text.RegularExpressions;
using GTFormats;

/// <summary>
/// gtdev mdl3-mat &lt;files.tsv&gt; &lt;groupRegex&gt; &lt;out.txt&gt;
/// Research pass over the material block (header 0xE4), the SHDS block (0xFC) and the TXS3 header:
/// column statistics of every record type, with the block each u32 value lands in.
/// </summary>
static class Mdl3MatCheck
{
    sealed record Block(string Name, int Offset, int Count, int Stride);

    sealed class Stats
    {
        public int Records, Files;
        public List<uint>[] U16 = [], U32 = [];
        public Dictionary<string, int>[] Region = [];
    }

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.WriteLine("mdl3-mat <files.tsv> <groupRegex> <out.txt> [dumpPath]"); return 1; }
        using var w = new StreamWriter(args[2]);
        if (args.Length > 3) { Dump(w, args[3]); return 0; }

        var rx = new Regex(args[1]);
        var files = File.ReadAllLines(args[0]).Select(l => l.Split('\t')).Where(p => rx.IsMatch(p[1])).ToList();

        foreach (var ver in files.GroupBy(p => p[2]).OrderBy(g => int.Parse(g.Key)))
        {
            var stats = new SortedDictionary<string, Stats>();
            var paramNames = new Dictionary<string, int>();
            foreach (var p in ver)
            {
                var r = new BigEndianReader(File.ReadAllBytes(p[0]));
                int dataAt = (int)r.U32(0xE4 + 0x0C);
                for (int i = 0; i < r.U16(0xE4 + 2); i++)
                {
                    int a = dataAt + i * 0x28;
                    string dn = r.U32(a) == 0 ? "-" : r.CString((int)r.U32(a));
                    for (int k = 0; k < r.U16(a + 0x0A); k++)
                    {
                        string key = dn + " / " + r.CString((int)r.U32((int)r.U32(a + 0x10) + k * 8));
                        paramNames[key] = paramNames.GetValueOrDefault(key) + 1;
                    }
                }
                var blocks = Blocks(r);
                var starts = Starts(r, blocks);
                foreach (var b in blocks)
                {
                    if (b.Count == 0 || b.Offset == 0) continue;
                    if (!stats.TryGetValue(b.Name, out var s))
                    {
                        stats[b.Name] = s = new Stats
                        {
                            U16 = Enumerable.Range(0, b.Stride / 2).Select(_ => new List<uint>()).ToArray(),
                            U32 = Enumerable.Range(0, b.Stride / 4).Select(_ => new List<uint>()).ToArray(),
                            Region = Enumerable.Range(0, b.Stride / 4).Select(_ => new Dictionary<string, int>()).ToArray(),
                        };
                    }
                    s.Files++;
                    for (int i = 0; i < b.Count; i++, s.Records++)
                    {
                        int at = b.Offset + i * b.Stride;
                        if (at + b.Stride > r.Length) break;
                        for (int c = 0; c < b.Stride / 2; c++) s.U16[c].Add(r.U16(at + c * 2));
                        for (int c = 0; c < b.Stride / 4; c++)
                        {
                            uint v = r.U32(at + c * 4);
                            s.U32[c].Add(v);
                            string where = v == 0 ? "zero" : v >= r.Length ? "outside" :
                                v < 0xE4 ? "header" : starts.LastOrDefault(x => x.start <= v).name ?? "?";
                            s.Region[c][where] = s.Region[c].GetValueOrDefault(where) + 1;
                        }
                    }
                }
            }

            foreach (var (name, s) in stats)
            {
                w.WriteLine($"================ v{ver.Key} {name}: {s.Files} files, {s.Records} records, stride 0x{s.U16.Length * 2:X}");
                w.WriteLine("u16 col   zero      min      max  distinct  top values");
                for (int c = 0; c < s.U16.Length; c++) Line(w, $"+{c * 2:X2}", s.U16[c]);
                w.WriteLine("u32 col   zero      min      max  distinct  top values  | lands in");
                for (int c = 0; c < s.U32.Length; c++)
                {
                    Line(w, $"+{c * 4:X2}", s.U32[c], end: false);
                    w.WriteLine("  | " + string.Join(" ", s.Region[c].OrderByDescending(k => k.Value).Take(4).Select(k => $"{k.Key}:{k.Value}")));
                }
                w.WriteLine();
            }
            w.WriteLine($"================ v{ver.Key} material data name / parameter name: count");
            foreach (var (k, n) in paramNames.OrderBy(k => k.Key)) w.WriteLine($"{n,8}  {k}");
            w.WriteLine();
        }
        Console.WriteLine($"{files.Count} files -> {args[2]}");
        return 0;
    }

    /// <summary>One file: every material, its material data and what the data points to, words shown as hex and float.</summary>
    static void Dump(StreamWriter w, string path)
    {
        var r = new BigEndianReader(File.ReadAllBytes(path));
        const int mat = 0xE4;
        int matCount = r.U16(mat), dataCount = r.U16(mat + 2);
        int matsAt = (int)r.U32(mat + 8), dataAt = (int)r.U32(mat + 0x0C);
        w.WriteLine($"{path}: {matCount} materials, {dataCount} material data, byte4 {r.U8(mat + 4)}, gcm {r.U8(mat + 5)}, texInfo {r.U16(mat + 6)}");

        string Name(uint off) => off == 0 ? "-" : r.CString((int)off);
        string Keys(int count, uint off) => string.Join(" ", Enumerable.Range(0, count)
            .Select(i => $"{Name(r.U32((int)off + i * 8))}={r.U32((int)off + i * 8 + 4)}"));
        string Words(uint off, int n) => off == 0 ? "-" : string.Join(" ", Enumerable.Range(0, n)
            .Select(i => { uint v = r.U32((int)off + i * 4); float f = BitConverter.UInt32BitsToSingle(v);
                return float.IsFinite(f) && MathF.Abs(f) is > 1e-4f and < 1e5f ? $"{v:X8}({f:G5})" : $"{v:X8}"; }));

        w.WriteLine("\n== materials");
        for (int i = 0; i < matCount; i++)
        {
            int a = matsAt + i * 0x34;
            w.WriteLine($"[{i}] {Name(r.U32(a))} data={r.S16(a + 4)} p06={r.S16(a + 6)} flags={r.U16(a + 8)} keys: {Keys(r.U16(a + 0x0A), r.U32(a + 0x0C))}");
        }

        w.WriteLine("\n== material data");
        for (int i = 0; i < dataCount; i++)
        {
            int a = dataAt + i * 0x28;
            w.WriteLine($"[{i}] {Name(r.U32(a))} idx={r.U32(a + 4)} b8={r.U8(a + 8)} b9={r.U8(a + 9)} s20={r.S16(a + 0x20)} n22={r.S16(a + 0x22)}");
            w.WriteLine($"   keys: {Keys(r.U16(a + 0x0A), r.U32(a + 0x10))}");
            w.WriteLine($"   +0C @{r.U32(a + 0x0C):X}: {Words(r.U32(a + 0x0C), 8)}");
            w.WriteLine($"   +14 @{r.U32(a + 0x14):X}: {Words(r.U32(a + 0x14), 10)}");
            uint l18 = r.U32(a + 0x18);
            for (int k = 0; k < r.S16(a + 0x22); k++)
            {
                int n = (int)r.U32((int)l18 + k * 8); uint off = r.U32((int)l18 + k * 8 + 4);
                w.WriteLine($"   +18[{k}] n={n} @{off:X}: {(off == 0 ? "-" : string.Join(" ", Enumerable.Range(0, n).Select(j => r.S16((int)off + j * 2))))}");
            }
            w.WriteLine($"   +1C @{r.U32(a + 0x1C):X}: {Words(r.U32(a + 0x1C), 2)}");
            w.WriteLine($"   +24 @{r.U32(a + 0x24):X}: {Words(r.U32(a + 0x24), 8)}");
            uint sr = r.U32(a + 0x14);
            if (sr != 0) w.WriteLine($"   shaderRef: {Name(r.U32((int)sr))} id={r.U32((int)sr + 4)}");
        }

        const int shds = 0xFC;
        int defCount = r.U16(shds + 0x16), progCount = r.U16(shds + 0x1A);
        uint defsAt = r.U32(shds + 0x1C), progsAt = r.U32(shds + 0x20);
        w.WriteLine($"\n== SHDS: defs {defCount} @{defsAt:X}, u16 +18 = {r.U16(shds + 0x18)}, programs {progCount} @{progsAt:X}");
        for (int i = 0; i < defCount; i++)
        {
            int a = (int)defsAt + i * 0x0C;
            w.WriteLine($"def[{i}] {Name(r.U32(a))} {r.U32(a + 4):X8} prog={r.S16(a + 8)} e={r.S16(a + 0x0A)}");
        }
        for (int i = 0; i < progCount; i++)
        {
            int a = (int)progsAt + i * 0x28;
            w.WriteLine($"prog[{i}] {Words((uint)a, 10)}");
        }
    }

    /// <summary>The record tables of the material, SHDS and TXS3 blocks, from their own headers.</summary>
    static List<Block> Blocks(BigEndianReader r)
    {
        const int mat = 0xE4, shds = 0xFC;
        int txs = (int)r.U32(0x48);
        return
        [
            new("mat.header", mat, 1, 0x18),
            new("mat.materials", (int)r.U32(mat + 0x08), r.U16(mat), 0x34),
            new("mat.data", (int)r.U32(mat + 0x0C), r.U16(mat + 2), 0x28),
            new("mat.gcmParams", (int)r.U32(mat + 0x10), r.U8(mat + 5), 0xA0),
            new("mat.texInfo", (int)r.U32(mat + 0x14), r.U16(mat + 6), 0x44),
            new("shds.header", shds, 1, 0x60),
            new("txs3.header", txs, 1, 0x40),
        ];
    }

    /// <summary>Known block starts (MDL3 header offsets + the sub-tables above), sorted, for the "lands in" column.</summary>
    static List<(uint start, string name)> Starts(BigEndianReader r, List<Block> blocks)
    {
        var l = new List<(uint, string)>();
        for (int o = 0x30; o < 0xE4; o += 4)
        {
            uint v = r.U32(o);
            if (v >= 0xE4 && v < r.Length) l.Add((v, $"h{o:X2}"));
        }
        foreach (var b in blocks) if (b.Offset != 0 && b.Count != 0) l.Add(((uint)b.Offset, b.Name));
        return l.OrderBy(x => x.Item1).ToList();
    }

    static void Line(StreamWriter w, string col, List<uint> v, bool end = true)
    {
        if (v.Count == 0) { w.WriteLine($"{col}  (no data)"); return; }
        var top = v.GroupBy(x => x).OrderByDescending(g => g.Count()).Take(5).Select(g => $"{g.Key:X}x{g.Count()}");
        string s = $"{col}  {v.Count(x => x == 0),6} {v.Min(),8:X} {v.Max(),8:X} {v.Distinct().Count(),8}  {string.Join(" ", top)}";
        if (end) w.WriteLine(s); else w.Write(s);
    }
}
