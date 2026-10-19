using System.Globalization;
using GTFormats.Mdl3;

/// <summary>
/// tex-full &lt;out.txt&gt; &lt;file&gt;... (ROADMAP 12a/12c, a few files only): per used material the name, the shader, every
/// sampler with where its pixels are (file, body_s, external n, not found), the colour constants, and the object
/// names of the shapes that use it. Summary: why a shape has no diffuse texture in the viewer.
/// </summary>
static class TexFull
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("tex-full <out.txt> <file>... | tex-full --vari <file>"); return 1; }
        if (args[0] == "--vari") return Variations(args[1]);
        var ci = CultureInfo.InvariantCulture;
        using var w = new StreamWriter(args[0]);
        var total = new Dictionary<string, int>();
        var constantKeys = new Dictionary<string, int>();
        foreach (var path in args[1..])
        {
            var m = Mdl3File.Load(path);
            var set = new Mdl3TextureSet(m);
            var shapes = Mdl3Obj.SelectShapes(m, false);
            var byMat = shapes.GroupBy(s => (int)m.Shapes[s].MaterialIndex).OrderBy(g => g.Key).ToList();
            var why = new Dictionary<string, int>();
            w.WriteLine($"=== {path}: {shapes.Count} shapes, {byMat.Count} materials, {m.Materials.Count} in file, {set.Entries.Count} images, {set.External.Count} external refs");
            foreach (var g in byMat)
            {
                if (g.Key < 0 || g.Key >= m.Materials.Count) { w.WriteLine($"  m{g.Key}: no material"); continue; }
                var mat = m.Materials[g.Key];
                var data = mat.DataIndex >= 0 && mat.DataIndex < m.MaterialData.Count ? m.MaterialData[mat.DataIndex] : null;
                string state = Status(m, set, mat, "diffuseMapSampler");
                if (state == "none" && mat["colorMapSampler"] != null) state = "color: " + Status(m, set, mat, "colorMapSampler");
                string reason = state.StartsWith("img") || state.StartsWith("color: img") ? "textured" : state.Split(' ')[0].TrimEnd(':');
                if (reason == "none") reason = data != null && data.Constants.ContainsKey("diffuseColor0") ? "none, diffuseColor0" : "none, no colour";
                why[reason] = why.GetValueOrDefault(reason) + g.Count();
                total[reason] = total.GetValueOrDefault(reason) + g.Count();
                w.WriteLine($"  m{g.Key} '{mat.Name}' [{data?.Name}] {g.Count()} shapes, diffuse: {state}");
                w.WriteLine($"      render state {mat.StateIndex}: {(mat.StateIndex >= 0 && mat.StateIndex < m.RenderStates.Count ? m.RenderStates[mat.StateIndex].ToString() : "-")}");
                foreach (var k in mat.Samplers.Where(k => k.Sampler != "diffuseMapSampler"))
                    w.WriteLine($"      {k.Sampler}: {Status(m, set, mat, k.Sampler)}");
                if (data != null)
                {
                    foreach (var c in data.Constants.Keys) constantKeys[c] = constantKeys.GetValueOrDefault(c) + 1;
                    var colours = data.Constants.Where(c => c.Key.Contains("olor", StringComparison.OrdinalIgnoreCase)).Select(c => $"{c.Key}=({string.Join(",", c.Value.Select(v => v.ToString("0.##", ci)))})");
                    w.WriteLine($"      colours: {string.Join("; ", colours)}");
                }
                if (state == "none")
                {
                    var cands = Mdl3ShaderColor.Candidates(m, g.Key);
                    w.WriteLine($"      shader colour: {(Mdl3ShaderColor.Of(m, g.Key) is { } sc ? string.Create(ci, $"{sc.X:0.###} {sc.Y:0.###} {sc.Z:0.###}") : "-")}   candidates: {string.Join("; ", cands.Select(c => string.Create(ci, $"{c.Pc}:{c.Value.X:0.###},{c.Value.Y:0.###},{c.Value.Z:0.###}")))}");
                }
                Mdl3Mesh? mesh = null;
                try { mesh = Mdl3Mesh.Decode(m, g.First(), 0, out _); } catch { }
                if (mesh?.Colors is { Length: > 0 } vc)
                {
                    var mean = vc.Aggregate(System.Numerics.Vector4.Zero, (a, b) => a + b) / vc.Length;
                    var lo = vc.Aggregate(System.Numerics.Vector4.Min); var hi = vc.Aggregate(System.Numerics.Vector4.Max);
                    string V(System.Numerics.Vector4 x) => string.Create(ci, $"{x.X * 255:0} {x.Y * 255:0} {x.Z * 255:0} {x.W * 255:0}");
                    w.WriteLine($"      vertex colour s{g.First()}: mean {V(mean)}, min {V(lo)}, max {V(hi)}");
                }
                foreach (var k in mat.Samplers.Where(k => k.Sampler.Contains("ColorShift") && !k.External && k.Index < m.TextureInfos.Count))
                {
                    int img = m.TextureInfos[k.Index].ImageIndex;
                    if (img < set.Entries.Count && set.Decode(img) is { } px)
                        w.WriteLine($"      {k.Sampler} {set.Entries[img].Record.Width}x{set.Entries[img].Record.Height} first pixels: {string.Join(" | ", px.Chunk(4).Take(4).Select(p => string.Join(" ", p)))}");
                }
                w.WriteLine($"      objects:{string.Join(", ", g.Take(6).Select(s => ShapeNaming.Short(m, s)))}{(g.Count() > 6 ? ", ..." : "")}");
            }
            w.WriteLine($"  -> {string.Join(", ", why.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value}"))}");
            w.WriteLine();
        }
        w.WriteLine($"ALL: {string.Join(", ", total.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value}"))}");
        w.WriteLine($"constant names (materials): {string.Join(", ", constantKeys.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value}"))}");
        Console.WriteLine($"{args.Length - 1} files -> {args[0]}");
        return 0;
    }

    /// <summary>--vari: every _CLR_VARI_k material (used by a shape or not), its colour-shift images and their first pixel.</summary>
    static int Variations(string path)
    {
        var m = Mdl3File.Load(path);
        var set = new Mdl3TextureSet(m);
        var used = Enumerable.Range(0, m.Shapes.Count).Select(s => (int)m.Shapes[s].MaterialIndex).ToHashSet();
        for (int i = 0; i < m.Materials.Count; i++)
        {
            var mat = m.Materials[i];
            if (!mat.Name.Contains("_CLR_VARI_")) continue;
            var imgs = mat.Samplers.Where(k => !k.External && k.Index < m.TextureInfos.Count).Select(k =>
            {
                int img = m.TextureInfos[k.Index].ImageIndex;
                var px = img < set.Entries.Count ? set.Decode(img) : null;
                return $"{k.Sampler.Replace("MapSampler", "")}=img{img}{(px != null ? $"({px[0]},{px[1]},{px[2]})" : "")}";
            });
            Console.WriteLine($"m{i,-4} {(used.Contains(i) ? "used" : "    ")} {mat.Name,-50} {string.Join(" ", imgs)}");
        }
        return 0;
    }

    static string Status(Mdl3File m, Mdl3TextureSet set, Mdl3Material mat, string sampler)
    {
        var k = mat[sampler];
        if (k == null) return "none";
        if (k.External) return $"external {k.Index & 0x7FFF}";
        if (k.Index >= m.TextureInfos.Count) return $"bad info {k.Index}";
        var info = m.TextureInfos[k.Index];
        int img = info.ImageIndex;
        if (img >= set.Entries.Count) return $"bad image {img}";
        var e = set.Entries[img];
        string where = set.Decode(img) != null ? (e.Record.Offset != 0 ? "file" : "body_s") : "no pixels";
        return $"img{img} {where} {e.Record.Width}x{e.Record.Height} {Path.GetFileName(info.Name.Replace('\\', '/'))}";
    }
}
