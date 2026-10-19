using System.Drawing.Imaging;
using System.Globalization;
using System.Numerics;
using GTViewer.Rendering;

namespace GTViewer;

/// <summary>
/// GTViewer --shot &lt;file&gt; &lt;out.png&gt; [--yaw D] [--pitch D] [--lod N] [--size WxH] [--wire] [--flat] [--no-ao]
/// [--set selector=branch]... [--obj out.obj] [--obj-lods dir\name] [--obj-car dir\name] [--paint N] [--only shape,shape...]: render the default view of a file off screen to a PNG and exit (no window), so the
/// renderer can be checked without using the mouse. Writes a text report next to the PNG.
/// </summary>
static class Shot
{
    public static int Run(string[] args)
    {
        string file = args[1], output = args[2];
        int w = 1280, h = 800;
        var cam = new OrbitCamera();
        using var renderer = new Renderer();
        int lod = 0, paint = -1;
        bool flat = false;
        var sets = new List<(int, int)>();
        string? objPath = null, lodsDir = null, carDir = null;
        HashSet<int>? only = null;
        for (int i = 3; i < args.Length; i++)
            switch (args[i])
            {
                case "--yaw": cam.Yaw = float.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--pitch": cam.Pitch = float.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--lod": lod = int.Parse(args[++i]); break;
                case "--size": var p = args[++i].Split('x'); w = int.Parse(p[0]); h = int.Parse(p[1]); break;
                case "--wire": renderer.Wireframe = true; break;
                case "--flat": flat = true; break;
                case "--no-ao": renderer.VertexOcclusion = false; break;
                case "--set": var q = args[++i].Split('='); sets.Add((int.Parse(q[0]), int.Parse(q[1]))); break;
                case "--obj": objPath = args[++i]; break;
                case "--obj-lods": lodsDir = args[++i]; break;
                case "--obj-car": carDir = args[++i]; break;
                case "--paint": paint = int.Parse(args[++i]); break;
                case "--only": only = args[++i].Split(',').Select(int.Parse).ToHashSet(); break;
            }
        renderer.CreateOffscreen(w, h);
        using var doc = new ViewerDocument(file, renderer) { Lod = lod, Textures = !flat };
        foreach (var (s, b) in sets) doc.State[s] = b;
        if (paint >= 0) doc.SetPaint(paint);
        if (only != null)
            foreach (var n in doc.Scene.Root.All().Where(n => n.Kind == "shape" && !only.Contains(n.Shape))) doc.Hidden.Add(n);
        var items = doc.DrawItems(out var min, out var max);
        cam.Frame(min, max);
        renderer.Render(cam.ViewProj(w / (float)h), new Vector3(-0.4f, -1, -0.6f), items);
        var px = renderer.ReadPixels();
        using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++) System.Runtime.InteropServices.Marshal.Copy(px, y * w * 4, data.Scan0 + y * data.Stride, w * 4);
            bmp.UnlockBits(data);
            bmp.Save(output, ImageFormat.Png);
        }
        if (objPath != null) ObjExport.Write(doc, objPath);   // the same export as File > Export visible to OBJ
        if (lodsDir != null) ObjExport.WriteAllLods(doc, System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(lodsDir))!, System.IO.Path.GetFileName(lodsDir));   // ... all LOD levels
        List<string> car = carDir == null ? [] : ObjExport.WriteCar(doc, renderer, System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(carDir))!, System.IO.Path.GetFileName(carDir));   // ... the whole car
        var shapes = doc.VisibleShapes().ToList();
        int failed = shapes.Count(n => doc.Mesh(n.Shape, out _) == null);
        File.WriteAllLines(System.IO.Path.ChangeExtension(output, ".txt"),
        [
            $"{file}: {(renderer.Software ? "WARP (software)" : "hardware")} device, {w}x{h}, LOD {lod}/{doc.LodCount}",
            $"visible shape nodes {shapes.Count}, drawn {items.Count}, without mesh {failed}, textured {items.Count(i => i.Texture != null)}, triangles {doc.VisibleTriangles()}",
            $"box {min} .. {max}",
            $"paint {doc.Paint}/{doc.Paints.Count}: {(doc.Paint >= 0 ? doc.Paints[doc.Paint].Name : "-")}, variation {doc.TextureSet.Variation} of {doc.TextureSet.VariationCount}",
            .. car.Select(x => "car export: " + x),
            $"colour sources (shapes):{string.Join(", ", shapes.GroupBy(n => doc.TextureSet.Look(doc.MaterialOf(n.Shape)).ColorSource).Select(g => $"{g.Key} {g.Count()}"))}",
            .. doc.Scene.Selectors.Select(s => $"switch [{s.Id}] {s.Name} = {doc.State[s.Id]}"),
            .. shapes.Select(n => (n, f: doc.Mesh(n.Shape, out var why) == null ? why : null)).Where(x => x.f != null).Select(x => $"no mesh: s{x.n.Shape} {x.n.Name}: {x.f}"),
        ]);
        return 0;
    }
}
