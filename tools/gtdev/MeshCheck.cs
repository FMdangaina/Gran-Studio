using System.Numerics;
using System.Text.RegularExpressions;
using GTFormats.Mdl3;

/// <summary>
/// mesh-check &lt;files.tsv&gt; &lt;groupRegex&gt; &lt;out.txt&gt; [--every N]: item 4e. Decodes every shape of every file at every
/// LOD level with the shared decoder (Mdl3Mesh.Decode) and checks: decoded (else the reason), positions finite,
/// indices in range, positions inside the shape bbox (v8 +28, 8 corners + 1 mm), triangle count against shape +26
/// (reported per kind, not a failure: GT5 packed +26 is higher on ~1% of shapes, see 4c).
/// </summary>
static class MeshCheck
{
    /// <summary>Non-degenerate triangles of the plain u16 index data read as one strip (research).</summary>
    static int StripCount(Mdl3File m, Mdl3Shape sh)
    {
        if (sh.TriOffset == 0) return -1;
        var r = m.Data;
        int n = 0;
        for (int i = 0; i + 2 < sh.TriLength; i++)
        {
            int a = r.U16((int)sh.TriOffset + i * 2), b = r.U16((int)sh.TriOffset + i * 2 + 2), c = r.U16((int)sh.TriOffset + i * 2 + 4);
            if (a != b && b != c && a != c) n++;
        }
        return n;
    }

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("mesh-check <files.tsv> <groupRegex> <out.txt> [--every N]"); return 1; }
        var rx = new Regex(args[1]);
        int every = Array.IndexOf(args, "--every") is int ei && ei >= 0 ? int.Parse(args[ei + 1]) : 1;
        var files = File.ReadLines(args[0]).Select(l => l.Split('\t')).Where(c => rx.IsMatch(c[1])).Where((_, i) => i % every == 0).ToList();
        using var w = new StreamWriter(args[2]);
        int done = 0;
        foreach (var grp in files.GroupBy(c => c[1]).OrderBy(g => g.Key))
        {
            var fails = new Dictionary<string, List<string>>();
            void Fail(string what, string where) { if (!fails.TryGetValue(what, out var l)) fails[what] = l = []; l.Add(where); }
            var kinds = new SortedDictionary<string, long[]>();   // shapes, triangles, +26 equal, +26 higher, +26 lower
            long shapes = 0, decoded = 0, tris = 0, empty = 0, lowLodEmpty = 0;
            var higherExample = new Dictionary<string, string>();
            var flagStats = new SortedDictionary<string, int>();
            int oldVersion = 0;
            foreach (var c in grp)
            {
                // versions 1-4 (19 car files) have another header layout: counted, not decoded
                if (int.Parse(c[2]) < 6) { oldVersion++; continue; }
                Mdl3File m;
                try { m = Mdl3File.Load(c[0]); } catch (Exception e) { Fail("file not loaded: " + e.Message, c[0]); continue; }
                for (int s = 0; s < m.Shapes.Count; s++)
                {
                    var sh = m.Shapes[s];
                    shapes++;
                    int lods;
                    try { lods = Mdl3Mesh.LodCount(m, s); } catch (Exception e) { Fail("exception " + e.GetType().Name + " " + e.Message, $"{c[0]} shape {s}"); continue; }
                    bool ok = true;
                    for (int lod = 0; lod < lods; lod++)
                    {
                        string where = $"{c[0]} shape {s}" + (lods > 1 ? $" lod {lod}" : "");
                        Mdl3Mesh? mesh;
                        string reason;
                        try { mesh = Mdl3Mesh.Decode(m, s, lod, out reason); }
                        catch (Exception e) { Fail("exception " + e.GetType().Name + " " + e.Message, where); ok = false; continue; }
                        if (mesh == null && reason == "empty shape") { empty++; break; }
                        if (mesh == null) { Fail("not decoded: " + reason, where); ok = false; continue; }
                        string kind = mesh.Packed ? (sh.FvfIndex < 0 && m.Pmsh != null ? "PMSH" : "GT5 packed") : "plain";
                        if (lod == 0)
                        {
                            var k = kinds.TryGetValue(kind, out var kv) ? kv : kinds[kind] = new long[5];
                            int t = mesh.Indices.Length / 3;
                            k[0]++; k[1] += t; tris += t;
                            k[t == sh.TriCount ? 2 : sh.TriCount > t ? 3 : 4]++;
                            if (sh.TriCount > t) higherExample.TryAdd(kind, $"{where}: {t} triangles, +26 {sh.TriCount}, v {mesh.Positions.Length}, tri length {sh.TriLength}, flags {sh.Flags:X4}");
                            if (kind == "plain")
                            {
                                string fk = $"primitive {sh.Flags & 0xF}, +26 - triangles {(sh.TriCount - t is int dd && Math.Abs(dd) <= 2 ? dd.ToString() : "other")}";
                                flagStats[fk] = flagStats.GetValueOrDefault(fk) + 1;
                            }
                        }
                        if (mesh.Positions.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z))) Fail("position not finite", where);
                        if (mesh.Indices.Any(i => i < 0 || i >= mesh.Positions.Length))
                            Fail("index out of range", where + $" (max {mesh.Indices.Max()}, vertices {mesh.Positions.Length}, {mesh.Indices.Count(i => i < 0 || i >= mesh.Positions.Length)} bad of {mesh.Indices.Length})");
                        if (mesh.Indices.Length == 0 && sh.TriCount > 0)
                        {
                            if (lod == 0) Fail("no triangles at LOD0 (shape +26 > 0)", where);
                            else lowLodEmpty++;   // part dropped at a low LOD level
                        }
                        if (sh.BBoxOffset != 0 && mesh.Positions.Length > 0)
                        {
                            var r = m.Data;
                            var box = Enumerable.Range(0, 8).Select(i => new Vector3(r.F32((int)sh.BBoxOffset + i * 12), r.F32((int)sh.BBoxOffset + i * 12 + 4), r.F32((int)sh.BBoxOffset + i * 12 + 8))).ToArray();
                            var lo = box.Aggregate(Vector3.Min) - new Vector3(0.001f); var hi = box.Aggregate(Vector3.Max) + new Vector3(0.001f);
                            int o = mesh.Positions.Count(p => p.X < lo.X || p.Y < lo.Y || p.Z < lo.Z || p.X > hi.X || p.Y > hi.Y || p.Z > hi.Z);
                            if (o > 0) Fail($"positions outside the shape bbox ({kind})", where + $" ({o} of {mesh.Positions.Length})");
                        }
                    }
                    if (ok) decoded++;
                }
                if (++done % 500 == 0) Console.WriteLine($"{done}/{files.Count}");
            }
            w.WriteLine($"== {grp.Key}: {grp.Count()} files, {shapes} shapes, {decoded} decoded at every LOD ({(shapes == 0 ? 0 : (double)decoded / shapes):P3}; {empty} empty shapes count as decoded), {tris} LOD0 triangles");
            if (oldVersion > 0) w.WriteLine($"   {oldVersion} files of MDL3 version < 6 skipped (other header layout)");
            if (lowLodEmpty > 0) w.WriteLine($"   {lowLodEmpty} shape LOD levels > 0 without triangles (part dropped at that level)");
            foreach (var (k, v) in kinds)
            {
                w.WriteLine($"   {k,-10} {v[0],8} shapes {v[1],11} tris; +26: equal {v[2]}, higher {v[3]}, lower {v[4]}");
                if (higherExample.TryGetValue(k, out var ex)) w.WriteLine($"              +26 higher e.g. {ex}");
            }
            foreach (var (k, n) in flagStats) w.WriteLine($"   plain {k}: {n}");
            w.WriteLine(fails.Count == 0 ? "   no failures" : "   failures:");
            foreach (var (k, l) in fails.OrderByDescending(x => x.Value.Count))
            {
                w.WriteLine($"{l.Count,8}  {k}");
                foreach (var x in l.Take(4)) w.WriteLine($"            {x}");
            }
            w.Flush();
        }
        Console.WriteLine($"{done} files -> {args[2]}");
        return 0;
    }
}
