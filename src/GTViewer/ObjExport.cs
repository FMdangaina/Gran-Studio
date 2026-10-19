using System.Drawing.Imaging;
using System.Globalization;
using System.Text;
using GTFormats.Mdl3;
using GTFormats.SpecDb;
using GTViewer.Rendering;

namespace GTViewer;

/// <summary>
/// Export of what the viewer shows (visible shapes, current switches and LOD level) as OBJ + MTL + PNG textures, for
/// ZModeler 2.2.6: one object per shape (`g` group path, `o` readable name) with its own material named like the
/// object, because ZModeler names the objects after the `usemtl` material (<see cref="ObjMaterials"/>); V flipped.
/// </summary>
static class ObjExport
{
    /// <summary>
    /// All LOD levels, one folder each (ROADMAP 12b): &lt;dir&gt;/&lt;name&gt;/lod0/&lt;name&gt;.obj + .mtl + the textures its
    /// materials use, lod1/..., returns the shapes per level.
    /// </summary>
    public static List<int> WriteAllLods(ViewerDocument doc, string dir, string name) => WriteLods(doc, Path.Combine(dir, name), name, true, new ObjTextureFolder(Path.Combine(dir, name)));

    /// <summary>Every LOD level of a model into folder\lodN\stem.obj (one level and not <paramref name="lodFolders"/>: folder\stem.obj).</summary>
    static List<int> WriteLods(ViewerDocument doc, string folder, string stem, bool lodFolders, ObjTextureFolder textures)
    {
        int keep = doc.Lod;
        var counts = new List<int>();
        try
        {
            for (int k = 0; k < doc.LodCount; k++)
            {
                doc.Lod = k;
                string lodDir = doc.LodCount == 1 && !lodFolders ? folder : Path.Combine(folder, $"lod{k}");
                Directory.CreateDirectory(lodDir);
                counts.Add(Write(doc, Path.Combine(lodDir, stem + ".obj"), textures));
            }
        }
        finally { doc.Lod = keep; }
        return counts;
    }

    /// <summary>
    /// The whole car by its code (ROADMAP 12b, user 2026-10-19: "export completo per id macchina da hq race interior
    /// wheels"), from any of its files: &lt;dir&gt;\&lt;name&gt;\hq\lod0..N and \race\lod0..N (bodies), \cockpit (interior;
    /// cockpit\lodN when it has levels), \wheel\hq|race\lod0..N (GT6) or \wheel\CODE\lod0..N (GT5; the CODE.NN wheels are
    /// aftermarket rims, not exported), each folder with its .obj and .mtl; the textures all in &lt;name&gt;\ (each once). The open file is exported as shown (switches,
    /// hidden parts), the others with their default visibility. A model that is not a car file: its levels only.
    /// </summary>
    public static List<string> WriteCar(ViewerDocument doc, Renderer renderer, string dir, string name)
    {
        string root = Path.Combine(dir, name);
        var textures = new ObjTextureFolder(root);   // every texture of the car once, next to the mesh folders (user 2026-10-19)
        var car = CarCatalog.FromModelPath(doc.Path);
        if (car == null) return [$"levels: {Levels(WriteLods(doc, root, name, true, textures))}"];
        var report = new List<string>();
        void Add(string what, string? file, string folder, string stem, bool lodFolders)
        {
            if (file != null) report.Add($"{what}: {Part(file, Path.Combine(root, folder), stem, lodFolders)}");
        }
        Add("hq body", car.HqBody, "hq", name + "_hq", true);
        Add("race body", car.RaceBody, "race", name + "_race", true);
        Add("cockpit", car.Interior, "cockpit", name + "_cockpit", false);
        // GT5 CODE.NN wheels are aftermarket rims (user 2026-10-19): only the car's own CODE wheel
        foreach (var w in car.Wheels.Where(w => car.Game == "GT6" || !Path.GetFileName(w).Contains('.')))
        {
            // GT6 car\...\hq|race\wheel -> wheel\hq, wheel\race; GT5 wheel\hq\CODE[.NN] -> wheel\CODE[_NN]
            string sub = car.Game == "GT6" ? Path.GetFileName(Path.GetDirectoryName(w))! : Path.GetFileName(w).Replace('.', '_');
            Add($"wheel {sub}", w, Path.Combine("wheel", sub), $"{name}_wheel_{sub}", true);
        }
        return report;

        string Part(string file, string folder, string stem, bool lodFolders)
        {
            try
            {
                bool open = Path.GetFullPath(file).Equals(Path.GetFullPath(doc.Path), StringComparison.OrdinalIgnoreCase);
                using var part = open ? null : new ViewerDocument(file, renderer);
                var d = part ?? doc;
                if (part != null && doc.Paint >= 0 && part.Paints.Count == doc.Paints.Count) part.SetPaint(doc.Paint);   // the same paint on every part
                if (!d.VisibleShapes().Any()) return $"no shapes in {Path.GetFileName(Path.GetDirectoryName(file))}\\{Path.GetFileName(file)}, not exported (GT5: the cockpit is in the hq body)";
                return Levels(WriteLods(d, folder, stem, lodFolders, textures));
            }
            catch (Exception e) { return $"not exported ({e.Message})"; }
        }
        static string Levels(List<int> counts) => string.Join(", ", counts.Select((c, k) => $"LOD{k} {c}"));
    }

    public static int Write(ViewerDocument doc, string path, ObjTextureFolder? textures = null)
    {
        var ci = CultureInfo.InvariantCulture;
        string dir = Path.GetDirectoryName(Path.GetFullPath(path))!, stem = Path.GetFileNameWithoutExtension(path);
        var obj = new StringBuilder();
        obj.AppendLine($"# GTViewer export of {doc.Path}, LOD {doc.Lod}");
        obj.AppendLine($"mtllib {stem}.mtl");
        var materials = new ObjMaterials(doc.File, doc.TextureSet, textures);
        int vBase = 1, exported = 0;
        var done = new HashSet<int>();   // a shape drawn by two models is written once
        foreach (var n in doc.VisibleShapes())
        {
            if (!done.Add(n.Shape)) continue;
            var mesh = doc.DecodeForExport(n.Shape);
            if (mesh == null || mesh.Indices.Length == 0) continue;
            var sh = doc.File.Shapes[n.Shape];
            var groups = new List<string>();
            for (var p = n.Parent; p != null && p.Kind != "file"; p = p.Parent) groups.Insert(0, p.Name);
            obj.AppendLine($"g {string.Join('/', groups)}");
            obj.AppendLine($"o {n.Name}");
            obj.AppendLine($"usemtl {materials.Add(n.Name, doc.MaterialOf(n.Shape))}");
            var nrm = mesh.Normals ?? Mdl3Mesh.FaceNormals(mesh.Positions, mesh.Indices);
            foreach (var v in mesh.Positions) obj.AppendLine(string.Create(ci, $"v {v.X} {v.Y} {v.Z}"));
            foreach (var t in mesh.Uvs ?? new System.Numerics.Vector2[mesh.Positions.Length]) obj.AppendLine(string.Create(ci, $"vt {t.X} {1 - t.Y}"));
            foreach (var q in nrm) obj.AppendLine(string.Create(ci, $"vn {q.X} {q.Y} {q.Z}"));
            for (int i = 0; i + 2 < mesh.Indices.Length; i += 3)
            {
                int a = mesh.Indices[i], b = mesh.Indices[i + 1], c = mesh.Indices[i + 2];
                if (a < 0 || b < 0 || c < 0 || a >= mesh.Positions.Length || b >= mesh.Positions.Length || c >= mesh.Positions.Length) continue;
                obj.AppendLine($"f {a + vBase}/{a + vBase}/{a + vBase} {b + vBase}/{b + vBase}/{b + vBase} {c + vBase}/{c + vBase}/{c + vBase}");
            }
            vBase += mesh.Positions.Length;
            exported++;
        }
        File.WriteAllText(path, obj.ToString());

        materials.Write(Path.Combine(dir, stem + ".mtl"), SavePng);
        return exported;
    }

    static void SavePng(string path, byte[] rgba, int w, int h)
    {
        var bgra = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++) { bgra[i * 4] = rgba[i * 4 + 2]; bgra[i * 4 + 1] = rgba[i * 4 + 1]; bgra[i * 4 + 2] = rgba[i * 4]; bgra[i * 4 + 3] = rgba[i * 4 + 3]; }
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < h; y++) System.Runtime.InteropServices.Marshal.Copy(bgra, y * w * 4, data.Scan0 + y * data.Stride, w * 4);
        bmp.UnlockBits(data);
        bmp.Save(path, ImageFormat.Png);
    }
}
