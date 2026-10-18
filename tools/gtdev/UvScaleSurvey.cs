using System.Text.RegularExpressions;
using GTFormats.Mdl3;

/// <summary>uv-scale &lt;files.tsv&gt; &lt;groupRegex&gt; &lt;out.txt&gt;: how the vertex programs treat the UVs (Mdl3UvScale), per used material.</summary>
static class UvScaleSurvey
{
    public static int Run(string[] args)
    {
        var rx = new Regex(args[1]);
        var files = File.ReadLines(args[0]).Select(l => l.Split('\t')).Where(c => rx.IsMatch(c[1])).ToList();
        var c = new SortedDictionary<string, int>();
        var ex = new Dictionary<string, string>();
        int done = 0;
        foreach (var f in files)
        {
            try
            {
                var m = Mdl3File.Load(f[0]);
                foreach (int mat in m.Shapes.Select(s => (int)s.MaterialIndex).Where(i => i >= 0 && i < m.Materials.Count).Distinct())
                {
                    Mdl3UvScale.Explain(m, mat, out string how);
                    string k = $"{f[1]}: {Regex.Replace(how, @"c\d+", "cN")}";
                    c[k] = c.GetValueOrDefault(k) + 1;
                    ex.TryAdd(k, $"{f[0]} m{mat}");
                }
            }
            catch (Exception e) { string k = $"{f[1]}: FAIL {e.GetType().Name}"; c[k] = c.GetValueOrDefault(k) + 1; ex.TryAdd(k, f[0]); }
            if (++done % 500 == 0) Console.WriteLine($"{done}/{files.Count}");
        }
        File.WriteAllLines(args[2], c.Select(x => $"{x.Value,8}  {x.Key}   e.g. {ex[x.Key]}"));
        Console.WriteLine($"{done} files -> {args[2]}");
        return 0;
    }
}
