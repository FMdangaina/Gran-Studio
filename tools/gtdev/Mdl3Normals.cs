using System.Numerics;
using System.Text.RegularExpressions;
using GTFormats.Mdl3;

/// <summary>
/// mdl3-normals &lt;files.tsv&gt; &lt;groupRegex&gt; &lt;out.txt&gt;: decodes every shape at every LOD level (Mdl3Mesh) and compares
/// the stored vertex normals with the triangle winding: per triangle, the sign of dot(face normal, sum of its vertex
/// normals). A shape is "ccw" when at least 90% agree, "cw" when at most 10% agree, else "mixed". Also counts shapes
/// without normals and normals that are not unit length. Lists the worst mixed shapes per LOD level.
/// </summary>
static class Mdl3Normals
{
    sealed class Stat
    {
        public int Shapes, Ccw, Cw, Mixed, NoNormals, Empty, Failed;
        public long Tris, Agree, Degenerate, BadLength, Normals, Edges, EdgeTwice;
    }

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("mdl3-normals <files.tsv> <groupRegex> <out.txt>"); return 1; }
        var rx = new Regex(args[1]);
        var files = File.ReadLines(args[0]).Select(l => l.Split('\t')).Where(c => rx.IsMatch(c[1])).ToList();
        var stats = new SortedDictionary<string, Stat>();
        var worst = new List<(double share, string what)>();
        var noNormals = new Dictionary<string, int>();
        int done = 0;
        foreach (var c in files)
        {
            Mdl3File m;
            try { m = Mdl3File.Load(c[0]); } catch { continue; }   // old v1/v2 files
            for (int s = 0; s < m.Shapes.Count; s++)
            {
                int lods;
                try { lods = Mdl3Mesh.LodCount(m, s); } catch { lods = 1; }
                for (int lod = 0; lod < lods; lod++)
                {
                    string key = $"{c[1]}  {(Mdl3PackedShape.IsPacked(m.Data, m.Shapes[s]) ? "packed" : "plain ")}  lod{lod}";
                    if (!stats.TryGetValue(key, out var st)) stats[key] = st = new Stat();
                    Mdl3Mesh? mesh;
                    try { mesh = Mdl3Mesh.Decode(m, s, lod, out _); }
                    catch { st.Failed++; continue; }
                    if (mesh == null) continue;
                    st.Shapes++;
                    if (mesh.Indices.Length == 0) { st.Empty++; continue; }
                    if (mesh.Normals == null)
                    {
                        st.NoNormals++;
                        var f = m.Shapes[s].FvfIndex >= 0 ? m.Fvfs[m.Shapes[s].FvfIndex] : null;
                        string fvf = f == null ? "-" : $"{f.Name}' normal '{f["normal"]?.Type.ToString() ?? "none"}";
                        noNormals[$"{c[1]} {(mesh.Packed ? "packed" : "plain")} fvf '{fvf}'"] = noNormals.GetValueOrDefault($"{c[1]} {(mesh.Packed ? "packed" : "plain")} fvf '{fvf}'") + 1;
                        continue;
                    }
                    // winding consistency without normals: a directed edge used twice means two neighbours disagree
                    var edges = new HashSet<long>();
                    for (int i = 0; i < mesh.Indices.Length; i += 3)
                        for (int e = 0; e < 3; e++)
                        {
                            long u = mesh.Indices[i + e], v = mesh.Indices[i + (e + 1) % 3];
                            st.Edges++;
                            if (!edges.Add(u << 32 | v)) st.EdgeTwice++;
                        }
                    long agree = 0, tris = 0;
                    foreach (var n in mesh.Normals) { st.Normals++; if (MathF.Abs(n.Length() - 1) > 0.02f) st.BadLength++; }
                    for (int i = 0; i < mesh.Indices.Length; i += 3)
                    {
                        int a = mesh.Indices[i], b = mesh.Indices[i + 1], d = mesh.Indices[i + 2];
                        var fn = Vector3.Cross(mesh.Positions[b] - mesh.Positions[a], mesh.Positions[d] - mesh.Positions[a]);
                        var vn = mesh.Normals[a] + mesh.Normals[b] + mesh.Normals[d];
                        float dot = Vector3.Dot(fn, vn);
                        if (fn.LengthSquared() < 1e-14f || vn.LengthSquared() < 1e-6f) { st.Degenerate++; continue; }
                        tris++;
                        if (dot > 0) agree++;
                    }
                    st.Tris += tris; st.Agree += agree;
                    double share = tris == 0 ? 1 : (double)agree / tris;
                    if (share >= 0.9) st.Ccw++;
                    else if (share <= 0.1) st.Cw++;
                    else
                    {
                        st.Mixed++;
                        string name = m.ShapeNames.TryGetValue(s, out var nm) ? nm[0] : "";
                        worst.Add((Math.Abs(share - 0.5), $"{share:P0} agree, {tris} tris, lod{lod}  {c[0]} s{s} {name}"));
                    }
                }
            }
            if (++done % 200 == 0) Console.WriteLine($"{done}/{files.Count}");
        }
        using var w = new StreamWriter(args[2]);
        w.WriteLine("group  kind  lod: shapes, ccw (>=90% triangles agree with the normals), cw (<=10%), mixed, no normals, empty, failed; triangle agreement; normals off unit length");
        foreach (var (k, st) in stats.Where(x => x.Value.Shapes + x.Value.Failed > 0))
            w.WriteLine($"{k,-34} {st.Shapes,7} ccw {st.Ccw,7} cw {st.Cw,6} mixed {st.Mixed,6} noN {st.NoNormals,6} empty {st.Empty,5} failed {st.Failed,3}  agree {(st.Tris == 0 ? 0 : (double)st.Agree / st.Tris):P2} of {st.Tris}  degenerate {st.Degenerate}  badLen {st.BadLength}/{st.Normals}  edge twice {(st.Edges == 0 ? 0 : (double)st.EdgeTwice / st.Edges):P2}");
        w.WriteLine();
        w.WriteLine("Shapes without normals by vertex format:");
        foreach (var (k, n) in noNormals.OrderByDescending(x => x.Value)) w.WriteLine($"{n,7}  {k}");
        w.WriteLine();
        w.WriteLine("Most mixed shapes (agreement closest to 50%):");
        foreach (var x in worst.OrderBy(x => x.share).Take(60)) w.WriteLine("  " + x.what);
        Console.WriteLine($"{done} files -> {args[2]}");
        return 0;
    }
}
