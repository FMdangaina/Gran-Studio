using System.Numerics;
using GTFormats.Mdl3;
using GTFormats.Txs3;
using GTViewer.Rendering;

namespace GTViewer;

/// <summary>
/// An opened MDL3 in the viewer: the scene (tree, switches, LOD levels), what is shown (hidden tree nodes, switch
/// branches, LOD level, textures on/off) and the GPU meshes and textures, decoded on first use.
/// </summary>
public sealed class ViewerDocument : IDisposable
{
    public string Path { get; }
    public Mdl3File File { get; }
    public Mdl3Scene Scene { get; }
    public HashSet<Mdl3SceneNode> Hidden { get; } = [];
    public Dictionary<int, int> State { get; } = [];          // selector id -> branch
    public int Lod { get; set; }
    public bool Textures { get; set; } = true;
    public Mdl3SceneNode? Selected { get; set; }
    public int LodCount { get; }

    readonly Renderer renderer;
    readonly Dictionary<(int shape, int lod), GpuMesh?> meshes = [];
    readonly Dictionary<int, GpuTexture?> textures = [];      // by TXS3 image index
    readonly Dictionary<(int shape, int lod), string> failures = [];

    public ViewerDocument(string path, Renderer renderer)
    {
        Path = path;
        this.renderer = renderer;
        File = Mdl3File.Load(path);
        Scene = new Mdl3Scene(File, System.IO.Path.GetFileName(path));
        foreach (var s in Scene.Selectors) State[s.Id] = s.Default;
        foreach (var n in Scene.Root.Children.Where(n => n.Kind == "model" && (n.Model < 0 || !Scene.DefaultOn(n.Model)))) Hidden.Add(n);
        // LOD levels: GT6 op8 LOD selects (one shape per level), GT5 packed levels inside the shapes
        int packed = 1;
        for (int s = 0; s < File.Shapes.Count; s++)
            try { packed = Math.Max(packed, Mdl3Mesh.LodCount(File, s)); } catch { }
        LodCount = Math.Max(Math.Max(1, Scene.Levels), packed);
        LoadPaints();
    }

    /// <summary>The car's paints from the GT6 database (name, colour), empty when unknown or the model has no colour variations.</summary>
    public List<(string Name, Vector3 Color)> Paints { get; } = [];
    public int Paint { get; private set; } = -1;

    /// <summary>
    /// GT6 paints (ROADMAP 13): the database colours of the car (model code from the path, VARIATION.ColorChip);
    /// the default one (MainColor) is applied. Only for bodies with `_CLR_VARI_` materials.
    /// </summary>
    public void LoadPaints()
    {
        try
        {
            if (TextureSet.VariationCount == 0 || GTFormats.SpecDb.CarCatalog.FromModelPath(Path) is not { } car) return;
            string? carDir = Mdl3TextureSet.CarFolder(System.IO.Path.GetFullPath(Path));
            if (carDir == null || GTFormats.SpecDb.CarCatalog.FindGt6SpecDb(System.IO.Path.GetDirectoryName(carDir)!) is not { } f) return;
            using var db = GTFormats.SpecDb.Gt6SpecDb.Open(f);
            var (list, main) = db.Paints(long.Parse(car.Code));
            foreach (var p in list) Paints.Add((p.Name.Length > 0 ? p.Name : $"colour {Paints.Count + 1}", new Vector3(p.R, p.G, p.B) / 255f));
            if (Paints.Count > 0) SetPaint(main);
        }
        catch { Paints.Clear(); }
    }

    /// <summary>Paint i: its colour on the paint materials, variation i when the body has one per colour (else 0).</summary>
    public void SetPaint(int i)
    {
        if (i < 0 || i >= Paints.Count) return;
        Paint = i;
        int v = TextureSet.VariationCount;
        TextureSet.SetPaint(v == Paints.Count || i < v ? i : 0, Paints[i].Color);
    }

    /// <summary>The material a shape is drawn with (the current colour variation).</summary>
    public int MaterialOf(int shape) => TextureSet.Material(File.Shapes[shape].MaterialIndex);

    /// <summary>A node is shown when neither it nor any parent is hidden.</summary>
    public bool IsShown(Mdl3SceneNode n)
    {
        for (var p = n; p != null; p = p.Parent) if (Hidden.Contains(p)) return false;
        return true;
    }

    /// <summary>Shape nodes drawn with the current visibility, switch state and LOD level.</summary>
    public IEnumerable<Mdl3SceneNode> VisibleShapes() =>
        Scene.Root.All().Where(n => n.Kind == "shape" && IsShown(n) && (n.Model < 0 || Scene.IsDrawn(n.Model, n.Shape, State, Lod)));

    public GpuMesh? Mesh(int shape, out string? failure)
    {
        int lod = Scene.ShapeLevel.ContainsKey(shape) ? 0 : Lod;   // shapes inside op8 are one level each
        failure = null;
        if (meshes.TryGetValue((shape, lod), out var m)) { failures.TryGetValue((shape, lod), out failure); return m; }
        try
        {
            var mesh = Mdl3Mesh.Decode(File, shape, lod, out string reason);
            if (mesh == null) failure = reason;
            else
            {
                var p = mesh.Positions;
                var nrm = mesh.Normals ?? Mdl3Mesh.FaceNormals(p, mesh.Indices);
                var v = new GpuVertex[p.Length];
                for (int i = 0; i < p.Length; i++)
                    v[i] = new GpuVertex(p[i], i < nrm.Length ? nrm[i] : Vector3.UnitY, mesh.Uvs != null && i < mesh.Uvs.Length ? mesh.Uvs[i] : Vector2.Zero,
                        mesh.Colors != null && i < mesh.Colors.Length ? mesh.Colors[i] : Vector4.One);
                var idx = mesh.Indices.Where(i => i >= 0 && i < p.Length).ToArray();
                if (idx.Length % 3 != 0) idx = idx[..(idx.Length / 3 * 3)];
                m = p.Length == 0 || idx.Length == 0 ? null : renderer.CreateMesh(v, idx);
                if (m == null) failure = "no triangles";
            }
        }
        catch (Exception e) { failure = "decode failed: " + e.Message; m = null; }
        meshes[(shape, lod)] = m;
        if (failure != null) failures[(shape, lod)] = failure;
        return m;
    }

    /// <summary>The diffuse texture of a material (in the file, or for GT6 bodies in body_s), null when none.</summary>
    public GpuTexture? DiffuseTexture(int material)
    {
        var tex = DiffuseImage(material);
        if (tex == null) return null;
        var (image, rgba, w, h) = tex.Value;
        if (textures.TryGetValue(image, out var t)) return t;
        try { t = rgba == null ? null : renderer.CreateTexture(rgba, w, h); } catch { t = null; }
        textures[image] = t;
        return t;
    }

    Mdl3TextureSet? textureSet;

    /// <summary>Every TXS3 image of the file (shared with the texture browser, decoded once).</summary>
    public Mdl3TextureSet TextureSet => textureSet ??= new Mdl3TextureSet(File);

    /// <summary>
    /// The texture of a material (diffuse map, Raytrace colour map, race number: <see cref="Mdl3TextureSet.Look"/>): TXS3
    /// image index and its level 0 as RGBA.
    /// </summary>
    public (int Image, byte[]? Rgba, int Width, int Height)? DiffuseImage(int material)
    {
        int image = TextureSet.Look(material).Image;
        if (image < 0) return null;
        var img = TextureSet.Entries[image].Record;
        return (image, TextureSet.Decode(image), img.Width, img.Height);
    }

    /// <summary>Triangles drawn with the current visibility, switches and LOD level (a shape drawn by two models once).</summary>
    public int VisibleTriangles() => VisibleShapes().Select(n => n.Shape).Distinct().Sum(s => (Mesh(s, out _)?.IndexCount ?? 0) / 3);

    /// <summary>The decoded triangles of a shape at the current LOD level (for export).</summary>
    public Mdl3Mesh? DecodeForExport(int shape)
    {
        int lod = Scene.ShapeLevel.ContainsKey(shape) ? 0 : Lod;
        try { return Mdl3Mesh.Decode(File, shape, lod, out _); } catch { return null; }
    }

    /// <summary>
    /// How the game blends a material (its RSX render state): SRC_ALPHA / ONE_MINUS_SRC_ALPHA = blend (GT5 decals),
    /// SRC_ALPHA / ONE = additive (GT5 CAdd), alpha test GREATER ref = cut out; else opaque. Most GT6 materials are opaque.
    /// </summary>
    (BlendMode, float) Mode(int material)
    {
        if (material < 0 || material >= File.Materials.Count) return (BlendMode.Opaque, 0);
        int s = File.Materials[material].StateIndex;
        if (s < 0 || s >= File.RenderStates.Count || !File.RenderStates[s].Valid) return (BlendMode.Opaque, 0);
        var st = File.RenderStates[s];
        if (st.Blend && st.DstFactor == 1) return (BlendMode.Additive, 0);
        if (st.AlphaBlend) return (BlendMode.Blend, 0);
        if (st.AlphaTest && st.AlphaFunc == 0x204 && st.AlphaRef > 0) return (BlendMode.AlphaTest, st.AlphaRef / 255f);   // GREATER
        return (BlendMode.Opaque, 0);
    }

    /// <summary>A soft colour per material, so the parts can be told apart without textures.</summary>
    public static Vector4 MaterialColor(int material)
    {
        if (material < 0) return new Vector4(0.7f, 0.7f, 0.7f, 1);
        uint h = (uint)material * 2654435761u;
        float r = 0.55f + (h & 0xFF) / 255f * 0.35f, g = 0.55f + ((h >> 8) & 0xFF) / 255f * 0.35f, b = 0.55f + ((h >> 16) & 0xFF) / 255f * 0.35f;
        return new Vector4(r, g, b, 1);
    }

    public List<DrawItem> DrawItems(out Vector3 min, out Vector3 max)
    {
        var items = new List<DrawItem>();
        min = new Vector3(float.MaxValue); max = new Vector3(float.MinValue);
        foreach (var n in VisibleShapes())
        {
            var mesh = Mesh(n.Shape, out _);
            if (mesh == null) continue;
            int mat = MaterialOf(n.Shape);
            // Textures on: the material's texture over its colour (paint, constant or shader colour: ROADMAP 12c);
            // off: a soft colour per material, to tell the parts apart
            var tex = Textures ? DiffuseTexture(mat) : null;
            var color = Textures ? new Vector4(TextureSet.Look(mat).Color, 1) : MaterialColor(mat);
            var (mode, alphaRef) = Mode(mat);
            items.Add(new DrawItem(mesh, color, tex, Selected != null && Selected.Shape == n.Shape && (Selected.Model == n.Model), tex != null ? mode : BlendMode.Opaque, alphaRef));
            min = Vector3.Min(min, mesh.Min); max = Vector3.Max(max, mesh.Max);
        }
        if (items.Count == 0) { min = -Vector3.One; max = Vector3.One; }
        return items;
    }

    public void Dispose()
    {
        foreach (var m in meshes.Values) m?.Dispose();
        foreach (var t in textures.Values) t?.Dispose();
    }
}
