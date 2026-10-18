using System.Text.RegularExpressions;
using GTFormats.Mdl3;

/// <summary>
/// mdl3-scene &lt;file&gt; [out.txt]: the viewer's scene (Mdl3Scene): switches with their branch labels, then the tree
/// (models -> Maya groups -> shapes) with the switch conditions and LOD level of every shape.
/// mdl3-scene &lt;files.tsv&gt; &lt;groupRegex&gt; &lt;out.txt&gt;: build the scene of every file and count the oddities
/// (default branches with commands, shapes never drawn, unnamed custom switches, LEVEL_n use).
/// </summary>
static class Mdl3SceneDump
{
    public static int Run(string[] args)
    {
        if (args.Length >= 3 && args[0].EndsWith(".tsv", StringComparison.OrdinalIgnoreCase)) return Survey(args);
        if (args.Length < 1) { Console.Error.WriteLine("mdl3-scene <file> [out.txt] | <files.tsv> <groupRegex> <out.txt>"); return 1; }
        var m = Mdl3File.Load(args[0]);
        var sc = new Mdl3Scene(m, Path.GetFileName(args[0]));
        using var w = args.Length > 1 ? new StreamWriter(args[1]) : new StreamWriter(Console.OpenStandardOutput());
        w.WriteLine($"{args[0]}: {m.Models.Count} models, {m.Shapes.Count} shapes, {sc.Selectors.Count} switches, {sc.LodSelects} op8, LOD levels {sc.Levels}");
        foreach (var s in sc.Selectors)
            w.WriteLine($"  [{s.Id}] op{s.Opcode} reg {s.Register}: {s.Name}  default {s.Default}  options: {string.Join(" | ", s.Options)}");
        foreach (var n in sc.Notes) w.WriteLine("  note: " + n);
        void Print(Mdl3SceneNode n, int depth)
        {
            string extra = "";
            if (n.Kind == "shape")
            {
                var conds = sc.Draws[n.Shape].Where(d => d.Model == n.Model).Select(d => string.Join(" & ",
                        d.Conditions.Select(q => $"[{q.Selector}]={string.Join(",", q.Branches)}").Concat(d.Lods.Select(q => $"LOD {string.Join(",", q.Branches)}/{q.Count}"))))
                    .Distinct().Select(x => x.Length == 0 ? "always" : x);
                extra = $"   ({string.Join(" or ", conds)}){(sc.MayaLevel.TryGetValue(n.Shape, out int l) ? $" LEVEL_{l}" : "")}";
            }
            else if (n.Kind == "model") extra = Mdl3Scene.DefaultOn(n.Name) ? "" : "   (hidden at load)";
            w.WriteLine($"{new string(' ', depth * 2)}{n.Name}{extra}");
            foreach (var c in n.Children) Print(c, depth + 1);
        }
        Print(sc.Root, 0);
        return 0;
    }

    static int Survey(string[] args)
    {
        var rx = new Regex(args[1]);
        var files = File.ReadLines(args[0]).Select(l => l.Split('\t')).Where(c => rx.IsMatch(c[1])).ToList();
        var c = new SortedDictionary<string, int>();
        var ex = new Dictionary<string, string>();
        void Count(string k, string? e = null) { c[k] = c.GetValueOrDefault(k) + 1; if (e != null && !ex.ContainsKey(k)) ex[k] = e; }
        int done = 0;
        foreach (var f in files)
        {
            try
            {
                var m = Mdl3File.Load(f[0]);
                var sc = new Mdl3Scene(m, "");
                Count($"{f[1]}: files");
                foreach (var n in sc.Notes) Count($"{f[1]}: {Regex.Replace(Regex.Replace(n, @"model \d+ op(\d+) @\w+: (.*) \(op\d+\)", "op$1 $2"), @"^shape \d+: nested.*", "shape in nested op8 drawn at no LOD level")}", f[0] + " " + n);
                for (int s = 0; s < m.Shapes.Count; s++) if (!sc.Draws.ContainsKey(s)) Count($"{f[1]}: shape drawn by no model", $"{f[0]} s{s}");
                foreach (var s in sc.Selectors)
                {
                    Count($"{f[1]}: switch op{s.Opcode}");
                    if (s.Opcode == 54 && s.Name.StartsWith("custom register")) Count($"{f[1]}: op54 without a body_s group", f[0]);
                    if (s.Opcode == 5) Count($"{f[1]}: op5 options: {Regex.Replace(string.Join(" / ", s.Options.Select(o => o.Contains(": ") ? o[(o.IndexOf(": ") + 2)..] : "?")), @"\d+", "#")}");
                }
                Count($"{f[1]}: op8 LOD levels {sc.Levels}");
                int packed = 1;
                for (int s = 0; s < m.Shapes.Count; s++) try { packed = Math.Max(packed, Mdl3Mesh.LodCount(m, s)); } catch { }
                Count($"{f[1]}: LOD levels (op8 or packed) {Math.Max(packed, sc.Levels)}");
                foreach (var d in sc.Draws.Values.SelectMany(l => l).Where(d => d.Lods.Length > 0))
                {
                    Count($"{f[1]}: draws inside op8: {d.Lods.Length} op8 deep, {string.Join("+", d.Lods.Select(q => q.Count))} branches");
                    if (d.Lods.Any(q => q.Branches.Length != 1)) Count($"{f[1]}: draws in more than one branch of an op8", f[0]);
                    if (d.Lods.Length > 1) Count($"{f[1]}: nested draw {string.Join(" > ", d.Lods.Select(q => $"{string.Join(",", q.Branches)}/{q.Count}"))}", f[0]);
                }
                var ops = m.Models.SelectMany(x => x.Commands).Where(x => x.Opcode == 8).ToList();
                foreach (var o in ops)
                {
                    Count($"{f[1]}: op8 distances {string.Join(" ", o.LodDistances.Select(x => x.ToString("0.##")))}");
                    if (o.Jumps.Length == 0) Count($"{f[1]}: op8 without branches", f[0]);
                }
                foreach (var (s, ml) in sc.MayaLevel)
                {
                    var inside = sc.Draws.TryGetValue(s, out var dl) ? dl.Where(d => d.Lods.Length > 0).ToList() : [];
                    if (inside.Count == 0) Count($"{f[1]}: LEVEL_n shape outside op8", $"{f[0]} s{s}");
                    else if (inside.Any(d => d.Lods[0].Branches.Contains(ml))) Count($"{f[1]}: LEVEL_n = op8 branch");
                    else Count($"{f[1]}: LEVEL_n != op8 branch", $"{f[0]} s{s} LEVEL_{ml}");
                }
                if (sc.Root.All().Any(n => n.Kind == "shape" && n.Model >= 0 && sc.Draws[n.Shape].Where(d => d.Model == n.Model).All(d => d.Conditions.Length > 0)))
                    Count($"{f[1]}: files with switched shapes");
            }
            catch (Exception e) { Count($"{f[1]}: FAIL {e.GetType().Name}", $"{f[0]} {e.Message}"); }
            if (++done % 500 == 0) Console.WriteLine($"{done}/{files.Count}");
        }
        using var w = new StreamWriter(args[2]);
        foreach (var (k, n) in c) w.WriteLine($"{n,8}  {k}{(ex.TryGetValue(k, out var e) ? "   e.g. " + e : "")}");
        Console.WriteLine($"{done} files -> {args[2]}");
        return 0;
    }
}
