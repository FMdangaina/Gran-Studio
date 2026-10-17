using System.Text.RegularExpressions;
using GTFormats;
using GTFormats.Mdl3;
using GTFormats.Txs3;

/// <summary>
/// gtdev body-s &lt;files.tsv&gt; &lt;groupRegex&gt; &lt;out.txt&gt;   check the GT6 body_s map (header 0xDC) on every car body:
/// streams tile body_s, every chunk inflates to its size, bundle headers agree with the map, and every image with
/// pixel offset 0 is found in a bundle with room for its size, then decoded.
/// gtdev body-s &lt;body file&gt; &lt;out.txt&gt;   list the streams and bundle contents of one car.
/// </summary>
static class BodySCheck
{
    public static int Run(string[] args)
    {
        if (args.Length == 2) return OneFile(args[0], args[1]);
        if (args.Length < 3) { Console.WriteLine("body-s <files.tsv> <groupRegex> <out.txt> | <body> <out.txt>"); return 1; }
        var rx = new Regex(args[1]);
        var files = File.ReadAllLines(args[0]).Select(l => l.Split('\t')).Where(p => rx.IsMatch(p[1])).ToList();
        using var w = new StreamWriter(args[2]);
        foreach (var grp in files.GroupBy(p => p[1] + " v" + p[2]).OrderBy(g => g.Key))
        {
            var c = new SortedDictionary<string, long>();
            var ex = new Dictionary<string, string>();
            foreach (var p in grp)
            {
                void Count(string k, string? d = null, long n = 1)
                {
                    c[k] = c.GetValueOrDefault(k) + n;
                    if (d != null && !ex.ContainsKey(k)) ex[k] = p[0] + " " + d;
                }
                bool Expect(bool ok, string k, string? d = null) { Count((ok ? "ok: " : "FAIL: ") + k, ok ? null : d); return ok; }
                try { CheckFile(p[0], Count, Expect); }
                catch (Exception e) { Count("FAIL: exception " + e.GetType().Name, e.Message); }
            }
            w.WriteLine($"================ {grp.Key}: {grp.Count()} files");
            foreach (var (k, n) in c) w.WriteLine($"{n,11}  {k}{(ex.TryGetValue(k, out var f) ? "   e.g. " + f : "")}");
            w.WriteLine();
            w.Flush();
        }
        Console.WriteLine($"{files.Count} files -> {args[2]}");
        return 0;
    }

    static void CheckFile(string path, Action<string, string?, long> count, Func<bool, string, string?, bool> expect)
    {
        void Count(string k, string? d = null) => count(k, d, 1);
        var file = new Mdl3File(File.ReadAllBytes(path));
        var r = file.Data;
        var images = Txs3Image.ReadAll(r, (int)r.U32(0x48));
        var pitch = file.TextureInfos.GroupBy(t => t.ImageIndex).ToDictionary(g => g.Key, g => g.First().Pitch);
        var names = file.TextureInfos.GroupBy(t => t.ImageIndex).ToDictionary(g => g.Key, g => g.First().Name);
        var map = Mdl3StreamMap.Read(r);
        string sPath = path + "_s";
        if (map == null) { Count("no stream map (0xDC = 0)"); return; }
        if (!expect(File.Exists(sPath), "body_s exists", null)) return;
        var bodyS = File.ReadAllBytes(sPath);
        var streams = map.Streams().ToList();

        // Streams tile body_s: sorted by offset, each starts where the previous ends, the last ends at the file end.
        long end = 0; bool tiled = true;
        foreach (var s in streams.Select(s => s.stream).OrderBy(s => s.Offset))
        {
            if (s.Offset != end) tiled = false;
            end = s.Offset + s.StoredSize;
        }
        expect(tiled && end == bodyS.Length, "streams tile body_s exactly (no gap, no overlap, end = file size)", $"end 0x{end:X} size 0x{bodyS.Length:X}");
        Count("stat: streams", null);
        count("stat: streams total", null, streams.Count - 1);
        foreach (var g in map.Groups) Count($"stat: group '{g.Name}' items {g.Items.Count}");

        var found = new Dictionary<int, List<string>>();
        var firstCopy = new Dictionary<int, byte[]>();
        foreach (var (gname, item, sub, s) in streams)
        {
            string where = $"{gname}[{item}]{(sub >= 0 ? "." + sub : "")}";
            expect(s.HeaderChunks.Count > 0, "stream has a header chunk", where);
            foreach (var ch in s.HeaderChunks.Concat(s.DataChunks))
                expect(ch.Deflated ? (ch.StoredSize & 0x7FFF0000) == 0 : ch.StoredSize == ch.Size,
                    "chunk sizes (deflated: stored < 64 KB; raw: stored = size)", where);
            byte[] bytes;
            try { bytes = s.Load(bodyS); }
            catch (Exception e) { expect(false, "chunks inflate to their sizes", where + " " + e.Message); continue; }
            expect(true, "chunks inflate to their sizes", null);
            var b = new Mdl3StreamBundle(bytes);
            expect(b.R.U32(0) == 0 && b.R.U32(0x10) == 0, "bundle +00 and +10 zero", where);
            static int Pad80(int n) => (n + 0x7F) & ~0x7F;
            expect(b.HeaderSize == s.HeaderSize && b.TotalSize == b.HeaderSize + b.DataSize
                && (b.DataSize == s.DataSize || Pad80(b.DataSize) == s.DataSize && s.DataChunks[^1].Deflated),
                "bundle header sizes = map chunk sizes (map pads a deflated data chunk to 0x80)", $"{where} {b.HeaderSize:X} {b.DataSize:X} {b.TotalSize:X}");
            if (b.DataSize != s.DataSize) Count("stat: bundle data size not 0x80-aligned (map has it padded)");
            if (b.ImageCount == 0 && b.ShapeCount == 0) { Count("stat: empty bundle (0x80 header only)"); continue; }
            if (b.ImageCount > 0)
                expect(b.ImageTable + b.ImageCount * 0x10 <= b.HeaderSize && b.ImageTable >= b.ShapeTable + b.ShapeCount * 0x20,
                    "image table inside the header, after the shape table", where);
            Count($"stat: bundle with shapes {(b.ShapeCount > 0 ? "yes" : "no")}, images {(b.ImageCount > 0 ? "yes" : "no")}");

            // Byte claims inside the bundle: 0x20 header, shape table, image table, image pixels; overlaps fail.
            var owner = new byte[bytes.Length];
            bool overlap = false;
            void Claim(int at, long n, byte who)
            {
                for (long i = at; i < at + n && i < owner.Length; i++) { if (owner[i] != 0) overlap = true; owner[i] = who; }
            }
            Claim(0, 0x20, 1);
            Claim(b.ShapeTable, b.ShapeCount * 0x20L, 1);
            Claim(b.ImageTable, b.ImageCount * 0x10L, 1);
            foreach (var (img, off) in b.Images())
            {
                if (!expect(img < images.Count, "image index < TXS3 image count", where)) continue;
                var im = images[img];
                expect(off % 0x80 == 0, "pixel offset 0x80-aligned", where);
                if (!expect(off >= 0x20 && off + im.Size <= b.TotalSize, "pixels inside the bundle", $"{where} img {img}")) continue;
                Count(off < b.HeaderSize ? "stat: image pixels in the header part" : "stat: image pixels in the data part");
                Claim(off, im.Size, 2);
                expect(im.Offset == 0, "image found in body_s has offset 0 in body", $"{where} img {img}");
                if (!found.TryGetValue(img, out var l)) found[img] = l = [];
                l.Add(gname + (sub >= 0 ? " sub" : ""));
                var px = bytes.AsSpan(off, (int)im.Size);
                if (!firstCopy.TryGetValue(img, out var first)) firstCopy[img] = px.ToArray();
                else Count(px.SequenceEqual(first) ? "stat: extra copy of an image, same pixels"
                    : $"stat: extra copy of an image, DIFFERENT pixels ({(names.GetValueOrDefault(img, "").EndsWith("ColorShiftBRDF") ? "colour-shift BRDF" : "other")})");
                var rgba = im.DecodeLevel0(bytes.AsSpan(off, (int)im.Size), pitch.GetValueOrDefault(img));
                expect(rgba != null, "decoded " + Txs3Survey.FormatName(im.Format), where);
            }
            expect(!overlap, "header, tables and images do not overlap", where);
            // Bytes nobody claims: shape data (item 4/5) or padding.
            long hdrNz = 0, dataNz = 0;
            for (int i = 0; i < bytes.Length; i++)
                if (owner[i] == 0 && bytes[i] != 0) { if (i < b.HeaderSize) hdrNz++; else dataNz++; }
            count("stat: unclaimed non-zero bytes in the header part (shape data)", null, hdrNz);
            count("stat: unclaimed non-zero bytes in the data part", null, dataNz);
            if (hdrNz > 0 && b.ShapeCount == 0) Count("unclaimed non-zero header bytes in a bundle without shapes", where);
            if (dataNz > 0) Count(b.ShapeCount > 0 ? "stat: unclaimed non-zero data bytes in a bundle with shapes" : "unclaimed non-zero data bytes in a bundle WITHOUT shapes", where);
        }

        for (int i = 0; i < images.Count; i++)
        {
            if (images[i].Offset != 0) { Count("stat: image in body itself"); continue; }
            found.TryGetValue(i, out var l);
            expect(l != null, "image with offset 0 found in body_s", $"img {i}");
            if (l == null) continue;
            bool brdf = names.GetValueOrDefault(i, "").EndsWith("ColorShiftBRDF");
            Count(l.Count == 1 ? $"stat: image in 1 bundle ({l[0]})"
                : $"stat: image in {(l.Count < 5 ? l.Count.ToString() : "5+")} bundles, {(brdf ? "colour-shift BRDF" : "other")}, {string.Join(",", l.Distinct().Order())}");
        }
    }

    static int OneFile(string path, string outPath)
    {
        var file = new Mdl3File(File.ReadAllBytes(path));
        var images = Txs3Image.ReadAll(file.Data, (int)file.Data.U32(0x48));
        var names = file.TextureInfos.GroupBy(t => t.ImageIndex).ToDictionary(g => g.Key, g => g.First().Name);
        var map = Mdl3StreamMap.Read(file.Data) ?? throw new InvalidDataException("no stream map");
        var bodyS = File.ReadAllBytes(path + "_s");
        using var w = new StreamWriter(outPath);
        w.WriteLine($"{path}: {images.Count} images, body_s 0x{bodyS.Length:X}");
        foreach (var (g, item, sub, s) in map.Streams())
        {
            var b = new Mdl3StreamBundle(s.Load(bodyS));
            w.WriteLine($"'{g}' item {item}{(sub >= 0 ? " sub " + sub : "")}: body_s 0x{s.Offset:X} stored 0x{s.StoredSize:X}, " +
                $"header 0x{b.HeaderSize:X} ({s.HeaderChunks.Count} chunks) data 0x{b.DataSize:X} ({s.DataChunks.Count} chunks), " +
                $"{b.ShapeCount} shapes, {b.ImageCount} images");
            if (b.ShapeCount > 0) w.WriteLine("    shapes " + string.Join(" ", b.Shapes()));
            foreach (var (img, off) in b.Images())
                w.WriteLine($"    image {img,3} at 0x{off:X6} size 0x{images[img].Size:X6} {Txs3Survey.FormatName(images[img].Format)} " +
                    $"{images[img].Width}x{images[img].Height} {names.GetValueOrDefault(img, "")}");
        }
        Console.WriteLine($"-> {outPath}");
        return 0;
    }
}
