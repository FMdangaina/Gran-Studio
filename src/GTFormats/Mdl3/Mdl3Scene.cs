using System.Text.RegularExpressions;

namespace GTFormats.Mdl3;

/// <summary>
/// A switch of the model command streams: all op5 (or all op54) with the same register, over every model.
/// op5 = state switches (day/night, lights on/off...), op54 = GT6 custom parts (one per `body_s` custom_* group, 5b).
/// Branch k is drawn when the register is k.
/// </summary>
public sealed class Mdl3Selector
{
    public int Id;
    public byte Opcode;
    public int Register;
    public int Branches;                 // most branches of any of its commands
    public string Name = "";             // readable: the custom_* group, or the Maya nodes that tell the branches apart
    public string[] Options = [];        // one label per branch
    public int Default;                  // branch shown at load
}

/// <summary>
/// One place where a model draws a shape, with the switch branches it sits in (all must hold) and the LOD branches of
/// the op8 LOD selects around it (Count = levels of that op8).
/// </summary>
public sealed record Mdl3Draw(int Model, int Command, (int Selector, int[] Branches)[] Conditions, (int Count, int[] Branches)[] Lods)
{
    /// <summary>
    /// Drawn at a LOD level: the outer op8 takes branch min(level, its last); a nested op8 counts the level from the
    /// start of the outer branch (whole-car LEVEL_1 > part LEVEL_0 = car LOD1). Outer first in <see cref="Lods"/>.
    /// </summary>
    public bool AtLevel(int level)
    {
        foreach (var q in Lods)
        {
            int b = Math.Min(Math.Max(level, 0), q.Count - 1);
            if (!q.Branches.Contains(b)) return false;
            level -= b;
        }
        return true;
    }
}

/// <summary>Tree node: file, model, Maya group or shape (as drawn by one model).</summary>
public sealed class Mdl3SceneNode
{
    public string Name = "";
    public string Kind = "";             // file, model, group, shape
    public int Model = -1, Shape = -1;
    public Mdl3SceneNode? Parent;
    public List<Mdl3SceneNode> Children = [];
    public IEnumerable<Mdl3SceneNode> All() => Children.SelectMany(c => c.All()).Prepend(this);
}

/// <summary>
/// The structure of an MDL3 for the viewer: the node tree (file -> model -> meaningful Maya groups -> shapes), the
/// switches of the command streams with the branches every shape is drawn in, and the LOD level of each shape
/// (Maya `LEVEL_n` nodes; GT5 packed shapes have their LOD levels inside, see <see cref="Mdl3Mesh.LodCount"/>).
/// </summary>
public sealed partial class Mdl3Scene
{
    public Mdl3File File { get; }
    public Mdl3SceneNode Root { get; } = new() { Kind = "file" };
    public List<Mdl3Selector> Selectors { get; } = [];
    public Dictionary<int, List<Mdl3Draw>> Draws { get; } = [];
    public Dictionary<int, int> ShapeLevel { get; } = [];   // shapes only drawn inside op8 LOD selects: first level drawn
    public Dictionary<int, int> MayaLevel { get; } = [];    // shapes under a Maya LEVEL_n node (per-part numbering)
    public int Levels { get; }                              // most op8 LOD branches (0 = no op8)
    public int LodSelects { get; private set; }             // op8 count
    public List<string> Notes { get; } = [];                // structure oddities found while building

    [GeneratedRegex(@"^LEVEL_?(\d+)$")]
    private static partial Regex LevelNode();

    /// <summary>Models that are not the visible car: shadow volumes/planes, self-shadow copies, light masks, projections.</summary>
    public static bool IsHelperModel(string name) =>
        name.Contains("SHADOW") || name.Contains("_SS_") || name.Contains("LIGHT_MASK") || name.Contains("ILLUMINATION") || name.Contains("PROJECTION");

    /// <summary>Models shown when a file is opened: not the helpers, not the GT6 menu cabin and occlusion copies.</summary>
    public static bool DefaultOn(string model) => !IsHelperModel(model) && !model.Contains("MENU_INTERIOR") && !model.Contains("OCCLUSION");

    /// <summary>
    /// Model shown when the file is opened: <see cref="DefaultOn(string)"/>, not an instance copy (FIX1, FIX2...: the
    /// same part again, e.g. the lower wheel levels), and its shapes not under a helper root node (WHEEL_SHADOW_...).
    /// </summary>
    public bool DefaultOn(int model)
    {
        string name = File.ModelNames.GetValueOrDefault(model, "");
        if (!DefaultOn(name) || Regex.IsMatch(name, @"^FIX\d+$")) return false;
        var roots = Draws.Where(d => d.Value.Any(x => x.Model == model))
            .Select(d => File.ShapeNames.TryGetValue(d.Key, out var n) ? n[0].Split('|', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "" : "").ToList();
        return roots.Count == 0 || roots.Any(r => !IsHelperModel(r));
    }

    public Mdl3Scene(Mdl3File m, string rootName)
    {
        File = m;
        Root.Name = rootName;
        var selectorOf = new Dictionary<(byte, int), Mdl3Selector>();
        for (int mi = 0; mi < m.Models.Count; mi++)
        {
            var model = m.Models[mi];
            var cmds = model.Commands;
            var at = cmds.Select((c, k) => (c.Offset, k)).ToDictionary(x => x.Offset, x => x.k);
            // branch ranges of every switch command: [target, next target) and the last one up to the default jump target
            var ranges = new List<(Mdl3Selector sel, int branch, int from, int to)>();
            var lodRanges = new List<(int op8, int count, int branch, int from, int to)>();
            for (int ci = 0; ci < cmds.Count; ci++)
            {
                var c = cmds[ci];
                if (c.Opcode is not (5 or 54 or 8) || c.Jumps.Length == 0) continue;
                Mdl3Selector? sel = null;
                if (c.Opcode != 8)
                {
                    int reg = m.Data.U16((int)model.CommandsOffset + c.Offset + 1);
                    if (!selectorOf.TryGetValue((c.Opcode, reg), out sel))
                    {
                        sel = new Mdl3Selector { Id = Selectors.Count, Opcode = c.Opcode, Register = reg };
                        Selectors.Add(sel);
                        selectorOf[(c.Opcode, reg)] = sel;
                    }
                    sel.Branches = Math.Max(sel.Branches, c.Jumps.Length);
                }
                else LodSelects++;
                int d = ci + 1;
                while (d < cmds.Count - 1 && cmds[d].Opcode == 15) d++;   // some branches pop before their jump
                int end;
                if (cmds[d].Opcode is 9 or 10) end = cmds[d].Jumps[0];
                else
                {
                    // default branch with commands: the branches end where the jump closing the first branch goes
                    Notes.Add($"model {mi} op{c.Opcode} @{c.Offset:X}: default branch is not a jump (op{cmds[d].Opcode})");
                    int first = c.Jumps.Min();
                    var close = cmds.Take(at[first]).LastOrDefault(x => x.Opcode is 9 or 10 && x.Offset > c.Offset);
                    end = close?.Jumps[0] ?? model.Commands[^1].Offset + 1;
                }
                var starts = c.Jumps.Distinct().Order().ToList();
                for (int b = 0; b < c.Jumps.Length; b++)
                {
                    int to = starts.FirstOrDefault(x => x > c.Jumps[b], end);
                    if (sel != null) ranges.Add((sel, b, c.Jumps[b], to));
                    else lodRanges.Add((ci, c.Jumps.Length, b, c.Jumps[b], to));
                }
            }
            foreach (var c in cmds.Select((c, k) => (c, k)).Where(x => x.c.Shapes.Length > 0))
            {
                var cond = ranges.Where(r => c.c.Offset >= r.from && c.c.Offset < r.to)
                    .GroupBy(r => r.sel.Id).Select(g => (g.Key, g.Select(r => r.branch).Distinct().Order().ToArray())).ToArray();
                var lods = lodRanges.Where(r => c.c.Offset >= r.from && c.c.Offset < r.to)
                    .GroupBy(r => r.op8).Select(g => (g.First().count, g.Select(r => r.branch).Distinct().Order().ToArray())).ToArray();
                foreach (int s in c.c.Shapes)
                {
                    if (!Draws.TryGetValue(s, out var l)) Draws[s] = l = [];
                    l.Add(new Mdl3Draw(mi, c.k, cond, lods));
                }
            }
        }

        // LOD levels: the op8 LOD selects of the command streams (GT6), the most branches of any op8
        Levels = Draws.Values.SelectMany(l => l).SelectMany(d => d.Lods).Select(q => q.Count).DefaultIfEmpty(0).Max();
        // nested op8 (whole-car levels around part levels, 56 GT6 bodies): a branch pair no single level reaches is noted
        foreach (var (s, l) in Draws)
            if (l.All(d => d.Lods.Length > 0))
            {
                int k = Enumerable.Range(0, Levels).FirstOrDefault(k => l.Any(d => d.AtLevel(k)), -1);
                if (k < 0) Notes.Add($"shape {s}: nested op8 branches {string.Join(" / ", l.Select(d => string.Join("+", d.Lods.Select(q => $"{string.Join(",", q.Branches)} of {q.Count}"))))} drawn at no LOD level");
                ShapeLevel[s] = Math.Max(k, 0);
            }
        // Maya LEVEL_n nodes: names only (numbered per part, not one level for the whole car)
        foreach (var (s, names) in m.ShapeNames)
            foreach (var node in names[0].Split('|'))
                if (LevelNode().Match(node) is { Success: true } lm) { MayaLevel[s] = int.Parse(lm.Groups[1].Value); break; }

        NameSelectors(m);
        BuildTree(m);
    }

    void NameSelectors(Mdl3File m)
    {
        var bodyS = m.BodyS;
        foreach (var sel in Selectors)
        {
            var perBranch = Enumerable.Range(0, sel.Branches).Select(b => Draws.Where(d => d.Value.Any(x => x.Conditions.Any(q => q.Selector == sel.Id && q.Branches.Contains(b))))
                .Select(d => d.Key).ToHashSet()).ToList();
            if (sel.Opcode == 54)
            {
                // custom part: the body_s group holding the branch shapes (custom_c0[3] -> custom_c0)
                var group = perBranch.SelectMany(x => x).Select(s => bodyS?.Shapes.TryGetValue(s, out var sd) == true ? sd.Where : null)
                    .Where(w => w != null && w.StartsWith("custom_")).Select(w => w![..w.IndexOf('[')]).GroupBy(x => x).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
                // no body_s (interiors): the CUSTOM_<part> Maya node of the branch shapes
                group ??= perBranch.SelectMany(x => x).SelectMany(s => m.ShapeNames.TryGetValue(s, out var n) ? n[0].Split('|') : [])
                    .Select(node => Regex.Match(node, @"CUSTOM_(BRAKE|CALIPER|[A-F])(?![A-Za-z])")).Where(x => x.Success)
                    .Select(x => "custom_" + x.Groups[1].Value.ToLowerInvariant()).GroupBy(x => x).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
                string part = group == null ? "custom part" : Regex.Match(group, @"^custom_(\w+?)\d*$").Groups[1].Value switch
                {
                    "a" => "bonnet", "b" => "muffler", "c" => "rear wing", "d" => "front bumper / spoiler", "e" => "flat floor",
                    "f" => "roof cover", "brake" => "brake discs", "caliper" => "brake calipers", var x => x,
                };
                sel.Name = group == null ? $"custom register {sel.Register}" : $"{group} ({part})";
                sel.Options = [.. Enumerable.Range(0, sel.Branches).Select(b => perBranch[b].Count == 0 ? $"option {b} (nothing)" : $"option {b}")];
                continue;
            }
            // state switch: per branch the Maya node words that only its shapes have (DAYTIME / NIGHT, *_ON / *_OFF...)
            var words = perBranch.Select(set => set.SelectMany(s => m.ShapeNames.TryGetValue(s, out var n) ? n[0].Split('|') : [])
                .Select(ShapeNaming.Clean).Where(w => w.Length > 0).ToHashSet()).ToList();
            sel.Options = new string[sel.Branches];
            for (int b = 0; b < sel.Branches; b++)
            {
                var own = words[b].Where(w => !words.Where((_, k) => k != b).Any(o => o.Contains(w))).ToList();
                string label = own.FirstOrDefault(w => Regex.IsMatch(w, "DAYTIME|NIGHT|_ON$|_OFF$|ON$|OFF$|OPEN|CLOSE", RegexOptions.IgnoreCase))
                    ?? own.OrderBy(w => w.Length).FirstOrDefault() ?? "";
                sel.Options[b] = perBranch[b].Count == 0 ? $"{b}: (nothing)" : label.Length > 0 ? $"{b}: {label}" : $"{b}";
            }
            sel.Name = $"switch {sel.Register} ({string.Join(" / ", sel.Options.Select(o => o.Contains(": ") ? o[(o.IndexOf(": ") + 2)..] : o))})";
            // day and closed/off states first when they exist, else branch 0
            int day = Array.FindIndex(sel.Options, o => o.Contains("DAYTIME", StringComparison.OrdinalIgnoreCase));
            sel.Default = day >= 0 ? day : 0;
        }
    }

    void BuildTree(Mdl3File m)
    {
        var models = new Dictionary<int, Mdl3SceneNode>();
        for (int mi = 0; mi < m.Models.Count; mi++)
        {
            var node = new Mdl3SceneNode { Name = m.ModelNames.GetValueOrDefault(mi, $"model {mi}"), Kind = "model", Model = mi, Parent = Root };
            Root.Children.Add(node);
            models[mi] = node;
        }
        var groups = new Dictionary<(int, string), Mdl3SceneNode>();
        foreach (var (s, draws) in Draws.OrderBy(x => x.Key))
            foreach (int mi in draws.Select(d => d.Model).Distinct())
            {
                var parent = models[mi];
                List<string> nodes;
                if (m.ShapeNames.TryGetValue(s, out var n))
                {
                    var raw = n[0].Split('|', StringSplitOptions.RemoveEmptyEntries);
                    nodes = ShapeNaming.Nodes(string.Join('|', raw.Take(Math.Max(1, raw.Length - 2))));
                }
                else nodes = ["(no Maya name)", "material " + (ShapeNaming.MaterialWord(m, s, true) ?? "none")];   // grouped by material
                // the first node usually repeats the model (GTBE_BODY -> BODY): skip it then
                if (nodes.Count > 0 && ShapeNaming.Clean(parent.Name).Equals(nodes[0], StringComparison.OrdinalIgnoreCase)) nodes.RemoveAt(0);
                string key = "";
                foreach (var g in nodes)
                {
                    key += "|" + g;
                    if (!groups.TryGetValue((mi, key), out var gn))
                    {
                        gn = new Mdl3SceneNode { Name = g, Kind = "group", Model = mi, Parent = parent };
                        parent.Children.Add(gn);
                        groups[(mi, key)] = gn;
                    }
                    parent = gn;
                }
                parent.Children.Add(new Mdl3SceneNode { Name = ShapeNaming.Short(m, s), Kind = "shape", Model = mi, Shape = s, Parent = parent });
            }
        // shapes no model draws (some GT5 wheels): kept, under their own node
        var orphans = Enumerable.Range(0, m.Shapes.Count).Where(s => !Draws.ContainsKey(s)).ToList();
        if (orphans.Count > 0)
        {
            var node = new Mdl3SceneNode { Name = "(not drawn by any model)", Kind = "model", Parent = Root };
            Root.Children.Add(node);
            foreach (int s in orphans)
                node.Children.Add(new Mdl3SceneNode { Name = ShapeNaming.Short(m, s), Kind = "shape", Shape = s, Parent = node });
        }
    }

    /// <summary>Is the shape drawn by this model in the given switch state (selector id -> branch)?</summary>
    public bool IsDrawn(int model, int shape, IReadOnlyDictionary<int, int> state, int level = 0) =>
        Draws.TryGetValue(shape, out var l) && l.Any(d => d.Model == model && d.AtLevel(level) &&
            d.Conditions.All(q => q.Branches.Contains(state.TryGetValue(q.Selector, out int b) ? b : Selectors[q.Selector].Default)));

    /// <summary>Is the shape drawn at this LOD level by any model (shapes outside op8 LOD selects: at every level)?</summary>
    public bool AtLevel(int shape, int level) => !Draws.TryGetValue(shape, out var l) || l.Any(d => d.AtLevel(level));
}
