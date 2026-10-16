using System.Text.RegularExpressions;
using GTFormats;
using GTFormats.Mdl3;

/// <summary>
/// gtdev mdl3-records &lt;files.tsv&gt; &lt;groupRegex&gt; &lt;table&gt; &lt;stride hex&gt; &lt;out.txt&gt;
/// Column statistics of one header table over many files: for every u16 and u32 position of the record,
/// value ranges, and for u32 values that could be offsets, which header block they land in.
/// </summary>
static class Mdl3Records
{
    public static int Run(string[] args)
    {
        if (args.Length < 5) { Console.WriteLine("mdl3-records <files.tsv> <groupRegex> <table> <stride hex> <out.txt>"); return 1; }
        var groupRx = new Regex(args[1]);
        var table = Mdl3Header.Tables.First(t => t.Name == args[2]);
        int stride = Convert.ToInt32(args[3], 16);

        var files = File.ReadAllLines(args[0]).Select(l => l.Split('\t'))
            .Where(p => groupRx.IsMatch(p[1])).Select(p => (path: p[0], group: p[1], version: int.Parse(p[2]))).ToList();

        using var w = new StreamWriter(args[4]);
        foreach (var ver in files.GroupBy(f => f.version).OrderBy(g => g.Key))
            Report(w, ver.Key, ver.Select(f => f.path).ToList(), table, stride);
        Console.WriteLine($"{files.Count} files -> {args[4]}");
        return 0;
    }

    static void Report(StreamWriter w, int version, List<string> paths, Mdl3Header.Table table, int stride)
    {
        var u16 = Enumerable.Range(0, stride / 2).Select(_ => new List<uint>()).ToArray();
        var u32 = Enumerable.Range(0, stride / 4).Select(_ => new List<uint>()).ToArray();
        var region = Enumerable.Range(0, stride / 4).Select(_ => new Dictionary<string, int>()).ToArray();
        int records = 0;

        foreach (var path in paths)
        {
            using var fs = File.OpenRead(path);
            var headBytes = new byte[Mdl3Header.Size];
            fs.ReadExactly(headBytes);
            var head = new BigEndianReader(headBytes);
            int count = head.U16(table.CountOffset);
            uint off = head.U32(table.OffsetOffset);
            if (count == 0 || off == 0) continue;

            var buf = new byte[count * stride];
            fs.Position = off;
            fs.ReadExactly(buf);
            var r = new BigEndianReader(buf);

            // Header blocks sorted by offset: a value v lands in the block with the highest start <= v.
            var blocks = Mdl3Header.Fields.Where(f => f.Kind == Mdl3Header.Kind.Offset32 && head.U32(f.Offset) != 0)
                .Select(f => (start: head.U32(f.Offset), name: f.Name)).OrderBy(b => b.start).ToList();

            for (int i = 0; i < count; i++, records++)
            {
                int b = i * stride;
                for (int c = 0; c < stride / 2; c++) u16[c].Add(r.U16(b + c * 2));
                for (int c = 0; c < stride / 4; c++)
                {
                    uint v = r.U32(b + c * 4);
                    u32[c].Add(v);
                    string where = v == 0 ? "zero" : v >= fs.Length ? "outside" :
                        v < Mdl3Header.Size ? "header" : blocks.LastOrDefault(x => x.start <= v).name ?? "?";
                    region[c][where] = region[c].GetValueOrDefault(where) + 1;
                }
            }
        }

        w.WriteLine($"================ version {version}: {paths.Count} files, {records} {table.Name} records, stride 0x{stride:X}");
        w.WriteLine("u16 col   zero      min      max  distinct  top values");
        for (int c = 0; c < u16.Length; c++) Line(w, $"+{c * 2:X2}", u16[c]);
        w.WriteLine("u32 col   zero      min      max  distinct  top values  | lands in");
        for (int c = 0; c < u32.Length; c++)
        {
            Line(w, $"+{c * 4:X2}", u32[c], end: false);
            w.WriteLine("  | " + string.Join(" ", region[c].OrderByDescending(k => k.Value).Take(4).Select(k => $"{k.Key}:{k.Value}")));
        }
        w.WriteLine();
    }

    static void Line(StreamWriter w, string col, List<uint> v, bool end = true)
    {
        if (v.Count == 0) { w.WriteLine($"{col}  (no data)"); return; }
        var top = v.GroupBy(x => x).OrderByDescending(g => g.Count()).Take(5).Select(g => $"{g.Key:X}x{g.Count()}");
        string s = $"{col}  {v.Count(x => x == 0),6} {v.Min(),8:X} {v.Max(),8:X} {v.Distinct().Count(),8}  {string.Join(" ", top)}";
        if (end) w.WriteLine(s); else w.Write(s);
    }
}
