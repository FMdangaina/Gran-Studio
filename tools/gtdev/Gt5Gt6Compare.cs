using System.Text.RegularExpressions;
using GTFormats.Mdl3;

/// <summary>
/// gt5-gt6 &lt;gt5 root&gt; &lt;gt6 root&gt; &lt;out.txt&gt;: item 5c, the same car code in GT5 (car\{hq,race}\MMMMNNNN) and GT6
/// (car\MMMM\NNNN\{hq,race}\body). Per pair: MDL3 version, models (names added / dropped), shapes, materials, texture
/// names, Maya shape paths in common, custom groups and colour variations (GT6 only). Totals + one line per pair.
/// </summary>
static class Gt5Gt6Compare
{
    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("gt5-gt6 <gt5 root> <gt6 root> <out.txt>"); return 1; }
        var c = new SortedDictionary<string, int>();
        var ex = new Dictionary<string, string>();
        void Count(string k, string? e = null) { c[k] = c.GetValueOrDefault(k) + 1; if (e != null && !ex.ContainsKey(k)) ex[k] = e; }
        using var tsv = new StreamWriter(Path.ChangeExtension(args[2], ".tsv"));
        tsv.WriteLine("code\tkind\tshapes gt5\tshapes gt6\tmaterials gt5\tmaterials gt6\timages gt5\timages gt6\tshape paths in common\ttexture names in common\tmodels only gt6");
        int pairs = 0, gt6Only = 0;
        var gt6Bodies = Directory.EnumerateFiles(Path.Combine(args[1], "car"), "body", SearchOption.AllDirectories).ToList();
        foreach (var body6 in gt6Bodies)
        {
            var m = Regex.Match(body6.Replace('\\', '/'), @"/car/(\d{4})/(\d{4})/(hq|race)/body$");
            if (!m.Success) continue;
            string code = m.Groups[1].Value + m.Groups[2].Value, kind = m.Groups[3].Value;
            string body5 = Path.Combine(args[0], "car", kind, code);
            if (!File.Exists(body5)) { gt6Only++; continue; }
            pairs++;
            try
            {
                var a = Mdl3File.Load(body5);
                var b = Mdl3File.Load(body6);
                Count($"{kind}: versions GT5 v{a.Header.Version} / GT6 v{b.Header.Version}");
                var ma = a.ModelNames.Values.ToHashSet();
                var mb = b.ModelNames.Values.ToHashSet();
                foreach (var x in mb.Except(ma)) Count($"{kind}: model only in GT6: {x}", code);
                foreach (var x in ma.Except(mb)) Count($"{kind}: model only in GT5: {x}", code);
                if (ma.SetEquals(mb)) Count($"{kind}: same model names");

                // Maya shape paths (the source scene) in common: same model rebuilt or a new one?
                var pa = a.ShapeNames.Values.SelectMany(x => x).ToHashSet();
                var pb = b.ShapeNames.Values.SelectMany(x => x).ToHashSet();
                double common = pa.Count == 0 ? 0 : pa.Intersect(pb).Count() / (double)pa.Count;
                Count($"{kind}: GT5 shape paths found in GT6: {Bucket(common)}", $"{code} {pa.Count}/{pb.Count}");

                static string File_(string n) => Path.GetFileNameWithoutExtension(n.Replace('\\', '/')).ToLowerInvariant();
                var ta = a.TextureInfos.Select(t => File_(t.Name)).ToHashSet();
                var tb = b.TextureInfos.Select(t => File_(t.Name)).ToHashSet();
                double tcommon = ta.Count == 0 ? 0 : ta.Intersect(tb).Count() / (double)ta.Count;
                Count($"{kind}: GT5 texture names found in GT6: {Bucket(tcommon)}", code);
                var na = a.Materials.Select(q => q.Name).ToHashSet();
                var nb = b.Materials.Select(q => Regex.Replace(q.Name, @"_CLR_VARI_\d+(_PaintExtraBRDF)?$", "")).ToHashSet();
                Count($"{kind}: GT5 material names found in GT6 (without CLR_VARI): {Bucket(na.Count == 0 ? 0 : na.Intersect(nb).Count() / (double)na.Count)}", code);
                Count($"{kind}: shapes GT6 vs GT5: {Ratio(b.Shapes.Count, a.Shapes.Count)}", $"{code} {a.Shapes.Count} -> {b.Shapes.Count}");
                Count($"{kind}: materials GT6 vs GT5: {Ratio(b.Materials.Count, a.Materials.Count)}", $"{code} {a.Materials.Count} -> {b.Materials.Count}");
                Count($"{kind}: bones GT6 vs GT5: {Ratio(b.Bones.Count, a.Bones.Count)}", $"{code} {a.Bones.Count} -> {b.Bones.Count}");
                // geometry storage in GT6: shapes without an fvf = PMSH packed meshes (data in body_s)
                int b6packed = b.Shapes.Count(s => s.FvfIndex < 0);
                Count($"{kind}: GT6 PMSH shapes (no fvf): {Bucket(b6packed / (double)Math.Max(1, b.Shapes.Count))}");
                var map = Mdl3StreamMap.Read(b.Data);
                if (map != null)
                {
                    Count($"{kind}: GT6 custom groups {map.Groups.Count(g => g.Name != "/")}");
                    var a5custom = a.ShapeNames.Values.SelectMany(x => x).Any(p => p.Contains("CUSTOM_"));
                    Count($"{kind}: GT5 has CUSTOM_ nodes: {(a5custom ? "yes" : "no")}, GT6 custom groups: {(map.Groups.Count > 1 ? "yes" : "no")}");
                }
                tsv.WriteLine($"{code}\t{kind}\t{a.Shapes.Count}\t{b.Shapes.Count}\t{a.Materials.Count}\t{b.Materials.Count}\t{a.TextureInfos.Count}\t{b.TextureInfos.Count}\t{common:0.00}\t{tcommon:0.00}\t{string.Join(",", mb.Except(ma))}");
            }
            catch (Exception e) { Count("FAIL: exception " + e.GetType().Name, $"{code} {kind} {e.Message}"); }
            if (pairs % 200 == 0) Console.WriteLine($"{pairs} pairs");
        }
        using var w = new StreamWriter(args[2]);
        w.WriteLine($"{gt6Bodies.Count} GT6 bodies, {pairs} with a GT5 file of the same code, {gt6Only} GT6 only");
        foreach (var (k, n) in c) w.WriteLine($"{n,8}  {k}{(ex.TryGetValue(k, out var e) ? "   e.g. " + e : "")}");
        Console.WriteLine($"{pairs} pairs -> {args[2]}");
        return 0;
    }

    static string Bucket(double f) => f >= 0.999 ? "all" : f >= 0.9 ? "90-99%" : f >= 0.5 ? "50-90%" : f > 0 ? "1-50%" : "none";
    static string Ratio(int b, int a) => a == b ? "same" : a == 0 ? "GT5 0" : (b / (double)a) switch
    {
        < 0.5 => "< 0.5x", < 0.9 => "0.5-0.9x", < 1.1 => "about the same", < 2 => "1.1-2x", < 4 => "2-4x", _ => ">= 4x",
    };
}
