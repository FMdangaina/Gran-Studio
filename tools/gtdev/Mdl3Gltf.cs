using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using GTFormats.Mdl3;

/// <summary>
/// mdl3-gltf &lt;file&gt; &lt;out.gltf&gt; [--all-models]: the same shapes as mdl3-obj, as glTF 2.0 (.gltf + .bin + PNG) with the
/// node hierarchy: file -> model -> meaningful Maya nodes (ShapeNaming.Nodes) -> one mesh node per shape. One file per
/// LOD level (out_lodN.gltf). Parts placed by the game at run time (GTM_SUSPENSION_*: brake discs, calipers) stay at
/// their local origin: their transform is not in the MDL3 (model command op6 = engine matrix slot).
/// </summary>
static class Mdl3Gltf
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("mdl3-gltf <file> <out.gltf> [--all-models]"); return 1; }
        var m = Mdl3File.Load(args[0]);
        var shapes = Mdl3Obj.SelectShapes(m, args.Contains("--all-models"));
        var scene = new Mdl3Scene(m, "");
        int lods = Math.Max(Mdl3Obj.LodCount(m, shapes), scene.Levels);
        string dir = Path.GetDirectoryName(Path.GetFullPath(args[1]))!, stem = Path.GetFileNameWithoutExtension(args[1]);
        var materials = shapes.Select(s => (int)m.Shapes[s].MaterialIndex).Where(i => i >= 0).Distinct().Order().ToList();
        var textures = Mdl3Obj.MaterialTextures(m, materials, dir, stem);
        for (int lod = 0; lod < lods; lod++)
        {
            string name = lod == 0 ? stem : $"{stem}_lod{lod}";
            int n = Write(m, shapes.Where(s => scene.AtLevel(s, lod)).ToList(), lod, materials, textures, Path.Combine(dir, name + ".gltf"), name + ".bin", Path.GetFileName(args[0]), out int skipped);
            Console.WriteLine($"lod{lod}: {n} shapes, {skipped} skipped -> {Path.Combine(dir, name + ".gltf")}");
        }
        return 0;
    }

    static int Write(Mdl3File m, List<int> shapes, int lod, List<int> materials, Dictionary<int, (string? Diffuse, string? Alpha)> tex,
                     string path, string binName, string rootName, out int skipped)
    {
        var bin = new MemoryStream();
        var views = new JsonArray();
        var accessors = new JsonArray();
        var meshes = new JsonArray();
        var nodes = new JsonArray();
        var children = new Dictionary<int, JsonArray>();
        var groups = new Dictionary<string, int>();
        skipped = 0;

        int AddNode(string name, int parent)
        {
            nodes.Add(new JsonObject { ["name"] = name });
            int id = nodes.Count - 1;
            if (parent >= 0)
            {
                if (!children.TryGetValue(parent, out var c)) children[parent] = c = [];
                c.Add(id);
            }
            return id;
        }
        int root = AddNode(rootName, -1);

        int Accessor(byte[] data, int count, string type, int componentType, int? target, JsonArray? min = null, JsonArray? max = null)
        {
            while (bin.Length % 4 != 0) bin.WriteByte(0);
            var view = new JsonObject { ["buffer"] = 0, ["byteOffset"] = bin.Length, ["byteLength"] = data.Length };
            if (target != null) view["target"] = target;
            bin.Write(data);
            views.Add(view);
            var a = new JsonObject { ["bufferView"] = views.Count - 1, ["componentType"] = componentType, ["count"] = count, ["type"] = type };
            if (min != null) { a["min"] = min; a["max"] = max; }
            accessors.Add(a);
            return accessors.Count - 1;
        }

        int exported = 0;
        foreach (int s in shapes)
        {
            Mdl3Mesh? mesh;
            try { mesh = Mdl3Mesh.Decode(m, s, lod, out _); } catch { mesh = null; }
            if (mesh == null || mesh.Indices.Length == 0 || mesh.Positions.Length == 0) { skipped++; continue; }

            var p = mesh.Positions;
            var mn = p.Aggregate(Vector3.Min); var mx = p.Aggregate(Vector3.Max);
            var attrs = new JsonObject
            {
                ["POSITION"] = Accessor(Floats(p.SelectMany(v => new[] { v.X, v.Y, v.Z })), p.Length, "VEC3", 5126, 34962,
                                        [mn.X, mn.Y, mn.Z], [mx.X, mx.Y, mx.Z]),
            };
            if (mesh.Normals != null)
                attrs["NORMAL"] = Accessor(Floats(mesh.Normals.SelectMany(v =>
                {
                    var u = v.LengthSquared() > 1e-8f ? Vector3.Normalize(v) : Vector3.UnitY;   // glTF wants unit normals
                    return new[] { u.X, u.Y, u.Z };
                })), p.Length, "VEC3", 5126, 34962);
            if (mesh.Uvs != null)
                attrs["TEXCOORD_0"] = Accessor(Floats(mesh.Uvs.SelectMany(v => new[] { v.X, v.Y })), p.Length, "VEC2", 5126, 34962);   // top-left origin as stored
            var idx = new byte[mesh.Indices.Length * 4];
            Buffer.BlockCopy(mesh.Indices, 0, idx, 0, idx.Length);
            var prim = new JsonObject { ["attributes"] = attrs, ["indices"] = Accessor(idx, mesh.Indices.Length, "SCALAR", 5125, 34963) };
            int mat = materials.IndexOf(m.Shapes[s].MaterialIndex);
            if (mat >= 0) prim["material"] = mat;
            string shortName = ShapeNaming.Short(m, s);
            meshes.Add(new JsonObject { ["name"] = shortName, ["primitives"] = new JsonArray(prim) });

            // group nodes: the meaningful nodes above the Maya transform + shape pair (the leaf is in the short name)
            var raw = (m.ShapeNames.TryGetValue(s, out var nm) ? nm[0] : $"shape{s}").Split('|', StringSplitOptions.RemoveEmptyEntries);
            var nodePath = ShapeNaming.Nodes(string.Join('|', raw.Take(Math.Max(1, raw.Length - 2))));
            if (nodePath.Count == 0) nodePath.Add("shape");
            int parent = root;
            string key = "";
            for (int i = 0; i < nodePath.Count; i++)
            {
                key += "|" + nodePath[i];
                if (!groups.TryGetValue(key, out int g)) groups[key] = g = AddNode(nodePath[i], parent);
                parent = g;
            }
            int leaf = AddNode(shortName, parent);
            nodes[leaf]!["mesh"] = meshes.Count - 1;
            exported++;
        }
        foreach (var (id, c) in children) nodes[id]!["children"] = c;

        var images = new JsonArray();
        var textures = new JsonArray();
        var imageIds = new Dictionary<string, int>();
        var mats = new JsonArray();
        foreach (int i in materials)
        {
            var pbr = new JsonObject { ["metallicFactor"] = 0.0, ["roughnessFactor"] = 0.6 };
            if (tex[i].Diffuse is { } file)
            {
                if (!imageIds.TryGetValue(file, out int t))
                {
                    images.Add(new JsonObject { ["uri"] = file });
                    textures.Add(new JsonObject { ["source"] = images.Count - 1, ["sampler"] = 0 });
                    imageIds[file] = t = textures.Count - 1;
                }
                pbr["baseColorTexture"] = new JsonObject { ["index"] = t };
            }
            else pbr["baseColorFactor"] = new JsonArray(0.8, 0.8, 0.8, 1.0);
            mats.Add(new JsonObject { ["name"] = string.IsNullOrEmpty(m.Materials[i].Name) ? $"m{i:D3}" : $"m{i:D3}_{m.Materials[i].Name}", ["pbrMetallicRoughness"] = pbr });
        }

        var doc = new JsonObject
        {
            ["asset"] = new JsonObject { ["version"] = "2.0", ["generator"] = "gt_dev gtdev mdl3-gltf" },
            ["scene"] = 0,
            ["scenes"] = new JsonArray(new JsonObject { ["nodes"] = new JsonArray(root) }),
            ["nodes"] = nodes,
            ["meshes"] = meshes,
            ["materials"] = mats,
            ["accessors"] = accessors,
            ["bufferViews"] = views,
            ["buffers"] = new JsonArray(new JsonObject { ["uri"] = binName, ["byteLength"] = bin.Length }),
        };
        if (images.Count > 0)
        {
            doc["images"] = images;
            doc["textures"] = textures;
            doc["samplers"] = new JsonArray(new JsonObject { ["wrapS"] = 10497, ["wrapT"] = 10497 });   // repeat (tiled UVs > 1)
        }
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(path)!, binName), bin.ToArray());
        File.WriteAllText(path, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return exported;
    }

    static byte[] Floats(IEnumerable<float> f)
    {
        var a = f.ToArray();
        var b = new byte[a.Length * 4];
        Buffer.BlockCopy(a, 0, b, 0, b.Length);
        return b;
    }
}
