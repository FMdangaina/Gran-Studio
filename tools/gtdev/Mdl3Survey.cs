using GTFormats;
using GTFormats.Mdl3;

/// <summary>
/// gtdev mdl3-survey &lt;out.txt&gt; &lt;root&gt;...
/// Finds every MDL3 under the roots and checks the header field by field:
/// value ranges per version, file size, offsets in range, table record sizes, magics at offsets.
/// </summary>
static class Mdl3Survey
{
    sealed record Entry(string Path, string Group, long Length, BigEndianReader Head, Dictionary<int, string> MagicAt);

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("mdl3-survey <out.txt> <root>..."); return 1; }
        var entries = new List<Entry>();
        foreach (var root in args.Skip(1))
            Collect(root, entries);

        // File list for the other commands, so they don't rescan the volumes.
        File.WriteAllLines(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, "files.tsv"),
            entries.Select(e => $"{e.Path}\t{e.Group}\t{e.Head.U16(0x0C)}\t{e.Length}"));

        using var w = new StreamWriter(args[0]);
        void P(string s) => w.WriteLine(s);
        Console.WriteLine($"{entries.Count} MDL3 files -> {args[0]}");

        P($"MDL3 files: {entries.Count}");
        foreach (var g in entries.GroupBy(e => e.Group).OrderBy(g => g.Key))
            P($"  {g.Key,-28} {g.Count(),6}  versions: {string.Join(", ", g.GroupBy(e => e.Head.U16(0x0C)).OrderBy(v => v.Key).Select(v => $"{v.Key} x{v.Count()}"))}");

        foreach (var ver in entries.GroupBy(e => e.Head.U16(0x0C)).OrderBy(g => g.Key))
            Report(P, ver.Key, ver.ToList());
        return 0;
    }

    static void Collect(string root, List<Entry> entries)
    {
        string game = Path.GetFileName(root.TrimEnd('\\'));
        var buf = new byte[0x100];
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            using var fs = File.OpenRead(path);
            if (fs.Length < 0x100) continue;
            fs.ReadExactly(buf, 0, 0x100);
            if (buf[0] != 'M' || buf[1] != 'D' || buf[2] != 'L' || buf[3] != '3') continue;

            var head = new BigEndianReader((byte[])buf.Clone());
            var magicAt = new Dictionary<int, string>();
            var tmp = new byte[4];
            foreach (var f in Mdl3Header.Fields.Where(f => f.Kind == Mdl3Header.Kind.Offset32))
            {
                uint off = head.U32(f.Offset);
                if (off == 0 || off + 4 > fs.Length) continue;
                fs.Position = off;
                fs.ReadExactly(tmp);
                magicAt[f.Offset] = tmp.All(b => b is >= 0x20 and < 0x7F) ? System.Text.Encoding.ASCII.GetString(tmp) : "";
            }
            entries.Add(new Entry(path, GroupOf(game, Path.GetRelativePath(root, path)), fs.Length, head, magicAt));
        }
    }

    // GT5: car\hq\00010007 -> GT5 car\hq ; GT6: car\0001\0001\hq\body -> GT6 car\hq\body
    static string GroupOf(string game, string rel)
    {
        var p = rel.Split('\\');
        string g = game.Replace("_VOL", "");
        if (p[0] == "car" && g == "GT6" && p.Length >= 4)
            return $"{g} car\\{string.Join("\\", p.Skip(3))}";
        if (p[0] == "car" && p.Length >= 3)
            return $"{g} car\\{p[1]}";
        return $"{g} {p[0]}";
    }

    static void Report(Action<string> P, int version, List<Entry> files)
    {
        P("");
        P($"================ version {version}: {files.Count} files ({string.Join(", ", files.GroupBy(f => f.Group).Select(g => $"{g.Key} {g.Count()}"))})");

        int sizeOk = files.Count(f => f.Head.U32(0x04) == f.Length);
        P($"fileSize == real length: {sizeOk}/{files.Count}");
        foreach (var bad in files.Where(f => f.Head.U32(0x04) != f.Length).Take(3))
            P($"  e.g. {bad.Path}: field 0x{bad.Head.U32(0x04):X} real 0x{bad.Length:X}");

        P("");
        P("field                     off   zero/n        min        max  distinct  top values");
        foreach (var f in Mdl3Header.Fields)
        {
            var v = files.Select(e => f.Kind is Mdl3Header.Kind.Count16 or Mdl3Header.Kind.Value16 ? e.Head.U16(f.Offset) : e.Head.U32(f.Offset)).ToList();
            var top = v.GroupBy(x => x).OrderByDescending(g => g.Count()).Take(4).Select(g => $"{g.Key:X}x{g.Count()}");
            P($"{f.Name,-24} {f.Offset:X3}  {v.Count(x => x == 0),5}/{v.Count,-5} {v.Min(),10:X} {v.Max(),10:X} {v.Distinct().Count(),8}  {string.Join(" ", top)}");
        }

        P("");
        P("offset field              out-of-range  magics at offset");
        foreach (var f in Mdl3Header.Fields.Where(f => f.Kind == Mdl3Header.Kind.Offset32))
        {
            var nz = files.Where(e => e.Head.U32(f.Offset) != 0).ToList();
            if (nz.Count == 0) continue;
            int oor = nz.Count(e => e.Head.U32(f.Offset) >= e.Length);
            var mags = nz.Select(e => e.MagicAt.GetValueOrDefault(f.Offset, "")).Where(m => m != "").GroupBy(m => m)
                         .OrderByDescending(g => g.Count()).Take(3).Select(g => $"'{g.Key}'x{g.Count()}");
            P($"{f.Name,-24} {oor,6}/{nz.Count,-6}  {string.Join(" ", mags)}");
        }

        // Record size: gap from the table offset to the next offset in the header (or end of file), divided by count.
        // The real record size is <= every gap/count, so the minimum over all files is the best upper bound;
        // "exact" counts the files where gap == count * that size (no padding/other data in between).
        P("");
        P("table            files  min gap/count  exact  gap/count values");
        foreach (var t in Mdl3Header.Tables)
        {
            var ratios = new List<(long gap, uint count)>();
            foreach (var e in files)
            {
                uint count = e.Head.U16(t.CountOffset), off = e.Head.U32(t.OffsetOffset);
                if (count == 0 || off == 0) continue;
                long next = Mdl3Header.Fields.Where(f => f.Kind == Mdl3Header.Kind.Offset32)
                    .Select(f => (long)e.Head.U32(f.Offset)).Where(o => o > off).DefaultIfEmpty(e.Length).Min();
                ratios.Add((next - off, count));
            }
            if (ratios.Count == 0) { P($"{t.Name,-16} {0,5}"); continue; }
            long minStride = ratios.Min(r => r.gap / r.count);
            int exact = ratios.Count(r => r.gap == r.count * minStride);
            var vals = ratios.Select(r => r.gap % r.count == 0 ? $"{r.gap / r.count:X}" : "frac").GroupBy(x => x)
                             .OrderByDescending(g => g.Count()).Take(5).Select(g => $"{g.Key}x{g.Count()}");
            P($"{t.Name,-16} {ratios.Count,5}  {minStride,13:X}  {exact,5}  {string.Join(" ", vals)}");
        }

        // Most common order of the non-zero offsets: tells which block follows which.
        P("");
        P("most common offset order:");
        var orders = files.Select(e => string.Join(" < ", Mdl3Header.Fields.Where(f => f.Kind == Mdl3Header.Kind.Offset32 && e.Head.U32(f.Offset) != 0)
            .OrderBy(f => e.Head.U32(f.Offset)).Select(f => f.Offset.ToString("X2")))).GroupBy(o => o).OrderByDescending(g => g.Count()).Take(3);
        foreach (var o in orders) P($"  x{o.Count()}: {o.Key}");
    }
}
