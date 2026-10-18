using System.Numerics;
using System.Text.RegularExpressions;
using GTFormats.Mdl3;

/// <summary>
/// pmsh-check &lt;files.tsv&gt; &lt;groupRegex&gt; &lt;out.txt&gt;: decodes every PMSH data block held in the MDL3 and checks it:
/// layout count = the rule of Mdl3Pmsh.PackedLayouts, every index names a loaded slot and a vertex &lt; V, every vertex
/// used, the index data consumed up to its last quadword, positions inside the block box (box corners' min/max
/// + 1 mm), normals of unit length, triangles agreeing with the normals, directed edges used twice.
/// </summary>
static class PmshCheck
{
    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("pmsh-check <files.tsv> <groupRegex> <out.txt> [--every N]"); return 1; }
        var rx = new Regex(args[1]);
        var files = File.ReadLines(args[0]).Select(l => l.Split('\t')).Where(c => rx.IsMatch(c[1])).ToList();
        // --every N: one file in N (quick sample)
        int every = Array.IndexOf(args, "--every") is int ei && ei >= 0 ? int.Parse(args[ei + 1]) : 1;
        files = [.. files.Where((_, i) => i % every == 0)];
        var fails = new Dictionary<string, List<string>>();
        void Fail(string what, string where) { if (!fails.TryGetValue(what, out var l)) fails[what] = l = []; l.Add(where); }
        long blocks = 0, tris = 0, verts = 0, outside = 0, agree = 0, faces = 0, twice = 0, edges = 0, badLen = 0, normals = 0, exceptions = 0;
        int done = 0, computed = 0;
        long bodyBlocks = 0;
        var computedKinds = new Dictionary<string, int>();
        var normalKinds = new Dictionary<string, long[]>();   // normals, off unit length, faces, agreeing, blocks
        foreach (var c in files)
        {
            Mdl3File m;
            try { m = Mdl3File.Load(c[0]); } catch { continue; }
            var p = m.Pmsh;
            if (p == null) continue;
            var shapeOfMesh = new Dictionary<int, int>();
            for (int s = 0; s < m.Shapes.Count; s++)
                if (Mdl3Pmsh.MeshIndex(m.Data, m.Shapes[s]) is int x && x >= 0) shapeOfMesh.TryAdd(x, s);
            for (int mi = 0; mi < p.Meshes.Count; mi++)
            {
                var me = p.Meshes[mi];
                if (me.Declaration < 0 || me.LayoutArray < 0) continue;
                // data in the MDL3, or (car bodies) in the body_s bundle of the shape that uses the mesh
                var r = m.Data;
                var dataBlocks = me.Data;
                var boxes = me.BBoxes;
                if ((dataBlocks.Count == 0 || dataBlocks[0].PackedVertices == 0) && shapeOfMesh.TryGetValue(mi, out int bs) && m.BodyS?.Shapes.TryGetValue(bs, out var sd) == true)
                {
                    r = sd!.Bundle.R;
                    dataBlocks = m.BodyS.Data(bs);
                    int a = (int)r.U32(sd.Record + 0x0C);
                    boxes = [[.. Enumerable.Range(0, 8).Select(k => new Vector3(r.F32(a + k * 12), r.F32(a + k * 12 + 4), r.F32(a + k * 12 + 8)))]];
                    bodyBlocks += dataBlocks.Count;
                }
                for (int k = 0; k < dataBlocks.Count; k++)
                {
                    var d = dataBlocks[k];
                    if (d.PackedVertices == 0) continue;
                    string where = $"{c[0]} mesh {mi} block {k}";
                    blocks++;
                    var decl = p.Declarations[me.Declaration];
                    if (Mdl3Pmsh.ExpectedLayoutCount(decl, d.Flags) != p.Layouts[me.LayoutArray].Count)
                        Fail($"layout count {p.Layouts[me.LayoutArray].Count} != expected (packed [{string.Join(' ', decl.Packed.Select(e => e.Name))}] flags {d.Flags:X})", where);
                    Mdl3Mesh mesh;
                    long used;
                    try
                    {
                        mesh = p.Decode(r, me, d);
                        Mdl3PmshCommands.Triangles(r.Data, d, out var cmds, out used, p.Entries1C);
                        if (cmds.Count == 0 || cmds[^1].Opcode != 0) Fail($"command stream does not end with op0 (last op{(cmds.Count > 0 ? cmds[^1].Opcode : -1)})", where);
                    }
                    catch (Exception e) { exceptions++; Fail("exception " + e.GetType().Name + " " + e.Message, where); continue; }
                    int V = d.VertexCount;
                    verts += V;
                    tris += mesh.Indices.Length / 3;
                    if (mesh.Indices.Any(i => i < 0)) Fail("index names an unloaded slot", where);
                    if (mesh.Indices.Any(i => i >= V)) Fail("index >= vertex count", where);
                    if (mesh.Indices.Where(i => i >= 0).Distinct().Count() != V) Fail("not every vertex used", where);
                    if (used > d.IndexQuadwords * 128L || used <= (d.IndexQuadwords - 1) * 128L) Fail("index data not consumed up to its last quadword", where);
                    if (boxes.Count > k)
                    {
                        var lo = boxes[k].Aggregate(Vector3.Min) - new Vector3(0.001f); var hi = boxes[k].Aggregate(Vector3.Max) + new Vector3(0.001f);
                        int o = mesh.Positions.Count(q => q.X < lo.X || q.Y < lo.Y || q.Z < lo.Z || q.X > hi.X || q.Y > hi.Y || q.Z > hi.Z);
                        outside += o;
                        if (o > 0) Fail("positions outside the block box", where + $" ({o} of {V})");
                    }
                    string nk = $"{mesh.NormalSource} | packed [{string.Join(' ', decl.Packed.Select(e => e.Name + ":" + e.Type))}] raw [{string.Join(' ', decl.Raw.Select(e => e.Name + ":" + e.Type))}] flags {d.Flags:X}";
                    var ns = normalKinds.TryGetValue(nk, out var nsv) ? nsv : normalKinds[nk] = new long[5];
                    ns[4]++;
                    if (!mesh.NormalsComputed && mesh.Normals != null)
                        foreach (var n in mesh.Normals) { ns[0]++; if (MathF.Abs(n.Length() - 1) > 0.02f) ns[1]++; }
                    if (mesh.NormalsComputed) { computed++; computedKinds[$"packed [{string.Join(' ', decl.Packed.Select(e => e.Name))}] raw [{string.Join(' ', decl.Raw.Select(e => e.Name))}] flags {d.Flags:X}"] = computedKinds.GetValueOrDefault($"packed [{string.Join(' ', decl.Packed.Select(e => e.Name))}] raw [{string.Join(' ', decl.Raw.Select(e => e.Name))}] flags {d.Flags:X}") + 1; }
                    else if (mesh.Normals != null)
                        foreach (var n in mesh.Normals) { normals++; if (MathF.Abs(n.Length() - 1) > 0.02f) badLen++; }
                    var seen = new HashSet<long>();
                    for (int i = 0; i + 2 < mesh.Indices.Length; i += 3)
                    {
                        int a = mesh.Indices[i], b = mesh.Indices[i + 1], e = mesh.Indices[i + 2];
                        if (a < 0 || b < 0 || e < 0 || a >= V || b >= V || e >= V) continue;
                        for (int x = 0; x < 3; x++) { edges++; if (!seen.Add((long)mesh.Indices[i + x] << 32 | (uint)mesh.Indices[i + (x + 1) % 3])) twice++; }
                        if (mesh.Normals == null || mesh.NormalsComputed) continue;
                        var fn = Vector3.Cross(mesh.Positions[b] - mesh.Positions[a], mesh.Positions[e] - mesh.Positions[a]);
                        var vn = mesh.Normals[a] + mesh.Normals[b] + mesh.Normals[e];
                        if (fn.LengthSquared() < 1e-14f || vn.LengthSquared() < 1e-6f) continue;
                        faces++; ns[2]++;
                        if (Vector3.Dot(fn, vn) > 0) { agree++; ns[3]++; }
                    }
                }
            }
            if (++done % 500 == 0) Console.WriteLine($"{done}/{files.Count}");
        }
        using var w = new StreamWriter(args[2]);
        w.WriteLine($"{done} files, {blocks} data blocks ({bodyBlocks} from body_s), {verts} vertices, {tris} triangles, {exceptions} exceptions");
        w.WriteLine($"positions outside the box: {outside}; normals off unit length: {badLen} of {normals}; triangles agreeing with the normals: {(faces == 0 ? 0 : (double)agree / faces):P2} of {faces}; directed edges used twice: {(edges == 0 ? 0 : (double)twice / edges):P3}");
        w.WriteLine($"blocks without stored normals (computed from the faces): {computed}");
        foreach (var (k, n) in computedKinds.OrderByDescending(x => x.Value).Take(12)) w.WriteLine($"{n,8}  {k}");
        w.WriteLine("normals by source (blocks, off unit length, triangles agreeing), worst first:");
        foreach (var (k, s) in normalKinds.Where(x => x.Value[0] > 0).OrderByDescending(x => x.Value[1] + (x.Value[2] - x.Value[3])))
            w.WriteLine($"{s[4],8}  off {(double)s[1] / s[0],7:P2}  agree {(s[2] == 0 ? 0 : (double)s[3] / s[2]),7:P2}  {k}");
        w.WriteLine(fails.Count == 0 ? "no failures" : "failures:");
        foreach (var (k, l) in fails.OrderByDescending(x => x.Value.Count))
        {
            w.WriteLine($"{l.Count,8}  {k}");
            foreach (var x in l.Take(4)) w.WriteLine($"            {x}");
        }
        Console.WriteLine($"{done} files, {blocks} blocks, {fails.Sum(x => x.Value.Count)} failures -> {args[2]}");
        return 0;
    }
}
