using System.Drawing.Imaging;
using System.Globalization;
using System.Text;
using GTFormats.Mdl3;

namespace GTViewer;

/// <summary>
/// Export of what the viewer shows (visible shapes, current switches and LOD level) as OBJ + MTL + diffuse PNGs, for
/// ZModeler 2.2.6: one object per shape (`g` group path, `o` readable name; ZModeler names the objects after the
/// `usemtl` material), V flipped.
/// </summary>
static class ObjExport
{
    public static int Write(ViewerDocument doc, string path)
    {
        var ci = CultureInfo.InvariantCulture;
        string dir = Path.GetDirectoryName(Path.GetFullPath(path))!, stem = Path.GetFileNameWithoutExtension(path);
        var obj = new StringBuilder();
        obj.AppendLine($"# GTViewer export of {doc.Path}, LOD {doc.Lod}");
        obj.AppendLine($"mtllib {stem}.mtl");
        var used = new SortedSet<int>();
        int vBase = 1, exported = 0;
        var done = new HashSet<int>();   // a shape drawn by two models is written once
        foreach (var n in doc.VisibleShapes())
        {
            if (!done.Add(n.Shape)) continue;
            var mesh = doc.DecodeForExport(n.Shape);
            if (mesh == null || mesh.Indices.Length == 0) continue;
            var sh = doc.File.Shapes[n.Shape];
            var groups = new List<string>();
            for (var p = n.Parent; p != null && p.Kind != "file"; p = p.Parent) groups.Insert(0, p.Name);
            obj.AppendLine($"g {string.Join('/', groups)}");
            obj.AppendLine($"o {n.Name}");
            if (sh.MaterialIndex >= 0) { obj.AppendLine($"usemtl m{sh.MaterialIndex:D3}"); used.Add(sh.MaterialIndex); }
            var nrm = mesh.Normals ?? Mdl3Mesh.FaceNormals(mesh.Positions, mesh.Indices);
            foreach (var v in mesh.Positions) obj.AppendLine(string.Create(ci, $"v {v.X} {v.Y} {v.Z}"));
            foreach (var t in mesh.Uvs ?? new System.Numerics.Vector2[mesh.Positions.Length]) obj.AppendLine(string.Create(ci, $"vt {t.X} {1 - t.Y}"));
            foreach (var q in nrm) obj.AppendLine(string.Create(ci, $"vn {q.X} {q.Y} {q.Z}"));
            for (int i = 0; i + 2 < mesh.Indices.Length; i += 3)
            {
                int a = mesh.Indices[i], b = mesh.Indices[i + 1], c = mesh.Indices[i + 2];
                if (a < 0 || b < 0 || c < 0 || a >= mesh.Positions.Length || b >= mesh.Positions.Length || c >= mesh.Positions.Length) continue;
                obj.AppendLine($"f {a + vBase}/{a + vBase}/{a + vBase} {b + vBase}/{b + vBase}/{b + vBase} {c + vBase}/{c + vBase}/{c + vBase}");
            }
            vBase += mesh.Positions.Length;
            exported++;
        }
        File.WriteAllText(path, obj.ToString());

        var mtl = new StringBuilder();
        foreach (int i in used)
        {
            mtl.AppendLine($"newmtl m{i:D3}");
            mtl.AppendLine($"# {doc.File.Materials[i].Name}");
            mtl.AppendLine("Kd 0.8 0.8 0.8");
            if (doc.DiffuseImage(i) is { Rgba: not null } img)
            {
                string png = $"{stem}_tex{img.Image:D3}.png";
                string full = Path.Combine(dir, png);
                if (!File.Exists(full)) SavePng(full, img.Rgba!, img.Width, img.Height);
                mtl.AppendLine($"map_Kd {png}");
            }
            mtl.AppendLine();
        }
        File.WriteAllText(Path.Combine(dir, stem + ".mtl"), mtl.ToString());
        return exported;
    }

    static void SavePng(string path, byte[] rgba, int w, int h)
    {
        var bgra = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++) { bgra[i * 4] = rgba[i * 4 + 2]; bgra[i * 4 + 1] = rgba[i * 4 + 1]; bgra[i * 4 + 2] = rgba[i * 4]; bgra[i * 4 + 3] = rgba[i * 4 + 3]; }
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < h; y++) System.Runtime.InteropServices.Marshal.Copy(bgra, y * w * 4, data.Scan0 + y * data.Stride, w * 4);
        bmp.UnlockBits(data);
        bmp.Save(path, ImageFormat.Png);
    }
}
