using System.Text.RegularExpressions;
using GTFormats;
using GTFormats.Mdl3;
using GTFormats.Txs3;

/// <summary>
/// gtdev txs3-png &lt;file&gt; &lt;outDir&gt;   decode every TXS3 image of one MDL3 (mip 0, faces/slices stacked) to PNG
/// gtdev txs3-png &lt;files.tsv&gt; &lt;groupRegex&gt; &lt;out.txt&gt;   decode every image of every file, counters only
/// </summary>
static class Txs3Png
{
    public static int Run(string[] args)
    {
        int scale = 1; bool split = false; string? only = null;
        var rest = new List<string>();
        for (int i = 0; i < args.Length; i++)
            if (args[i] == "--scale") scale = int.Parse(args[++i]);
            else if (args[i] == "--split") split = true;
            else if (args[i] == "--only") only = args[++i];
            else rest.Add(args[i]);
        args = [.. rest];
        if (args.Length == 2) return OneFile(args[0], args[1], scale, split, only);
        if (args.Length < 3) { Console.WriteLine("txs3-png <file> <outDir> | <files.tsv> <groupRegex> <out.txt>"); return 1; }
        var rx = new Regex(args[1]);
        var files = File.ReadAllLines(args[0]).Select(l => l.Split('\t')).Where(p => rx.IsMatch(p[1])).ToList();
        using var w = new StreamWriter(args[2]);
        foreach (var grp in files.GroupBy(p => p[1] + " v" + p[2]).OrderBy(g => g.Key))
        {
            var c = new SortedDictionary<string, int>();
            var ex = new Dictionary<string, string>();
            foreach (var p in grp)
            {
                void Count(string k, string? d = null)
                {
                    c[k] = c.GetValueOrDefault(k) + 1;
                    if (d != null && !ex.ContainsKey(k)) ex[k] = p[0] + " " + d;
                }
                try
                {
                    foreach (var (img, pitch, _) in Images(new BigEndianReader(File.ReadAllBytes(p[0])), out var r))
                    {
                        string d = $"{Txs3Survey.FormatName(img.Format)} {img.Width}x{img.Height}";
                        if (img.Offset == 0) { Count("image offset 0 (pixels not in this file)"); continue; }
                        if (!img.SizeMatches(pitch)) { Count("fail: size rule", d); continue; }
                        var rgba = img.DecodeLevel0(r.Data.AsSpan((int)img.Offset, (int)img.Size), pitch);
                        Count(rgba == null ? "fail: format not decoded" : "ok: decoded " + Txs3Survey.FormatName(img.Format), rgba == null ? d : null);
                    }
                }
                catch (Exception e) { Count("fail: exception " + e.GetType().Name, e.Message); }
            }
            w.WriteLine($"================ {grp.Key}: {grp.Count()} files");
            foreach (var (k, n) in c) w.WriteLine($"{n,9}  {k}{(ex.TryGetValue(k, out var f) ? "   e.g. " + f : "")}");
            w.WriteLine();
        }
        Console.WriteLine($"{files.Count} files -> {args[2]}");
        return 0;
    }

    /// <summary>Every image with the pitch and name of its first texture info.</summary>
    static IEnumerable<(Txs3Image img, int pitch, string name)> Images(BigEndianReader data, out BigEndianReader r)
    {
        r = data;
        var file = new Mdl3File(data.Data);
        var images = Txs3Image.ReadAll(data, (int)data.U32(0x48));
        var users = file.TextureInfos.GroupBy(t => t.ImageIndex).ToDictionary(g => g.Key, g => g.First());
        return images.Select((img, i) => users.TryGetValue(i, out var t) ? (img, t.Pitch, t.Name) : (img, 0, ""));
    }

    static int OneFile(string path, string outDir, int scale, bool split, string? only)
    {
        Directory.CreateDirectory(outDir);
        int i = 0, written = 0;
        var data = new BigEndianReader(File.ReadAllBytes(path));
        // GT6 car bodies: images with offset 0 are in body_s (every bundle holding one is exported, variants get a suffix).
        var inBodyS = File.Exists(path + "_s") && Mdl3StreamMap.Read(data) is { } map
            ? map.FindImages(File.ReadAllBytes(path + "_s")) : [];
        foreach (var (img, pitch, name) in Images(data, out var r))
        {
            string shortName = Regex.Replace(Path.GetFileNameWithoutExtension(name.Replace('\\', '/').Split('/')[^1]), @"[^\w\-]", "_");
            string info = $"{i:D3} {Txs3Survey.FormatName(img.Format)} {img.Width}x{img.Height}{(img.Depth > 1 ? "x" + img.Depth : "")}{(img.Cube ? " cube" : "")} mips {img.Mipmaps} {name}";
            i++;
            if (only != null && !only.Split(',').Contains((i - 1).ToString())) continue;
            var sources = new List<(byte[] bytes, int offset, string where)>();
            if (img.Offset != 0) sources.Add((r.Data, (int)img.Offset, ""));
            else if (inBodyS.TryGetValue(i - 1, out var l)) sources.AddRange(l.Select(x => (x.bundle, x.offset, x.where)));
            if (sources.Count == 0) { Console.WriteLine(info + "  -> offset 0, not in body_s, skipped"); continue; }
            for (int v = 0; v < sources.Count; v++)
            {
                var (bytes, offset, where) = sources[v];
                var rgba = img.DecodeLevel0(bytes.AsSpan(offset, (int)img.Size), pitch);
                if (rgba == null) { Console.WriteLine(info + "  -> format not decoded"); continue; }
                int w = img.Width, h = img.Height * img.Faces * img.Depth;
                string baseName = Path.Combine(outDir, $"{i - 1:D3}_{shortName}{(sources.Count > 1 ? "_v" + v : "")}");
                if (split)
                {
                    var rgb = (byte[])rgba.Clone();
                    var a = (byte[])rgba.Clone();
                    for (int p = 0; p < rgba.Length; p += 4)
                    {
                        rgb[p + 3] = 255;
                        a[p] = a[p + 1] = a[p + 2] = rgba[p + 3]; a[p + 3] = 255;
                    }
                    Png.Write(baseName + "_rgb.png", Scale(rgb, w, h, scale), w * scale, h * scale);
                    Png.Write(baseName + "_a.png", Scale(a, w, h, scale), w * scale, h * scale);
                }
                else Png.Write(baseName + ".png", Scale(rgba, w, h, scale), w * scale, h * scale);
                written++;
                Console.WriteLine(info + (where != "" ? $"  [body_s {where}]" : ""));
            }
        }
        Console.WriteLine($"{written} PNG -> {outDir}");
        return 0;
    }

    /// <summary>Nearest-neighbour enlargement, to look at small images.</summary>
    static byte[] Scale(byte[] rgba, int w, int h, int s)
    {
        if (s == 1) return rgba;
        var o = new byte[rgba.Length * s * s];
        for (int y = 0; y < h * s; y++)
            for (int x = 0; x < w * s; x++)
                Array.Copy(rgba, ((y / s) * w + x / s) * 4, o, (y * w * s + x) * 4, 4);
        return o;
    }
}
