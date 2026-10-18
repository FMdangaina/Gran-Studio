using System.Numerics;
using System.Text.RegularExpressions;
using GTFormats.Mdl3;

/// <summary>
/// mdl3-preview &lt;file&gt; &lt;out.png&gt; [x|y|z] [shapeRegex] [--lod N] [--all-models] [--size N]: quick shaded view of the
/// decoded shapes (the same selection as mdl3-obj), looking along the axis, z-buffered, flat shading. For visual
/// checks without a 3D tool. The regex filters on the readable shape name.
/// </summary>
static class Mdl3Preview
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("mdl3-preview <file> <out.png> [x|y|z] [shapeRegex] [--lod N] [--all-models] [--size N]"); return 1; }
        var rest = args.Skip(2).ToList();
        int lod = Opt(rest, "--lod", 0), size = Opt(rest, "--size", 512);
        bool all = rest.Remove("--all-models");
        string axis = rest.FirstOrDefault(a => a is "x" or "y" or "z") ?? "x";
        rest.Remove(axis);
        var filter = rest.Count > 0 ? new Regex(rest[0]) : null;

        var m = Mdl3File.Load(args[0]);
        var tris = new List<(Vector3 a, Vector3 b, Vector3 c)>();
        foreach (int s in Mdl3Obj.SelectShapes(m, all))
        {
            if (filter != null && !filter.IsMatch(ShapeNaming.Short(m, s))) continue;
            Mdl3Mesh? mesh;
            try { mesh = Mdl3Mesh.Decode(m, s, lod, out _); } catch { continue; }
            if (mesh == null) continue;
            for (int i = 0; i + 2 < mesh.Indices.Length; i += 3)
            {
                int a = mesh.Indices[i], b = mesh.Indices[i + 1], c = mesh.Indices[i + 2];
                if ((uint)a < mesh.Positions.Length && (uint)b < mesh.Positions.Length && (uint)c < mesh.Positions.Length)
                    tris.Add((View(mesh.Positions[a], axis), View(mesh.Positions[b], axis), View(mesh.Positions[c], axis)));
            }
        }
        if (tris.Count == 0) { Console.Error.WriteLine("no triangles"); return 1; }

        var lo = new Vector2(float.MaxValue); var hi = new Vector2(float.MinValue);
        foreach (var t in tris) foreach (var p in new[] { t.a, t.b, t.c }) { lo = Vector2.Min(lo, new(p.X, p.Y)); hi = Vector2.Max(hi, new(p.X, p.Y)); }
        float scale = (size - 20) / MathF.Max(hi.X - lo.X, hi.Y - lo.Y);
        var zbuf = new float[size * size];
        Array.Fill(zbuf, float.MinValue);
        var rgba = new byte[size * size * 4];
        Array.Fill(rgba, (byte)255);
        var light = Vector3.Normalize(new Vector3(0.3f, 0.4f, 1f));
        foreach (var (a, b, c) in tris)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (n.LengthSquared() == 0) continue;
            byte shade = (byte)(MathF.Abs(Vector3.Dot(Vector3.Normalize(n), light)) * 200 + 40);
            Vector2 P(Vector3 v) => new((v.X - lo.X) * scale + 10, size - 1 - ((v.Y - lo.Y) * scale + 10));
            Vector2 p0 = P(a), p1 = P(b), p2 = P(c);
            float d = (p1.Y - p2.Y) * (p0.X - p2.X) + (p2.X - p1.X) * (p0.Y - p2.Y);
            if (MathF.Abs(d) < 1e-9f) continue;
            int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.X, MathF.Min(p1.X, p2.X)))), x1 = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(p0.X, MathF.Max(p1.X, p2.X))));
            int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y)))), y1 = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y))));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float l1 = ((p1.Y - p2.Y) * (x - p2.X) + (p2.X - p1.X) * (y - p2.Y)) / d;
                    float l2 = ((p2.Y - p0.Y) * (x - p2.X) + (p0.X - p2.X) * (y - p2.Y)) / d;
                    float l3 = 1 - l1 - l2;
                    if (l1 < 0 || l2 < 0 || l3 < 0) continue;
                    float z = l1 * a.Z + l2 * b.Z + l3 * c.Z;
                    int k = y * size + x;
                    if (z <= zbuf[k]) continue;
                    zbuf[k] = z;
                    rgba[k * 4] = rgba[k * 4 + 1] = rgba[k * 4 + 2] = shade;
                }
        }
        Png.Write(args[1], rgba, size, size);
        Console.WriteLine($"{tris.Count} triangles -> {args[1]}");
        return 0;
    }

    /// <summary>Screen x, y and depth (towards the viewer) when looking along the axis.</summary>
    static Vector3 View(Vector3 p, string axis) => axis switch
    {
        "x" => new(p.Z, p.Y, p.X),
        "y" => new(p.X, p.Z, p.Y),
        _ => new(p.X, p.Y, p.Z),
    };

    static int Opt(List<string> a, string name, int def)
    {
        int i = a.IndexOf(name);
        if (i < 0 || i + 1 >= a.Count) return def;
        int v = int.Parse(a[i + 1]);
        a.RemoveRange(i, 2);
        return v;
    }
}
