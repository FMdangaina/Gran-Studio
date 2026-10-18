using System.Text.RegularExpressions;
using GTFormats.Mdl3;

/// <summary>
/// bodys-subs &lt;files.tsv&gt; &lt;groupRegex&gt; &lt;out.txt&gt;: item 5a, what the `/` sub streams of the GT6 body_s hold.
/// Per car: sub stream count and per sub (shapes, images); totals: sub counts, which subs have data, image names,
/// the samplers and materials that use the sub images. Also writes &lt;out&gt;.tsv (one line per body) for joins.
/// </summary>
static class BodySSubs
{
    public static int Run(string[] args)
    {
        if (args.Length == 1) return OneFile(args[0]);
        if (args.Length < 3) { Console.Error.WriteLine("bodys-subs <files.tsv> <groupRegex> <out.txt> | <body>"); return 1; }
        var rx = new Regex(args[1]);
        var files = File.ReadLines(args[0]).Select(l => l.Split('\t')).Where(c => rx.IsMatch(c[1])).ToList();
        var c = new SortedDictionary<string, int>();
        var ex = new Dictionary<string, string>();
        void Count(string k, string? e = null, int n = 1)
        {
            c[k] = c.GetValueOrDefault(k) + n;
            if (e != null && !ex.ContainsKey(k)) ex[k] = e;
        }
        using var tsv = new StreamWriter(Path.ChangeExtension(args[2], ".tsv"));
        tsv.WriteLine("file\tsubs\tsignature (shapes/images per sub)\tBRDF images per sub");
        int done = 0;
        foreach (var f in files)
        {
            string path = f[0];
            try
            {
                var m = Mdl3File.Load(path);
                var map = Mdl3StreamMap.Read(m.Data);
                if (map == null) { Count("no stream map"); continue; }
                var bodyS = File.ReadAllBytes(path + "_s");
                string car = Regex.Replace(path.Replace('\\', '/'), @"^.*/car/", "");
                var names = m.TextureInfos.GroupBy(t => t.ImageIndex).ToDictionary(g => g.Key, g => g.First().Name);
                // texture info index -> image index, to find which materials sample the sub images
                var samplersOf = new Dictionary<int, List<string>>();
                foreach (var mat in m.Materials)
                    foreach (var k in mat.Samplers.Where(k => !k.External && k.Index < m.TextureInfos.Count))
                    {
                        int img = m.TextureInfos[k.Index].ImageIndex;
                        if (!samplersOf.TryGetValue(img, out var l)) samplersOf[img] = l = [];
                        l.Add($"{k.Sampler} in '{mat.Name}'");
                    }

                Count("stat: files");
                foreach (var g in map.Groups)
                    for (int i = 0; i < g.Items.Count; i++)
                        Count($"subs per item: group {(g.Name == "/" ? "/" : "custom_*")} item {(g.Name == "/" ? i.ToString() : "any")}: {g.Items[i].Subs.Count}");

                var root = map.Groups.FirstOrDefault(g => g.Name == "/");
                if (root == null) { Count("FAIL: no '/' group", car); continue; }
                Count($"'/' items: {root.Items.Count}", car);
                var sig = new List<string>();
                var brdf = new List<int>();
                var main = new Mdl3StreamBundle(root.Items[0].Stream.Load(bodyS));
                var mainImages = main.Images().Select(x => x.image).ToHashSet();
                List<string>[] halfSets = [[], []];
                for (int s = 0; s < root.Items[0].Subs.Count; s++)
                {
                    var b = new Mdl3StreamBundle(root.Items[0].Subs[s].Load(bodyS));
                    var imgs = b.Images().Select(x => x.image).ToList();
                    sig.Add(b.ShapeCount == 0 && imgs.Count == 0 ? "-" : $"{b.ShapeCount}/{imgs.Count}");
                    int nb = imgs.Count(i => names.GetValueOrDefault(i, "").EndsWith("ColorShiftBRDF"));
                    brdf.Add(nb);
                    if (b.ShapeCount > 0) Count($"sub {s} has shapes", car);
                    foreach (int i in imgs)
                    {
                        string n = names.GetValueOrDefault(i, "?");
                        Count($"sub image name: {(n.Contains('/') ? "(path) " + Path.GetFileName(n) : n)}", $"{car} sub {s}");
                        if (mainImages.Contains(i)) Count("sub image also in the '/' item bundle", $"{car} sub {s} img {i}");
                        foreach (var who in samplersOf.GetValueOrDefault(i, ["(no material samples it)"]))
                            Count($"sub image sampled by: {Regex.Replace(who, @"'.*'", q => Regex.Replace(q.Value, @"\d+", "#"))}");
                    }
                    // colour variation numbers (material name ..._CLR_VARI_<n>) of the materials sampling this sub's images
                    var varis = imgs.SelectMany(i => samplersOf.GetValueOrDefault(i, []))
                        .Select(who => Regex.Match(who, @"_CLR_VARI_(\d+)")).Where(q => q.Success)
                        .Select(q => int.Parse(q.Groups[1].Value)).Distinct().Order().ToList();
                    int hk = root.Items[0].Subs.Count / 2;
                    if (imgs.Count > 0)
                        Count(varis.Count == 0 ? "rule: sub images not sampled by any CLR_VARI material"
                            : varis.Contains(s % hk) ? "rule OK: sub k of a half used by CLR_VARI_k" + (varis.Count > 1 ? " (and others)" : " only")
                            : $"rule FAIL (half {(s < hk ? "A" : "B")}): sub k of a half NOT used by CLR_VARI_k", $"{car} sub {s} of {root.Items[0].Subs.Count}: {string.Join(",", varis)}");
                    halfSets[s < hk ? 0 : 1].Add(string.Join(",", imgs.Order()));
                    if (imgs.Count > 0)
                    {
                        string rel =varis.Count == 0 ? "no CLR_VARI material" : varis.Count > 1 ? "several CLR_VARI: " + string.Join(",", varis)
                            : varis[0] == s ? "VARI = s" : varis[0] == s / 2 ? "VARI = s/2" : varis[0] == s - root.Items[0].Subs.Count / 2 ? "VARI = s - subs/2" : $"VARI {varis[0]} for s {s}";
                        Count($"sub -> colour: {rel}", $"{car} sub {s} of {root.Items[0].Subs.Count}");
                    }
                    int half = root.Items[0].Subs.Count / 2;
                    var users = imgs.SelectMany(i => samplersOf.GetValueOrDefault(i, [])).Select(who => who[(who.IndexOf('\'') + 1)..^1]).Distinct().ToList();
                    if (imgs.Count > 0)
                    {
                        bool last = s % half == half - 1, extra = users.Count > 0 && users.All(u => u.EndsWith("_PaintExtraBRDF"));
                        Count($"slot {(last ? "last" : "0..V-1")}: materials {(extra ? "all *_PaintExtraBRDF" : users.Any(u => u.EndsWith("_PaintExtraBRDF")) ? "some *_PaintExtraBRDF" : "no *_PaintExtraBRDF")}", $"{car} sub {s}");
                        // model (first path node) of the shapes drawn with these materials
                        var matIdx = users.SelectMany(u => Enumerable.Range(0, m.Materials.Count).Where(q => m.Materials[q].Name == u)).ToHashSet();
                        foreach (var model in Enumerable.Range(0, m.Shapes.Count).Where(q => matIdx.Contains(m.Shapes[q].MaterialIndex))
                            .Select(q => m.ShapeNames.TryGetValue(q, out var pn) ? ShapeNaming.Nodes(pn[0]).FirstOrDefault() ?? "?" : "(no name)").Distinct())
                            Count($"half {(s < half ? "A" : "B")} model: {Regex.Replace(model, @"\d+", "#")}");
                    }
                    foreach (var matName in imgs.SelectMany(i => samplersOf.GetValueOrDefault(i, [])).Select(who => who[(who.IndexOf('\'') + 1)..^1]).Distinct())
                        Count($"half {(s < half ? "A" : "B")} material: {Regex.Replace(Regex.Replace(matName, @"_CLR_VARI_\d+", "_CLR_VARI_n"), @"\d+", "#")}");
                    if (imgs.Count > 0 && (s % half) == half - 1) Count("last slot of a half NOT empty", $"{car} sub {s}");
                    if (imgs.Count > 0) Count($"non-empty sub: index {s} of {root.Items[0].Subs.Count}");
                    if (imgs.Count > 0) Count($"non-empty sub: {nb} BRDF images, {imgs.Count - nb} others");
                }
                Count($"sub count {root.Items[0].Subs.Count}", car);
                for (int h = 0; h < 2; h++)
                {
                    var sets = halfSets[h].Take(halfSets[h].Count - 1).ToList(); // without the last (empty) slot
                    if (sets.Count == 0) continue;
                    string kind = sets.All(x => x == "") ? "all empty" : sets.Distinct().Count() == 1 ? "same images in every sub"
                        : sets.Distinct().Count() == sets.Count ? "every sub different" : "some subs share images";
                    Count($"half {(h == 0 ? "A" : "B")}: {kind}", car);
                }
                var allVari = m.Materials.Select(q => Regex.Match(q.Name, @"_CLR_VARI_(\d+)")).Where(q => q.Success)
                    .Select(q => int.Parse(q.Groups[1].Value)).Distinct().ToList();
                int nVari = allVari.Count == 0 ? 0 : allVari.Max() + 1;
                Count($"subs vs CLR_VARI count: {(nVari == 0 ? "no CLR_VARI" : root.Items[0].Subs.Count == nVari ? "subs = vari" : root.Items[0].Subs.Count == 2 * nVari ? "subs = 2 x vari" : $"subs {root.Items[0].Subs.Count}, vari {nVari}")}"
                    + (allVari.Count != nVari ? " (vari numbers not contiguous)" : ""), car);
                Count($"signature {string.Join(",", sig)}", car);
                // BRDF images in the main '/' bundle (not in a sub)?
                int mainBrdf = mainImages.Count(i => names.GetValueOrDefault(i, "").EndsWith("ColorShiftBRDF"));
                if (mainBrdf > 0) Count("'/' item bundle itself holds BRDF images", car);
                int allBrdf = names.Count(x => x.Value.EndsWith("ColorShiftBRDF"));
                Count($"BRDF images in the file {(allBrdf == brdf.Sum() + mainBrdf ? "= " : "!= ")}sum over the bundles", car);
                tsv.WriteLine($"{car}\t{root.Items[0].Subs.Count}\t{string.Join(",", sig)}\t{string.Join(",", brdf)}");
            }
            catch (Exception e) { Count("FAIL: exception " + e.GetType().Name, path + " " + e.Message); }
            if (++done % 300 == 0) Console.WriteLine($"{done}/{files.Count}");
        }
        using var w = new StreamWriter(args[2]);
        w.WriteLine($"{done} files ({args[1]})");
        foreach (var (k, n) in c.OrderBy(x => x.Key))
            if (k.Length > 0) w.WriteLine($"{n,8}  {k}{(ex.TryGetValue(k, out var e) ? "   e.g. " + e : "")}");
        Console.WriteLine($"{done} files -> {args[2]}");
        return 0;
    }

    /// <summary>One body: every sub stream of every item with its images, their pixel hash and the materials sampling them.</summary>
    static int OneFile(string path)
    {
        var m = Mdl3File.Load(path);
        var map = Mdl3StreamMap.Read(m.Data) ?? throw new InvalidDataException("no stream map");
        var bodyS = File.ReadAllBytes(path + "_s");
        var names = m.TextureInfos.GroupBy(t => t.ImageIndex).ToDictionary(g => g.Key, g => g.First().Name);
        var samplersOf = new Dictionary<int, List<string>>();
        foreach (var mat in m.Materials)
            foreach (var k in mat.Samplers.Where(k => !k.External && k.Index < m.TextureInfos.Count))
            {
                int img = m.TextureInfos[k.Index].ImageIndex;
                if (!samplersOf.TryGetValue(img, out var l)) samplersOf[img] = l = [];
                l.Add($"{k.Sampler.Replace("MapSampler", "")}@{mat.Name}");
            }
        var vari = m.Materials.Select(q => Regex.Match(q.Name, @"_CLR_VARI_(\d+)")).Where(q => q.Success).Select(q => q.Groups[1].Value).Distinct();
        Console.WriteLine($"{path}: {m.Materials.Count} materials, CLR_VARI {string.Join(",", vari)}");
        var images = GTFormats.Txs3.Txs3Image.ReadAll(m.Data, (int)m.Data.U32(0x48));
        foreach (var g in map.Groups)
            for (int i = 0; i < g.Items.Count; i++)
                for (int s = 0; s < g.Items[i].Subs.Count; s++)
                {
                    var b = new Mdl3StreamBundle(g.Items[i].Subs[s].Load(bodyS));
                    Console.WriteLine($"'{g.Name}'[{i}] sub {s}/{g.Items[i].Subs.Count}: {b.ShapeCount} shapes, {b.ImageCount} images");
                    foreach (var (img, off) in b.Images())
                    {
                        var px = b.Bytes.AsSpan(off, (int)images[img].Size);
                        uint h = 2166136261; foreach (byte x in px) h = (h ^ x) * 16777619;
                        Console.WriteLine($"    img {img,3} {images[img].Width}x{images[img].Height} hash {h:X8} {Path.GetFileName(names.GetValueOrDefault(img, "?"))}  <- {string.Join(", ", samplersOf.GetValueOrDefault(img, []))}");
                    }
                }
        return 0;
    }
}
