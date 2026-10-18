using System.Text.RegularExpressions;
using GTFormats.Mdl3;

/// <summary>
/// gtdev mdl3-models &lt;files.tsv&gt; &lt;groupRegex&gt; &lt;out.txt&gt; [dumpPath]
/// Decodes the command stream of every model and checks it: exact size, known opcodes, jump targets on
/// command boundaries, shape indices in range, and how many shapes no model ever draws.
/// With dumpPath, writes the full disassembly of that one file instead.
/// </summary>
static class Mdl3Models
{
    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.WriteLine("mdl3-models <files.tsv> <groupRegex> <out.txt> [dumpPath]"); return 1; }
        using var w = new StreamWriter(args[2]);
        if (args.Length > 3) { Dump(w, args[3]); return 0; }

        var rx = new Regex(args[1]);
        var files = File.ReadAllLines(args[0]).Select(l => l.Split('\t')).Where(p => rx.IsMatch(p[1])).ToList();

        foreach (var ver in files.GroupBy(p => p[2]).OrderBy(g => int.Parse(g.Key)))
        {
            int ok = 0, models = 0, shapesTotal = 0, shapesUnused = 0, badShape = 0, badJump = 0, filesWithUnused = 0;
            var errors = new Dictionary<string, int>();
            var opcodes = new SortedDictionary<int, int>();
            var unusedExamples = new List<string>();

            foreach (var p in ver)
            {
                Mdl3File f;
                try { f = Mdl3File.Load(p[0]); }
                catch (Exception e)
                {
                    errors[e.Message] = errors.GetValueOrDefault(e.Message) + 1;
                    if (errors[e.Message] == 1) w.WriteLine($"ERROR {p[0]}: {e.Message}");
                    continue;
                }
                ok++;
                models += f.Models.Count;
                shapesTotal += f.Shapes.Count;
                var used = new HashSet<int>();
                foreach (var m in f.Models)
                {
                    var starts = m.Commands.Select(c => c.Offset).ToHashSet();
                    starts.Add((int)m.CommandsSize);
                    foreach (var c in m.Commands)
                    {
                        opcodes[c.Opcode] = opcodes.GetValueOrDefault(c.Opcode) + 1;
                        foreach (var s in c.Shapes) { if (s >= f.Shapes.Count) badShape++; used.Add(s); }
                        foreach (var j in c.Jumps) if (!starts.Contains(j)) badJump++;
                    }
                }
                int unused = f.Shapes.Count - used.Count(s => s < f.Shapes.Count);
                shapesUnused += unused;
                if (unused > 0)
                {
                    filesWithUnused++;
                    if (unusedExamples.Count < 5) unusedExamples.Add($"{p[0]}: {unused}/{f.Shapes.Count} not drawn");
                }
            }

            w.WriteLine($"================ version {ver.Key}: {ver.Count()} files, decoded {ok}");
            foreach (var e in errors) w.WriteLine($"  error x{e.Value}: {e.Key}");
            w.WriteLine($"models {models}, shapes {shapesTotal}");
            w.WriteLine($"shape index out of range: {badShape}; jumps not on a command boundary: {badJump}");
            w.WriteLine($"shapes no model draws: {shapesUnused} (in {filesWithUnused} files)");
            foreach (var e in unusedExamples) w.WriteLine($"  {e}");
            w.WriteLine("opcodes: " + string.Join(" ", opcodes.Select(o => $"{o.Key}:{o.Value}")));
            w.WriteLine();
        }
        Console.WriteLine($"{files.Count} files -> {args[2]}");
        return 0;
    }

    static void Dump(StreamWriter w, string path)
    {
        var f = Mdl3File.Load(path);
        w.WriteLine($"{path}: {f.Models.Count} models, {f.Shapes.Count} shapes");
        for (int i = 0; i < f.Models.Count; i++)
        {
            var m = f.Models[i];
            w.WriteLine($"== model {i} {f.ModelNames.GetValueOrDefault(i)}  r={m.Radius:0.###} c={m.Center}  flags {m.Flags:X4} unk2C {m.Unk2C:X4} vm {m.VmInit}/{m.VmUpdate}/{m.VmUnk}");
            foreach (var c in m.Commands)
            {
                w.WriteLine("   " + c + (c.Opcode == 8 ? "  " + Convert.ToHexString(f.Data.Data, (int)m.CommandsOffset + c.Offset, c.Length) : ""));
                foreach (var s in c.Shapes)
                {
                    var sh = s < f.Shapes.Count ? f.Shapes[s] : null;
                    string name = f.ShapeNames.TryGetValue(s, out var n) ? n[0] : "?";
                    if (sh != null)
                        w.WriteLine($"        shape {s}: flags {sh.Flags:X4} fvf {sh.FvfIndex} mat {sh.MaterialIndex} verts {sh.VertexCount} tris {sh.TriCount} packed {sh.PackedRefOffset:X}  {name}");
                }
            }
        }
    }
}
