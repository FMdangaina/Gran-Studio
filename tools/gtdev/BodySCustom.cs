using System.Text.RegularExpressions;
using GTFormats.Mdl3;

/// <summary>
/// bodys-custom &lt;files.tsv&gt; &lt;groupRegex&gt; &lt;out.txt&gt; | &lt;body&gt;: item 5b, the `custom_*` groups of the GT6 body_s map
/// against the v14 switch command op54 (u16 value, u8 n, n x u16 rel). Branch i of an op54 = the commands from its
/// target to the next branch target (the last one up to the default jump target). For every op54 the group whose items
/// hold the shapes of the branches (item i shapes inside branch i) is searched; value -> group is counted.
/// </summary>
static class BodySCustom
{
    public static int Run(string[] args)
    {
        if (args.Length == 1) { Survey([args[0]], null, true); return 0; }
        if (args.Length < 3) { Console.Error.WriteLine("bodys-custom <files.tsv> <groupRegex> <out.txt> | <body>"); return 1; }
        var rx = new Regex(args[1]);
        var files = File.ReadLines(args[0]).Select(l => l.Split('\t')).Where(c => rx.IsMatch(c[1])).Select(c => c[0]).ToList();
        Survey(files, args[2], false);
        return 0;
    }

    static void Survey(List<string> files, string? outPath, bool verbose)
    {
        var c = new SortedDictionary<string, int>();
        var ex = new Dictionary<string, string>();
        void Count(string k, string? e = null) { c[k] = c.GetValueOrDefault(k) + 1; if (e != null && !ex.ContainsKey(k)) ex[k] = e; }
        int done = 0;
        foreach (var path in files)
        {
            string car = Regex.Replace(path.Replace('\\', '/'), @"^.*/car/", "");
            try
            {
                var m = Mdl3File.Load(path);
                var map = Mdl3StreamMap.Read(m.Data);
                if (map == null) { Count("no stream map"); continue; }
                var bodyS = File.ReadAllBytes(path + "_s");
                // shapes of every item (sub streams included) per group
                var items = map.Groups.ToDictionary(g => g.Name, g => g.Items.Select(it =>
                    new[] { it.Stream }.Concat(it.Subs).SelectMany(s => s.HeaderChunks.Count == 0 ? [] : new Mdl3StreamBundle(s.Load(bodyS)).Shapes()).ToHashSet()).ToList());
                foreach (var g in map.Groups.Where(g => g.Name != "/"))
                {
                    Count($"group {Regex.Replace(g.Name, @"\d+$", "#")}", car);
                    for (int i = 0; i < g.Items.Count; i++)
                    {
                        if (items[g.Name][i].Count == 0) Count($"custom item without shapes (item {(i == 0 ? "0" : ">0")})", $"{car} {g.Name}[{i}]");
                        if (g.Items[i].Subs.Count > 0) Count($"custom item with sub streams: {g.Items[i].Subs.Count}", $"{car} {g.Name}[{i}]");
                        if (g.Items[i].Subs.Count > 0)
                        {
                            int rootSubs = map.Groups.FirstOrDefault(x => x.Name == "/")?.Items[0].Subs.Count ?? 0;
                            Count(g.Items[i].Subs.Count == rootSubs ? "custom item subs = '/' subs (same colour variations)" : "custom item subs != '/' subs", $"{car} {g.Name}[{i}] {g.Items[i].Subs.Count} vs {rootSubs}");
                            var subShapes = g.Items[i].Subs.Sum(s => s.HeaderChunks.Count == 0 ? 0 : new Mdl3StreamBundle(s.Load(bodyS)).ShapeCount);
                            Count(subShapes == 0 ? "custom item sub streams: images only" : "custom item sub streams hold shapes", $"{car} {g.Name}[{i}]");
                        }
                    }
                }
                var matched = new HashSet<string>();
                var union = new Dictionary<int, List<HashSet<int>>>();   // op54 value -> branch i -> shapes, over all models
                for (int mi = 0; mi < m.Models.Count; mi++)
                {
                    var cmds = m.Models[mi].Commands;
                    var byOffset = cmds.Select((x, k) => (x.Offset, k)).ToDictionary(x => x.Offset, x => x.k);
                    for (int ci = 0; ci < cmds.Count; ci++)
                    {
                        if (cmds[ci].Opcode != 54) continue;
                        var op = cmds[ci];
                        int at = (int)m.Models[mi].CommandsOffset + op.Offset;
                        int value = m.Data.U16(at + 1), n = op.Jumps.Length;
                        // default = the command right after the op54 (op9 to the end)
                        var after = cmds[ci + 1];
                        int end = after.Opcode is 9 or 10 ? after.Jumps[0] : -1;
                        if (end < 0) { Count("op54 not followed by a jump", $"{car} model {mi}"); continue; }
                        var starts = op.Jumps.Distinct().Order().ToList();
                        var branch = new List<HashSet<int>>();
                        foreach (int t in op.Jumps)
                        {
                            int stop = starts.FirstOrDefault(x => x > t, end);
                            var set = new HashSet<int>();
                            for (int k = byOffset[t]; k < cmds.Count && cmds[k].Offset < stop; k++) set.UnionWith(cmds[k].Shapes);
                            branch.Add(set);
                        }
                        if (!union.TryGetValue(value, out var u)) union[value] = u = [];
                        for (int i = 0; i < n; i++) { if (u.Count <= i) u.Add([]); u[i].UnionWith(branch[i]); }
                        string model = m.ModelNames.GetValueOrDefault(mi, "?");
                        Count($"op54 in model {Regex.Replace(model, @"\d+", "#")}");
                        Count((value & 0xFFE0) == 0x20 ? "op54 value = 0x20 + small number" : $"op54 value 0x{value:X}", $"{car} model {mi}");
                        if (verbose)
                            Console.WriteLine($"model {mi} {model} @{op.Offset:X4} op54 value {value} (0x{value:X}) n {n}\n" +
                                string.Join("\n", branch.Select((b, i) => $"    branch {i}: {string.Join(" ", b.Order().Select(s => $"s{s}"))}")));
                    }
                }
                // the group of a value = the one whose non-empty items i are all inside branch i (all op54 of the value
                // together), most shapes in common; then value - group index must be one constant per file
                var drawnBySwitch = new HashSet<int>();
                var bases = new HashSet<int>();
                foreach (var (value, u) in union)
                {
                    int best = -1, bestScore = 0;
                    for (int gi = 1; gi < map.Groups.Count; gi++)
                    {
                        var its = items[map.Groups[gi].Name];
                        if (!Enumerable.Range(0, its.Count).All(i => its[i].Count == 0 || i < u.Count && its[i].IsSubsetOf(u[i]))) continue;
                        int score = Enumerable.Range(0, Math.Min(its.Count, u.Count)).Sum(i => its[i].Intersect(u[i]).Count());
                        if (score > bestScore) { best = gi; bestScore = score; }
                    }
                    if (best < 0)
                    {
                        Count(u.All(b => b.All(s => items["/"].Count > 0 && items["/"][0].Contains(s))) ? "op54 value without custom group: draws only '/' shapes"
                            : "op54 value: NO custom group fits", $"{car} value 0x{value:X} n {u.Count}");
                        continue;
                    }
                    bases.Add(value - best);
                    var g = map.Groups[best];
                    matched.Add(g.Name);
                    foreach (var b in u) drawnBySwitch.UnionWith(b);
                    Count(u.Count == g.Items.Count ? "branches = items" : u.Count > g.Items.Count ? "more branches than items" : "fewer branches than items",
                        u.Count == g.Items.Count ? null : $"{car} {g.Name} {u.Count} vs {g.Items.Count}");
                    for (int i = 0; i < Math.Max(u.Count, g.Items.Count); i++)
                    {
                        var bs = i < u.Count ? u[i] : [];
                        var its = i < g.Items.Count ? items[g.Name][i] : [];
                        Count(bs.SetEquals(its) ? "branch i shapes = item i shapes (exact)" : its.IsSubsetOf(bs) ? "item i shapes inside branch i, branch draws more"
                            : bs.IsSubsetOf(its) ? "branch i inside item i, item has more" : "branch i and item i DIFFER", bs.SetEquals(its) ? null : $"{car} {g.Name}[{i}]");
                    }
                }
                // host registers customN against the groups: a..f -> custom0..5, brake -> custom10, caliper -> custom11
                var hostCustom = m.Keys(0x1E, 0x58).Select(k => k.name).Where(n => Regex.IsMatch(n, @"^custom\d+$")).ToHashSet();
                var expected = map.Groups.Where(g => g.Name != "/").Select(g => Regex.Match(g.Name, @"^custom_(\w+?)\d*$").Groups[1].Value switch
                {
                    "brake" => "custom10", "caliper" => "custom11", var x when x.Length == 1 => $"custom{x[0] - 'a'}", var x => "?" + x,
                }).ToHashSet();
                Count(hostCustom.SetEquals(expected) ? "host registers customN = groups (a..f = 0..5, brake 10, caliper 11)"
                    : $"host registers customN differ from the groups", hostCustom.SetEquals(expected) ? null : $"{car} host {string.Join(",", hostCustom.Order())} groups {string.Join(",", expected.Order())}");
                if (bases.Count > 0) Count(bases.Count == 1 ? "value - group index: one constant per file" : "value - group index: SEVERAL constants", $"{car} {string.Join(",", bases)}");
                foreach (var g in map.Groups.Where(g => g.Name != "/"))
                {
                    Count(matched.Contains(g.Name) ? "group selected by op54" : items[g.Name].All(x => x.Count == 0) ? "group not selected by op54, no shapes (images only)" : "group with shapes NOT selected by any op54", $"{car} {g.Name}");
                    // which Maya names the parts carry: leaf node words of the item shapes
                    foreach (var w in items[g.Name].SelectMany(x => x).Select(s => m.ShapeNames.TryGetValue(s, out var sn) ? sn[0] : "")
                        .SelectMany(p => p.Split('|', StringSplitOptions.RemoveEmptyEntries)).Where(p => Regex.IsMatch(p, "CUSTOM|OPTION|custom", RegexOptions.IgnoreCase))
                        .Select(p => Regex.Replace(p, @"\d+", "#")).Distinct())
                        Count($"names {Regex.Replace(g.Name, @"\d+$", "#")}: {w}");
                    // words of the Maya node names under the CUSTOM node (what the part is)
                    if (!g.Name.StartsWith("custom_brake") && !g.Name.StartsWith("custom_caliper"))
                        foreach (var w in items[g.Name].SelectMany(x => x).Select(s => m.ShapeNames.TryGetValue(s, out var sn) ? sn[0] : "")
                            .SelectMany(p => Regex.Split(p, @"[|_:\d]+")).Select(x => x.ToLowerInvariant())
                            .Where(x => x.Length > 2 && !Regex.IsMatch(x, "^(custom|option|shape|lod|level|std|body|gt|fix|transform|poly|surface|polysurface|polytosubd|pcube|pplane|group|mesh|shadow|opt|share|batch|bmfpp|noexpand|porygon)$")).Distinct())
                            Count($"words {Regex.Replace(g.Name, @"\d+$", "#")}: {w}");
                }
                foreach (var g in map.Groups.Where(g => g.Name != "/"))
                    foreach (var s in items[g.Name].SelectMany(x => x).Distinct())
                        if (!drawnBySwitch.Contains(s)) Count("custom item shape NOT drawn by its op54", $"{car} {g.Name} s{s}");
                if (items["/"].Count > 0)
                    foreach (var s in items["/"][0])
                        if (drawnBySwitch.Contains(s)) Count("'/' shape drawn under an op54", $"{car} s{s}");
                if (verbose)
                    foreach (var g in map.Groups)
                        for (int i = 0; i < g.Items.Count; i++)
                            Console.WriteLine($"'{g.Name}'[{i}] shapes {string.Join(" ", items[g.Name][i].Order().Take(40).Select(s => $"s{s} {(m.ShapeNames.TryGetValue(s, out var sn) ? string.Join('.', ShapeNaming.Nodes(sn[0]).Skip(1).Take(3)) : "")}"))}");
            }
            catch (Exception e) { Count("FAIL: exception " + e.GetType().Name, $"{car} {e.Message}"); }
            if (++done % 300 == 0) Console.WriteLine($"{done}/{files.Count}");
        }
        var lines = c.Select(x => $"{x.Value,8}  {x.Key}{(ex.TryGetValue(x.Key, out var e) ? "   e.g. " + e : "")}");
        if (outPath == null) { foreach (var l in lines) Console.WriteLine(l); return; }
        File.WriteAllLines(outPath, [$"{done} files", .. lines]);
        Console.WriteLine($"{done} files -> {outPath}");
    }
}
