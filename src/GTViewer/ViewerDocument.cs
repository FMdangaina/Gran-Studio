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
    List<Txs3Image>? images;
    Dictionary<int, List<(string where, byte[] bundle, int offset)>>? bodySImages;

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
    }

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
                    v[i] = new GpuVertex(p[i], i < nrm.Length ? nrm[i] : Vector3.UnitY, mesh.Uvs != null && i < mesh.Uvs.Length ? mesh.Uvs[i] : Vector2.Zero);
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

    readonly Dictionary<int, byte[]?> decoded = [];

    /// <summary>The diffuse map of a material: TXS3 image index and its level 0 as RGBA (null pixels when not decodable).</summary>
    public (int Image, byte[]? Rgba, int Width, int Height)? DiffuseImage(int material)
    {
        if (material < 0 || material >= File.Materials.Count) return null;
        var key = File.Materials[material]["diffuseMapSampler"];
        if (key == null || key.External || key.Index >= File.TextureInfos.Count) return null;
        var info = File.TextureInfos[key.Index];
        images ??= Txs3Image.ReadAll(File.Data, (int)File.Data.U32(0x48));
        var img = images[info.ImageIndex];
        if (!decoded.TryGetValue(info.ImageIndex, out var rgba))
        {
            try
            {
                if (img.Offset != 0) rgba = img.DecodeLevel0(File.Data.Data.AsSpan((int)img.Offset, (int)img.Size), info.Pitch);
                else if (System.IO.File.Exists(Path + "_s") && Mdl3StreamMap.Read(File.Data) is { } map)
                {
                    bodySImages ??= map.FindImages(System.IO.File.ReadAllBytes(Path + "_s"));
                    if (bodySImages.TryGetValue(info.ImageIndex, out var where) && where.Count > 0)
                        rgba = img.DecodeLevel0(where[0].bundle.AsSpan(where[0].offset, (int)img.Size), info.Pitch);
                }
            }
            catch { rgba = null; }
            decoded[info.ImageIndex] = rgba;
        }
        return (info.ImageIndex, rgba, img.Width, img.Height);
    }

    /// <summary>Triangles drawn with the current visibility, switches and LOD level (a shape drawn by two models once).</summary>
    public int VisibleTriangles() => VisibleShapes().Select(n => n.Shape).Distinct().Sum(s => (Mesh(s, out _)?.IndexCount ?? 0) / 3);

    /// <summary>The decoded triangles of a shape at the current LOD level (for export).</summary>
    public Mdl3Mesh? DecodeForExport(int shape)
    {
        int lod = Scene.ShapeLevel.ContainsKey(shape) ? 0 : Lod;
        try { return Mdl3Mesh.Decode(File, shape, lod, out _); } catch { return null; }
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
            int mat = File.Shapes[n.Shape].MaterialIndex;
            var tex = Textures ? DiffuseTexture(mat) : null;
            // textured: the colour under the texture is a neutral paint (the real paint colour is in the game data, item 7)
            items.Add(new DrawItem(mesh, tex != null ? new Vector4(0.86f, 0.86f, 0.86f, 1) : MaterialColor(mat), tex, Selected != null && Selected.Shape == n.Shape && (Selected.Model == n.Model)));
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
