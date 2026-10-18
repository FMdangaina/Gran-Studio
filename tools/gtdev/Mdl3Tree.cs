using System.Globalization;
using GTFormats.Mdl3;

/// <summary>
/// mdl3-tree &lt;file&gt; [out.txt]: bones (name, parent, translation) and every model's command stream with operands,
/// push/pop depth and the shape names. Research dump for the model hierarchy and the part transforms.
/// </summary>
static class Mdl3Tree
{
    public static int Run(string[] args)
    {
        if (args.Length < 1) { Console.Error.WriteLine("mdl3-tree <file> [out.txt]"); return 1; }
        var m = Mdl3File.Load(args[0]);
        var r = m.Data;
        var ci = CultureInfo.InvariantCulture;
        using var w = args.Length > 1 ? new StreamWriter(args[1]) : new StreamWriter(Console.OpenStandardOutput());
        w.WriteLine($"{args[0]}: {m.Models.Count} models, {m.Shapes.Count} shapes, {m.Bones.Count} bones");
        for (int i = 0; i < m.Bones.Count; i++)
        {
            var b = m.Bones[i];
            var t = b.Matrix;
            w.WriteLine(string.Create(ci, $"  bone {i,3} id {b.Id,3} parent {b.Parent,3} {b.Name,-28} row0 {t.M11:0.###} {t.M12:0.###} {t.M13:0.###} {t.M14:0.###}  t {t.M41:0.####} {t.M42:0.####} {t.M43:0.####} {t.M44:0.###}"));
        }
        foreach (var (name, v) in m.Keys(0x1E, 0x58).OrderBy(k => k.index & 0xFFFF))
            w.WriteLine($"  host register {v & 0xFFFF,3} (len {v >> 16}) {name}");
        foreach (var (name, v) in m.Keys(0x22, 0x5C).OrderBy(k => k.index & 0xFFFF))
            w.WriteLine($"  vm output {v & 0xFFFF,3} ({v >> 16:X}) {name}");
        for (int mi = 0; mi < m.Models.Count; mi++)
        {
            var model = m.Models[mi];
            w.WriteLine($"== model {mi} {m.ModelNames.GetValueOrDefault(mi, "?")}");
            int depth = 0;
            foreach (var c in model.Commands)
            {
                if (c.Opcode == 15) depth--;
                int at = (int)model.CommandsOffset + c.Offset;
                string ops = string.Join(' ', Enumerable.Range(1, c.Length - 1).Select(k => r.U8(at + k).ToString("X2")));
                string extra = c.Opcode switch
                {
                    6 => r.U8(at + 1) < m.Bones.Count ? $"  bone? {m.Bones[r.U8(at + 1)].Name}" : "",
                    _ => "",
                };
                string shapes = string.Join(" ; ", c.Shapes.Select(s => $"s{s} {(m.ShapeNames.TryGetValue(s, out var n) ? n[0].Split('|')[^2] : "")}"));
                w.WriteLine($"  {c.Offset:X4} {new string(' ', depth * 2)}op{c.Opcode} {ops}{extra}  {shapes}");
                if (c.Opcode == 14) depth++;
            }
        }
        return 0;
    }
}
