using System.Numerics;
using GTFormats.Fbx;
using GTFormats.Mdl3;
using GTFormats.SpecDb;
using GTViewer.Rendering;

namespace GTViewer;

/// <summary>
/// Export of what the viewer shows as FBX with the model hierarchy kept (user 2026-10-25: "esportazione con mantenimento
/// della gerarchia"), textures as TGA. Scene: <c>name &gt; LOD0..N &gt; (the model's own node tree: model, Maya groups) &gt; shape
/// (mesh)</c>; every mesh has the GT material of its shape (colour, diffuse texture). Hidden parts and the switch state are
/// exported as shown, like the OBJ export. Textures go to <c>textures\*.tga</c> next to the FBX file.
/// </summary>
static class FbxExport
{
    const string TextureFolderName = "textures";

    /// <summary>One FBX file with every LOD level of the open model: dir\stem.fbx and dir\textures\*.tga. Returns the shapes per level.</summary>
    public static List<int> Write(ViewerDocument doc, string fbxPath, ObjTextureFolder? textures = null)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(fbxPath))!, stem = Path.GetFileNameWithoutExtension(fbxPath);
        textures ??= Folder(dir);
        var builder = new FbxBuilder("GTViewer: " + Path.GetFileName(doc.Path));
        var counts = Fill(doc, builder, 0, stem, textures, dir);
        File.WriteAllBytes(fbxPath, builder.ToBytes());
        return counts;
    }

    /// <summary>The texture folder of an FBX export: <c>dir\textures</c>, TGA.</summary>
    public static ObjTextureFolder Folder(string dir) =>
        new(Path.Combine(dir, TextureFolderName)) { Extension = ".tga", Save = (path, rgba, w, h) => Tga.Write(path, rgba, w, h) };

    /// <summary>Adds the model under a root Null named <paramref name="name"/> (0 = scene root): one LOD node per level.</summary>
    static List<int> Fill(ViewerDocument doc, FbxBuilder builder, long parent, string name, ObjTextureFolder textures, string fbxDir)
    {
        var materials = new ObjMaterials(doc.File, doc.TextureSet, textures);
        var fbxMaterials = new Dictionary<int, FbxMaterial>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        FbxMaterial Material(int mi)
        {
            if (fbxMaterials.TryGetValue(mi, out var m)) return m;
            string baseName = mi >= 0 && mi < doc.File.Materials.Count ? ObjMaterials.MaterialName(doc.File.Materials[mi].Name) : "";
            if (baseName.Length == 0) baseName = "material";
            string unique = baseName;
            for (int k = 2; !usedNames.Add(unique); k++) unique = $"{baseName}_{k}";
            var look = doc.TextureSet.Look(mi);
            m = new FbxMaterial { Name = unique, Color = look.Color };
            if (look.Image >= 0 && materials.TextureFile(look.Image, textures) is { } file)
            {
                m.TextureFile = Path.GetFullPath(Path.Combine(textures.Directory, file));
                m.TextureRelative = Path.GetRelativePath(fbxDir, m.TextureFile);
            }
            return fbxMaterials[mi] = m;
        }

        long root = builder.AddNull(name, parent);
        int keep = doc.Lod;
        var counts = new List<int>();
        try
        {
            for (int lod = 0; lod < doc.LodCount; lod++)
            {
                doc.Lod = lod;
                long lodNode = builder.AddNull("LOD" + lod, root);
                var visible = doc.VisibleShapes().ToHashSet();
                var done = new HashSet<int>();   // a shape drawn by two models is written once
                int shapes = 0;
                foreach (var child in doc.Scene.Root.Children) shapes += AddNode(child, lodNode);
                counts.Add(shapes);

                // a node (and its Null) only when something visible is under it
                int AddNode(Mdl3SceneNode n, long under)
                {
                    if (n.Kind == "shape")
                    {
                        if (!visible.Contains(n) || !done.Add(n.Shape)) return 0;
                        var mesh = doc.DecodeForExport(n.Shape);
                        if (mesh == null || mesh.Indices.Length == 0) return 0;
                        var idx = mesh.Indices.Where(i => i >= 0 && i < mesh.Positions.Length).ToArray();
                        idx = idx[..(idx.Length / 3 * 3)];
                        if (idx.Length == 0) return 0;
                        var nrm = mesh.Normals ?? Mdl3Mesh.FaceNormals(mesh.Positions, mesh.Indices);
                        if (nrm.Length != mesh.Positions.Length) nrm = Mdl3Mesh.FaceNormals(mesh.Positions, mesh.Indices);
                        Vector2[]? uv = mesh.Uvs is { } u && u.Length == mesh.Positions.Length ? u.Select(t => new Vector2(t.X, 1 - t.Y)).ToArray() : null;
                        builder.AddMesh(n.Name, under, Material(doc.MaterialOf(n.Shape)), mesh.Positions, nrm, uv, idx);
                        return 1;
                    }
                    if (!n.All().Any(x => x.Kind == "shape" && visible.Contains(x))) return 0;
                    long node = builder.AddNull(n.Name.Length > 0 ? n.Name : n.Kind, under);
                    int count = 0;
                    foreach (var c in n.Children) count += AddNode(c, node);
                    return count;
                }
            }
        }
        finally { doc.Lod = keep; }
        return counts;
    }

    /// <summary>
    /// The whole car by its code, from any of its files (the parts of <see cref="ObjExport.WriteCar"/>): one FBX per part in
    /// &lt;dir&gt;\&lt;name&gt;\ (name_hq.fbx, name_race.fbx, name_cockpit.fbx, name_wheel_hq.fbx ...), each with all its LOD
    /// levels as nodes, and the textures of all of them once in &lt;dir&gt;\&lt;name&gt;\textures (TGA).
    /// </summary>
    public static List<string> WriteCar(ViewerDocument doc, Renderer renderer, string dir, string name)
    {
        string root = Path.Combine(dir, name);
        Directory.CreateDirectory(root);
        var textures = Folder(root);
        var car = CarCatalog.FromModelPath(doc.Path);
        var report = new List<string>();
        if (car == null) { report.Add($"{name}.fbx: levels {Levels(Write(doc, Path.Combine(root, name + ".fbx"), textures))}"); return report; }
        void Add(string what, string? file, string stem)
        {
            if (file != null) report.Add($"{what}: {Part(file, stem)}");
        }
        Add("hq body", car.HqBody, name + "_hq");
        Add("race body", car.RaceBody, name + "_race");
        Add("cockpit", car.Interior, name + "_cockpit");
        foreach (var w in car.Wheels.Where(w => car.Game == "GT6" || !Path.GetFileName(w).Contains('.')))
        {
            string sub = car.Game == "GT6" ? Path.GetFileName(Path.GetDirectoryName(w))! : Path.GetFileName(w).Replace('.', '_');
            Add($"wheel {sub}", w, $"{name}_wheel_{sub}");
        }
        return report;

        string Part(string file, string stem)
        {
            try
            {
                bool open = Path.GetFullPath(file).Equals(Path.GetFullPath(doc.Path), StringComparison.OrdinalIgnoreCase);
                using var part = open ? null : new ViewerDocument(file, renderer);
                var d = part ?? doc;
                if (part != null && doc.Paint >= 0 && part.Paints.Count == doc.Paints.Count) part.SetPaint(doc.Paint);
                if (!d.VisibleShapes().Any()) return $"no shapes in {Path.GetFileName(Path.GetDirectoryName(file))}\\{Path.GetFileName(file)}, not exported";
                return stem + ".fbx " + Levels(Write(d, Path.Combine(root, stem + ".fbx"), textures));
            }
            catch (Exception e) { return $"not exported ({e.Message})"; }
        }
        static string Levels(List<int> counts) => string.Join(", ", counts.Select((c, k) => $"LOD{k} {c}"));
    }

    /// <summary>Every texture of the open model file, as TGA in <paramref name="dir"/> (not only the ones a visible part uses). Returns the count.</summary>
    public static int WriteAllTextures(ViewerDocument doc, string dir)
    {
        var folder = new ObjTextureFolder(dir) { Extension = ".tga", Save = (path, rgba, w, h) => Tga.Write(path, rgba, w, h) };
        int n = 0;
        foreach (var e in doc.TextureSet.Entries)
        {
            if (doc.TextureSet.Decode(e.Image) is not { } rgba) continue;
            string stem = e.SharedFrom != null ? ObjMaterials.Clean(e.ShortName.Replace(' ', '_')) : ObjMaterials.Clean(Path.GetFileNameWithoutExtension(e.ShortName));
            folder.Add(stem, rgba, e.Record.Width, rgba.Length / 4 / e.Record.Width);
            n++;
        }
        return n;
    }
}
