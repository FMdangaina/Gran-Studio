using System.Numerics;
using System.Text;
using GTFormats.Mdl3;

/// <summary>
/// mdl3-3ds &lt;file&gt; &lt;out.3ds&gt; [--all-models] [--lod N] [--long-names] [--yup]: the shapes of mdl3-obj as a 3DS file
/// for ZModeler 2 / 3ds Max, with the hierarchy in the keyframer: file -> model -> meaningful Maya groups (dummy nodes)
/// -> one mesh object per shape. Transforms are identity (vertices are already in model space). 3DS names are kept to
/// 10 characters (the format's limit) unless --long-names; &lt;out&gt;_names.txt maps them to the full names.
/// Axes: GT is Y-up; 3DS is Z-up, so (x, y, z) -> (x, -z, y) unless --yup. Meshes over 65535 vertices are split.
/// </summary>
static class Mdl3Max3ds
{
    sealed class Node
    {
        public string Name = "";
        public bool Dummy;
        public int Parent = -1;
        public int Id;
    }

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("mdl3-3ds <file> <out.3ds> [--all-models] [--lod N] [--long-names] [--yup]"); return 1; }
        var opts = args.Skip(2).ToList();
        bool all = opts.Contains("--all-models"), longNames = opts.Contains("--long-names"), yup = opts.Contains("--yup");
        int li = opts.IndexOf("--lod"), lod = li >= 0 ? int.Parse(opts[li + 1]) : 0;
        int maxName = longNames ? 63 : 10;

        var m = Mdl3File.Load(args[0]);
        string dir = Path.GetDirectoryName(Path.GetFullPath(args[1]))!, stem = Path.GetFileNameWithoutExtension(args[1]);
        var shapes = Mdl3Obj.SelectShapes(m, all);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var map = new StringBuilder();
        string Unique(string wanted, string full)
        {
            string b = new([.. wanted.Replace('.', '_').Where(ch => char.IsLetterOrDigit(ch) || ch == '_')]);
            if (b.Length == 0) b = "node";
            string name = b.Length > maxName ? b[..maxName] : b;
            for (int k = 1; !used.Add(name); k++)
            {
                string suf = k.ToString();
                name = (b.Length > maxName - suf.Length ? b[..(maxName - suf.Length)] : b) + suf;
            }
            map.AppendLine($"{name}\t{full}");
            return name;
        }

        // nodes: root dummy, groups (dummies), meshes
        var nodes = new List<Node>();
        int AddNode(string name, bool dummy, int parent) { nodes.Add(new Node { Name = name, Dummy = dummy, Parent = parent, Id = nodes.Count }); return nodes.Count - 1; }
        int root = AddNode(Unique(Path.GetFileName(args[0]), args[0]), true, -1);
        var groups = new Dictionary<string, int>();
        var meshes = new List<(string name, Mdl3Mesh mesh, int material, int node)>();
        int skipped = 0;
        foreach (int s in shapes)
        {
            Mdl3Mesh? mesh;
            try { mesh = Mdl3Mesh.Decode(m, s, lod, out _); } catch { mesh = null; }
            if (mesh == null || mesh.Indices.Length == 0) { skipped++; continue; }
            var raw = (m.ShapeNames.TryGetValue(s, out var nm) ? nm[0] : $"shape{s}").Split('|', StringSplitOptions.RemoveEmptyEntries);
            var path = ShapeNaming.Nodes(string.Join('|', raw.Take(Math.Max(1, raw.Length - 2))));
            if (path.Count == 0) path.Add("shape");
            int parent = root;
            string key = "";
            foreach (var g in path)
            {
                key += "|" + g;
                if (!groups.TryGetValue(key, out int gi)) groups[key] = gi = AddNode(Unique(g, key), true, parent);
                parent = gi;
            }
            string full = ShapeNaming.Short(m, s);
            var leaf = ShapeNaming.Nodes(string.Join('|', raw)).LastOrDefault() ?? $"s{s}";
            foreach (var (part, k) in Split(mesh).Select((p, k) => (p, k)))
            {
                string wanted = longNames ? full + (k > 0 ? $"_p{k}" : "") : $"{leaf}_{s}" + (k > 0 ? $"p{k}" : "");
                // short names: keep the shape index at the end so it survives the 10 characters
                if (!longNames)
                {
                    string suf = $"_{s}" + (k > 0 ? $"p{k}" : "");
                    string b = new([.. leaf.Where(ch => char.IsLetterOrDigit(ch) || ch == '_')]);
                    wanted = (b.Length > maxName - suf.Length ? b[..Math.Max(0, maxName - suf.Length)] : b) + suf;
                }
                string name = Unique(wanted, full + (k > 0 ? $" part {k}" : ""));
                meshes.Add((name, part, m.Shapes[s].MaterialIndex, AddNode(name, false, parent)));
            }
        }

        var materials = meshes.Select(x => x.material).Where(i => i >= 0).Distinct().Order().ToList();
        var tex = Mdl3Obj.MaterialTextures(m, materials, dir, stem);
        Vector3 Axis(Vector3 p) => yup ? p : new Vector3(p.X, -p.Z, p.Y);

        var w = new ChunkWriter();
        w.Begin(0x4D4D);
        w.Chunk(0x0002, () => w.U32(3));
        w.Begin(0x3D3D);
        w.Chunk(0x3D3E, () => w.U32(3));
        foreach (int i in materials)
        {
            w.Begin(0xAFFF);
            w.Chunk(0xA000, () => w.Str(MatName(i)));
            foreach (int id in new[] { 0xA010, 0xA020 })
                w.Chunk(id, () => w.Chunk(0x0011, () => { w.U8(204); w.U8(204); w.U8(204); }));
            w.Chunk(0xA030, () => w.Chunk(0x0011, () => { w.U8(255); w.U8(255); w.U8(255); }));
            if (tex[i].Diffuse is { } file)
            {
                w.Begin(0xA200);
                w.Chunk(0x0030, () => w.U16(100));
                w.Chunk(0xA300, () => w.Str(file));
                w.End();
            }
            w.End();
        }
        w.Chunk(0x0100, () => w.F32(1f));
        foreach (var (name, mesh, material, _) in meshes)
        {
            w.Begin(0x4000);
            w.Str(name);
            w.Begin(0x4100);
            w.Chunk(0x4110, () => { w.U16(mesh.Positions.Length); foreach (var p in mesh.Positions) { var q = Axis(p); w.F32(q.X); w.F32(q.Y); w.F32(q.Z); } });
            if (mesh.Uvs != null)
                w.Chunk(0x4140, () => { w.U16(mesh.Uvs.Length); foreach (var t in mesh.Uvs) { w.F32(t.X); w.F32(1 - t.Y); } });
            w.Chunk(0x4160, () => { foreach (float f in new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 }) w.F32(f); });
            int faces = mesh.Indices.Length / 3;
            w.Begin(0x4120);
            w.U16(faces);
            for (int f = 0; f < faces; f++) { w.U16(mesh.Indices[f * 3]); w.U16(mesh.Indices[f * 3 + 1]); w.U16(mesh.Indices[f * 3 + 2]); w.U16(7); }
            if (material >= 0)
                w.Chunk(0x4130, () => { w.Str(MatName(material)); w.U16(faces); for (int f = 0; f < faces; f++) w.U16(f); });
            w.Chunk(0x4150, () => { for (int f = 0; f < faces; f++) w.U32(1); });
            w.End();   // face array
            w.End();   // tri object
            w.End();   // named object
        }
        w.End();   // editor

        // keyframer: one node per dummy / mesh, identity transforms, parent links = the hierarchy
        w.Begin(0xB000);
        w.Chunk(0xB00A, () => { w.U16(5); w.Str(""); w.U32(100); });
        w.Chunk(0xB008, () => { w.U32(0); w.U32(100); });
        w.Chunk(0xB009, () => w.U32(0));
        foreach (var n in nodes)
        {
            w.Begin(0xB002);
            w.Chunk(0xB030, () => w.U16(n.Id));
            w.Chunk(0xB010, () => { w.Str(n.Dummy ? "$$$DUMMY" : n.Name); w.U16(0x4000); w.U16(0); w.U16(n.Parent < 0 ? 0xFFFF : n.Parent); });
            if (n.Dummy) w.Chunk(0xB011, () => w.Str(n.Name));
            w.Chunk(0xB013, () => { w.F32(0); w.F32(0); w.F32(0); });
            if (n.Dummy) w.Chunk(0xB014, () => { for (int k = 0; k < 6; k++) w.F32(0); });
            Track(w, 0xB020, () => { w.F32(0); w.F32(0); w.F32(0); });
            Track(w, 0xB021, () => { w.F32(0); w.F32(0); w.F32(0); w.F32(1); });
            Track(w, 0xB022, () => { w.F32(1); w.F32(1); w.F32(1); });
            w.End();
        }
        w.End();   // keyframer
        w.End();   // main
        File.WriteAllBytes(args[1], w.ToArray());
        File.WriteAllText(Path.Combine(dir, stem + "_names.txt"), map.ToString());
        Console.WriteLine($"{meshes.Count} mesh objects, {nodes.Count(n => n.Dummy)} group nodes, {skipped} shapes skipped -> {args[1]}");
        return 0;

        string MatName(int i)
        {
            string n = string.IsNullOrEmpty(m.Materials[i].Name) ? $"m{i:D3}" : $"m{i:D3}_{m.Materials[i].Name}";
            n = new([.. n.Where(ch => char.IsLetterOrDigit(ch) || ch == '_')]);
            return n.Length > 16 ? n[..16] : n;   // 3DS material names: 16 characters
        }
    }

    /// <summary>
    /// 3ds-dump &lt;file.3ds&gt;: walks the chunk tree (lengths must nest exactly), prints chunk counts, the objects and the
    /// keyframer hierarchy (node id, parent, name) as an indented tree.
    /// </summary>
    public static int Dump(string[] args)
    {
        var d = File.ReadAllBytes(args[0]);
        var counts = new SortedDictionary<int, int>();
        var kfNodes = new List<(int id, int parent, string name)>();
        int errors = 0;
        void Walk(int at, int end, int depth)
        {
            while (at < end)
            {
                if (at + 6 > end) { errors++; Console.WriteLine($"truncated header at {at:X}"); return; }
                int id = BitConverter.ToUInt16(d, at), len = BitConverter.ToInt32(d, at + 2);
                if (len < 6 || at + len > end) { errors++; Console.WriteLine($"bad length {len} for {id:X4} at {at:X}"); return; }
                counts[id] = counts.GetValueOrDefault(id) + 1;
                int body = at + 6;
                switch (id)
                {
                    case 0x4D4D: case 0x3D3D: case 0xB000: case 0xAFFF: case 0xA200: case 0xA010: case 0xA020: case 0xA030:
                        Walk(body, at + len, depth + 1); break;
                    case 0x4000: { int z = Array.IndexOf(d, (byte)0, body); Walk(z + 1, at + len, depth + 1); break; }
                    case 0x4100: Walk(body, at + len, depth + 1); break;
                    case 0x4120: { int n = BitConverter.ToUInt16(d, body); Walk(body + 2 + n * 8, at + len, depth + 1); break; }
                    case 0xB002:
                    {
                        int nid = -1, parent = -1; string name = "", inst = "";
                        int p = body;
                        while (p < at + len)
                        {
                            int cid = BitConverter.ToUInt16(d, p), clen = BitConverter.ToInt32(d, p + 2);
                            counts[cid] = counts.GetValueOrDefault(cid) + 1;
                            if (cid == 0xB030) nid = BitConverter.ToUInt16(d, p + 6);
                            if (cid == 0xB010)
                            {
                                int z = Array.IndexOf(d, (byte)0, p + 6);
                                name = Encoding.ASCII.GetString(d, p + 6, z - p - 6);
                                parent = BitConverter.ToInt16(d, z + 5);
                            }
                            if (cid == 0xB011) { int z = Array.IndexOf(d, (byte)0, p + 6); inst = Encoding.ASCII.GetString(d, p + 6, z - p - 6); }
                            if (clen < 6) { errors++; break; }
                            p += clen;
                        }
                        if (p != at + len) { errors++; Console.WriteLine($"node chunk lengths do not add up at {at:X}"); }
                        kfNodes.Add((nid, parent, name == "$$$DUMMY" ? $"[{inst}]" : name));
                        break;
                    }
                }
                at += len;
            }
            if (at != end) { errors++; Console.WriteLine($"chunks overrun their parent by {at - end}"); }
        }
        Walk(0, d.Length, 0);
        Console.WriteLine($"{d.Length} bytes, {errors} errors; chunks: " + string.Join(' ', counts.Select(x => $"{x.Key:X4}x{x.Value}")));
        var children = kfNodes.GroupBy(n => n.parent).ToDictionary(g => g.Key, g => g.ToList());
        void Print(int parent, int depth, int limit)
        {
            if (!children.TryGetValue(parent, out var l)) return;
            foreach (var n in l.Take(limit))
            {
                Console.WriteLine($"{new string(' ', depth * 2)}{n.name}");
                Print(n.id, depth + 1, limit);
            }
            if (l.Count > limit) Console.WriteLine($"{new string(' ', depth * 2)}... {l.Count - limit} more");
        }
        Print(-1, 0, args.Length > 1 ? int.Parse(args[1]) : 12);
        return errors == 0 ? 0 : 1;
    }

    /// <summary>Track chunk with one key at frame 0 (u16 flags, 8 reserved bytes, u32 key count, u32 frame, u16 0, value).</summary>
    static void Track(ChunkWriter w, int id, Action value) => w.Chunk(id, () =>
    {
        w.U16(0); w.U32(0); w.U32(0); w.U32(1);
        w.U32(0); w.U16(0);
        value();
    });

    /// <summary>3DS counts are u16: meshes over 65535 vertices or faces are split, vertices re-indexed per part.</summary>
    static IEnumerable<Mdl3Mesh> Split(Mdl3Mesh mesh)
    {
        if (mesh.Positions.Length <= 65535 && mesh.Indices.Length / 3 <= 65535) { yield return mesh; yield break; }
        var remap = new Dictionary<int, int>();
        var idx = new List<int>();
        Mdl3Mesh Flush()
        {
            var order = remap.OrderBy(x => x.Value).Select(x => x.Key).ToArray();
            var part = new Mdl3Mesh
            {
                Packed = mesh.Packed,
                Positions = [.. order.Select(i => mesh.Positions[i])],
                Normals = mesh.Normals == null ? null : [.. order.Select(i => mesh.Normals[i])],
                Uvs = mesh.Uvs == null ? null : [.. order.Select(i => mesh.Uvs[i])],
                Indices = [.. idx],
            };
            remap.Clear(); idx.Clear();
            return part;
        }
        for (int t = 0; t + 2 < mesh.Indices.Length; t += 3)
        {
            if (remap.Count > 65532 || idx.Count / 3 >= 65535) yield return Flush();
            for (int k = 0; k < 3; k++)
            {
                int v = mesh.Indices[t + k];
                if (!remap.TryGetValue(v, out int n)) remap[v] = n = remap.Count;
                idx.Add(n);
            }
        }
        if (idx.Count > 0) yield return Flush();
    }

    /// <summary>Little-endian 3DS chunk writer: u16 id, u32 length (header included), nested by Begin/End.</summary>
    sealed class ChunkWriter
    {
        readonly MemoryStream ms = new();
        readonly BinaryWriter bw;
        readonly Stack<long> open = new();
        public ChunkWriter() => bw = new BinaryWriter(ms);
        public void Begin(int id) { bw.Write((ushort)id); open.Push(ms.Position); bw.Write(0u); }
        public void End()
        {
            long at = open.Pop(), end = ms.Position;
            ms.Position = at;
            bw.Write((uint)(end - at + 2));
            ms.Position = end;
        }
        public void Chunk(int id, Action body) { Begin(id); body(); End(); }
        public void U8(int v) => bw.Write((byte)v);
        public void U16(int v) => bw.Write((ushort)v);
        public void U32(uint v) => bw.Write(v);
        public void U32(int v) => bw.Write((uint)v);
        public void F32(float v) => bw.Write(v);
        public void Str(string s) { bw.Write(Encoding.ASCII.GetBytes(s)); bw.Write((byte)0); }
        public byte[] ToArray() => ms.ToArray();
    }
}
