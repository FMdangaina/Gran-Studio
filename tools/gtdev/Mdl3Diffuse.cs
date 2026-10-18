using GTFormats.Mdl3;
using GTFormats.Txs3;

/// <summary>
/// mdl3-diffuse &lt;file&gt;: per used material (biggest first by triangles): shader, diffuse image (format, size, mean
/// RGB, share of transparent pixels) and the other samplers. To find why a part looks wrong in the viewer.
/// </summary>
static class Mdl3Diffuse
{
    public static int Run(string[] args)
    {
        var m = Mdl3File.Load(args[0]);
        var images = Txs3Image.ReadAll(m.Data, (int)m.Data.U32(0x48));
        var bodyS = File.Exists(args[0] + "_s") && Mdl3StreamMap.Read(m.Data) is { } map ? map.FindImages(File.ReadAllBytes(args[0] + "_s")) : null;
        var tris = new Dictionary<int, int>();
        var shapes = new Dictionary<int, List<int>>();
        foreach (int s in Mdl3Obj.SelectShapes(m, false))
        {
            int mat = m.Shapes[s].MaterialIndex;
            Mdl3Mesh? mesh = null;
            try { mesh = Mdl3Mesh.Decode(m, s, 0, out _); } catch { }
            tris[mat] = tris.GetValueOrDefault(mat) + (mesh?.Indices.Length ?? 0) / 3;
            if (!shapes.TryGetValue(mat, out var l)) shapes[mat] = l = [];
            l.Add(s);
        }
        foreach (var (mat, n) in tris.OrderByDescending(x => x.Value))
        {
            if (mat < 0 || mat >= m.Materials.Count) continue;
            var mt = m.Materials[mat];
            string shader = mt.DataIndex >= 0 && mt.DataIndex < m.MaterialData.Count ? m.MaterialData[mt.DataIndex].Name : "?";
            string diff = "-";
            if (mt["diffuseMapSampler"] is { External: false } k && k.Index < m.TextureInfos.Count)
            {
                var ti = m.TextureInfos[k.Index];
                var img = images[ti.ImageIndex];
                byte[]? rgba = null;
                if (img.Offset != 0) rgba = img.DecodeLevel0(m.Data.Data.AsSpan((int)img.Offset, (int)img.Size), ti.Pitch);
                else if (bodyS != null && bodyS.TryGetValue(ti.ImageIndex, out var w) && w.Count > 0) rgba = img.DecodeLevel0(w[0].bundle.AsSpan(w[0].offset, (int)img.Size), ti.Pitch);
                if (rgba != null)
                {
                    int px = rgba.Length / 4;
                    double r = 0, g = 0, b = 0; int clear = 0;
                    for (int i = 0; i < px; i++) { r += rgba[i * 4]; g += rgba[i * 4 + 1]; b += rgba[i * 4 + 2]; if (rgba[i * 4 + 3] < 128) clear++; }
                    diff = $"wrap {ti.WrapS}/{ti.WrapT} img {ti.ImageIndex} {Txs3Survey.FormatName(img.Format)} {img.Width}x{img.Height} mean {r / px:0} {g / px:0} {b / px:0} clear {100.0 * clear / px:0}%  {Path.GetFileName(ti.Name)}";
                }
                else diff = $"img {ti.ImageIndex} not decoded {Path.GetFileName(ti.Name)}";
            }
            Console.WriteLine($"{n,7} tris  m{mat} {mt.Name} [{shader}]  diffuse: {diff}");
            foreach (int s in shapes[mat].Take(2))
            {
                var sh = m.Shapes[s];
                string decl = "";
                if (sh.FvfIndex < 0 && m.Pmsh != null && Mdl3Pmsh.MeshIndex(m.Data, sh) is int mi && mi >= 0 && mi < m.Pmsh.Meshes.Count)
                {
                    var d = m.Pmsh.Declarations[m.Pmsh.Meshes[mi].Declaration];
                    decl = $"pmsh decl packed [{string.Join(" ", d.Packed.Select(e => $"{e.Name}:{e.Type}x{e.Count}"))}] raw [{string.Join(" ", d.Raw.Select(e => $"{e.Name}:{e.Type}x{e.Count}@{e.OutputOffset}"))}] stride {d.RawStride}";
                }
                else if (sh.FvfIndex >= 0) decl = $"fvf {m.Fvfs[sh.FvfIndex].Name}";
                Mdl3Mesh? mesh = null;
                try { mesh = Mdl3Mesh.Decode(m, s, 0, out _); } catch { }
                string uv = mesh?.Uvs is { Length: > 0 } u ? $"uv {u.Min(x => x.X):0.###}..{u.Max(x => x.X):0.###} x {u.Min(x => x.Y):0.###}..{u.Max(x => x.Y):0.###}" : "no uv";
                Console.WriteLine($"         s{s}: {decl}  {uv}");
            }
            if (mt.DataIndex >= 0 && mt.DataIndex < m.MaterialData.Count)
                Console.WriteLine($"         shader def {m.MaterialData[mt.DataIndex].ShaderDefinition}, data {mt.DataIndex}, state {mt.StateIndex}, flags {mt.Flags:X}  constants: {string.Join("; ", m.MaterialData[mt.DataIndex].Constants.Select(c => $"{c.Key}=({string.Join(",", c.Value.Select(v => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)))})"))}");
            Console.WriteLine($"         samplers:{string.Join(", ", mt.Samplers.Select(s => s.Sampler.Replace("MapSampler", "") + (s.External ? "(ext)" : s.Index < m.TextureInfos.Count ? $"=img{m.TextureInfos[s.Index].ImageIndex} {Path.GetFileName(m.TextureInfos[s.Index].Name)}" : "")))}  shapes {string.Join(",", shapes[mat].Take(8))}");
        }
        return 0;
    }
}
