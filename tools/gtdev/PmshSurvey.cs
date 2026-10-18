using System.Text.RegularExpressions;
using GTFormats.Mdl3;

/// <summary>
/// pmsh-survey &lt;files.tsv&gt; &lt;groupRegex&gt; &lt;out.txt&gt;: counts over every PMSH: packed / raw element sets of the
/// declarations, layouts per array vs packed elements, bits per component, mesh data count / k, data flags,
/// and whether the data is in the file.
/// </summary>
static class PmshSurvey
{
    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("pmsh-survey <files.tsv> <groupRegex> <out.txt>"); return 1; }
        var rx = new Regex(args[1]);
        var files = File.ReadLines(args[0]).Select(l => l.Split('\t')).Where(c => rx.IsMatch(c[1])).ToList();
        var stats = new Dictionary<string, int>();
        void S(string k) => stats[k] = stats.GetValueOrDefault(k) + 1;
        int done = 0;
        foreach (var c in files)
        {
            Mdl3File m;
            try { m = Mdl3File.Load(c[0]); } catch { continue; }
            var p = Mdl3Pmsh.Read(m.Data);
            if (p == null) { S($"{c[1]}: no PMSH"); continue; }
            foreach (var me in p.Meshes)
            {
                if (me.Declaration < 0 || me.LayoutArray < 0) { S($"{c[1]}: mesh decl {me.Declaration} layouts {me.LayoutArray} data {me.DataCount}"); continue; }
                var d = p.Declarations[me.Declaration];
                var l = p.Layouts[me.LayoutArray];
                S($"{c[1]}: packed [{string.Join(' ', d.Packed.Select(e => $"{e.Name}:{e.Type}x{e.Count}"))}]");
                S($"{c[1]}: raw [{string.Join(' ', d.Raw.Select(e => $"{e.Name}:{e.Type}x{e.Count}"))}]");
                S($"{c[1]}: layouts {l.Count} vs packed elements {d.Packed.Count} (colorSet1 packed {d.Packed.Any(e => e.Name == "colorSet1")})");
                foreach (var (e, i) in d.Packed.Select((e, i) => (e, i)))
                    if (i < l.Count) S($"{c[1]}: packed {e.Name} bits {string.Join(',', l[i].Bits)} +0C {l[i].Unk0C} +0E {l[i].Unk0E}");
                S($"{c[1]}: mesh data count {me.DataCount} k {me.K} data in file {me.Data.Count > 0 && me.Data[0].PackedVertices != 0}");
                foreach (var dd in me.Data) S($"{c[1]}: data flags {dd.Flags:X} idxBits {dd.IndexBits} out>V {dd.OutputVertexCount > dd.VertexCount}");
            }
            if (++done % 500 == 0) Console.WriteLine($"{done}/{files.Count}");
        }
        File.WriteAllLines(args[2], stats.OrderBy(x => x.Key).Select(x => $"{x.Value,8}  {x.Key}"));
        Console.WriteLine($"{done} files -> {args[2]}");
        return 0;
    }
}
