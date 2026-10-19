using System.Globalization;
using System.Text;
using GTFormats;
using GTFormats.Mdl3;
using GTFormats.Txs3;

/// <summary>
/// mdl3-obj &lt;file&gt; &lt;out.obj&gt; [--all-models]: export the plain and GT5 packed shapes as OBJ, one object per shape
/// with a readable name (ShapeNaming), one file per LOD level (out_lodN.obj), with an .mtl
/// and the diffuse textures as PNG. Research export to look at the geometry. Also writes &lt;out&gt;.txt with per-shape stats.
/// </summary>
static class Mdl3Obj
{
    static string Box(System.Numerics.Vector3[] p)
    {
        if (p.Length == 0) return "-";
        var lo = p.Aggregate(System.Numerics.Vector3.Min); var hi = p.Aggregate(System.Numerics.Vector3.Max);
        return string.Create(CultureInfo.InvariantCulture, $"{lo.X:0.000},{lo.Y:0.000},{lo.Z:0.000} .. {hi.X:0.000},{hi.Y:0.000},{hi.Z:0.000}");
    }

    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("mdl3-obj <file> <out.obj> [--all-models]  |  mdl3-obj <files.tsv> <groupRegex> (rank files by plain vertices)");
            return 1;
        }
        if (args[0].EndsWith(".tsv", StringComparison.OrdinalIgnoreCase)) return Rank(args[0], args[1]);
        bool allModels = args.Contains("--all-models");
        var m = Mdl3File.Load(args[0]);
        var all = SelectShapes(m, allModels);
        var scene = new Mdl3Scene(m, "");
        int lods = Math.Max(LodCount(m, all), scene.Levels);

        string stem = Path.GetFileNameWithoutExtension(args[1]), outDir = Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
        var textures = new Mdl3TextureSet(m);
        var log = new StringBuilder();
        log.AppendLine($"{args[0]}: {m.Shapes.Count} shapes, {all.Count} exported models' shapes, {lods} LOD level(s); LOD0 shapes:");
        var textureFolder = new ObjTextureFolder(lods == 1 ? outDir : Path.Combine(outDir, stem));   // the textures once, next to the lodN folders
        for (int lod = 0; lod < lods; lod++)
        {
            // GT6: the op8 LOD selects pick one shape per level; GT5 packed shapes hold their levels inside
            var shapes = all.Where(s => scene.AtLevel(s, lod)).ToList();
            // several levels: one folder each (out/stem/lodN/stem.obj + .mtl + its textures), as the viewer
            string dir = lods == 1 ? outDir : Path.Combine(outDir, stem, $"lod{lod}");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, stem + ".obj");
            var materials = new ObjMaterials(m, textures, textureFolder);
            int exported = Export(m, shapes, lod, path, stem + ".mtl", materials, lod == 0 ? log : null, out int skipped);
            materials.Write(Path.Combine(dir, stem + ".mtl"), Png.Write);
            Console.WriteLine($"lod{lod}: {exported} shapes exported, {skipped} skipped -> {path}");
        }
        File.WriteAllText(Path.Combine(outDir, stem + ".txt"), log.ToString());
        return 0;
    }

    /// <summary>Shapes drawn by a visible model (command streams); with allModels every shape.</summary>
    public static List<int> SelectShapes(Mdl3File m, bool allModels)
    {
        var drawnBy = new Dictionary<int, List<int>>();
        for (int mi = 0; mi < m.Models.Count; mi++)
            foreach (var c in m.Models[mi].Commands)
                foreach (int s in c.Shapes)
                {
                    if (!drawnBy.TryGetValue(s, out var l)) drawnBy[s] = l = [];
                    if (!l.Contains(mi)) l.Add(mi);
                }
        // helper = drawn only by helper models, or under a helper root node (instance models are named FIX4, FIX5...)
        return [.. Enumerable.Range(0, m.Shapes.Count)
            .Where(s => allModels ||
                (drawnBy.GetValueOrDefault(s, []).Any(mi => !IsHelperModel(m.ModelNames.GetValueOrDefault(mi, ""))) &&
                 !IsHelperModel(m.ShapeNames.TryGetValue(s, out var n) ? n[0].Split('|', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "" : "")))];
    }

    /// <summary>Most LOD levels of the given shapes (1 when none has packed LODs).</summary>
    public static int LodCount(Mdl3File m, List<int> shapes)
    {
        int lods = 1;
        foreach (int s in shapes)
            try { lods = Math.Max(lods, Mdl3Mesh.LodCount(m, s)); } catch { }
        return lods;
    }

    /// <summary>Models that are not the visible car: shadow volumes/planes, self-shadow copies, light masks, projections.</summary>
    public static bool IsHelperModel(string name) => Mdl3Scene.IsHelperModel(name);

    static int Export(Mdl3File m, List<int> shapes, int lod, string path, string mtlName, ObjMaterials materials, StringBuilder? log, out int skipped)
    {
        var ci = CultureInfo.InvariantCulture;
        var obj = new StringBuilder();
        obj.AppendLine($"mtllib {mtlName}");
        int vBase = 1, vtBase = 1, vnBase = 1, exported = 0;
        skipped = 0;
        foreach (int s in shapes)
        {
            var sh = m.Shapes[s];
            string full = m.ShapeNames.TryGetValue(s, out var n) ? n[0] : $"shape{s}";
            Mdl3Mesh? mesh;
            string reason;
            try { mesh = Mdl3Mesh.Decode(m, s, lod, out reason); }
            catch (Exception e) { mesh = null; reason = "FAILED " + e.Message; }
            if (mesh == null)
            {
                log?.AppendLine($"{s,4} SKIPPED {reason}  {full}");
                skipped++;
                continue;
            }
            log?.AppendLine($"{s,4} {(mesh.Packed ? "packed" : "plain ")} v={mesh.Positions.Length,6} tris={mesh.Indices.Length / 3,6} (+26 {sh.TriCount,6}) " +
                            $"n={(mesh.Normals != null ? "y" : "-")} uv={(mesh.Uvs != null ? "y" : "-")} box {Box(mesh.Positions)}  {ShapeNaming.Short(m, s)}  {full}");
            // OBJ has no parent links: the group path (meaningful Maya groups above the shape) goes in a g line
            var raw = full.Split('|', StringSplitOptions.RemoveEmptyEntries);
            var groupPath = ShapeNaming.Nodes(string.Join('|', raw.Take(Math.Max(1, raw.Length - 2))));
            obj.AppendLine($"g {(groupPath.Count > 0 ? string.Join('/', groupPath) : "shape")}");
            obj.AppendLine($"o {ShapeNaming.Short(m, s)}");
            obj.AppendLine($"usemtl {materials.Add(ShapeNaming.Short(m, s), sh.MaterialIndex)}");
            foreach (var p in mesh.Positions) obj.AppendLine(string.Create(ci, $"v {p.X} {p.Y} {p.Z}"));
            if (mesh.Uvs != null) foreach (var t in mesh.Uvs) obj.AppendLine(string.Create(ci, $"vt {t.X} {1 - t.Y}"));   // V flipped for OBJ
            if (mesh.Normals != null) foreach (var q in mesh.Normals) obj.AppendLine(string.Create(ci, $"vn {q.X} {q.Y} {q.Z}"));
            bool hasUv = mesh.Uvs != null, hasN = mesh.Normals != null;
            string Corner(int i) => (hasUv, hasN) switch
            {
                (true, true) => $"{i + vBase}/{i + vtBase}/{i + vnBase}",
                (true, false) => $"{i + vBase}/{i + vtBase}",
                (false, true) => $"{i + vBase}//{i + vnBase}",
                _ => $"{i + vBase}",
            };
            for (int i = 0; i + 2 < mesh.Indices.Length; i += 3)
                obj.AppendLine($"f {Corner(mesh.Indices[i])} {Corner(mesh.Indices[i + 1])} {Corner(mesh.Indices[i + 2])}");
            vBase += mesh.Positions.Length;
            if (hasUv) vtBase += mesh.Positions.Length;
            if (hasN) vnBase += mesh.Positions.Length;
            exported++;
        }
        File.WriteAllText(path, obj.ToString());
        return exported;
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
            Mdl3File m;
            try { m = Mdl3File.Load(c[0]); } catch { continue; }   // old v1/v2 files
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

    /// <summary>Diffuse and alpha map of each material, decoded from the file's TXS3 (level 0) to &lt;stem&gt;_texNNN.png in dir.</summary>
    public static Dictionary<int, (string? Diffuse, string? Alpha)> MaterialTextures(Mdl3File m, IEnumerable<int> used, string dir, string stem)
    {
        var images = Txs3Image.ReadAll(m.Data, (int)m.Data.U32(0x48));
        var written = new Dictionary<int, string?>();
        string? Texture(Mdl3SamplerKey? key)
        {
            if (key == null || key.External || key.Index >= m.TextureInfos.Count) return null;
            var t = m.TextureInfos[key.Index];
            if (written.TryGetValue(t.ImageIndex, out var f)) return f;
            var img = images[t.ImageIndex];
            byte[]? rgba = img.Offset == 0 ? null : img.DecodeLevel0(m.Data.Data.AsSpan((int)img.Offset, (int)img.Size), t.Pitch);
            f = null;
            if (rgba != null)
            {
                f = $"{stem}_tex{t.ImageIndex:D3}.png";
                Png.Write(Path.Combine(dir, f), rgba, img.Width, img.Height);   // level 0, first face/slice
            }
            return written[t.ImageIndex] = f;
        }
        return used.ToDictionary(i => i, i => (Texture(m.Materials[i]["diffuseMapSampler"]), Texture(m.Materials[i]["alphaMapSampler"])));
    }
}
