using System.Text.RegularExpressions;
using GTFormats.Mdl3;

/// <summary>
/// mdl3-samplers &lt;files.tsv&gt; &lt;groupRegex&gt; &lt;out.txt&gt;: which textures the materials ask for. Per sampler name: how many
/// material references point to a texture in the file and how many are external (0x8000 | n, provided by the game),
/// the external n values and the texture names seen. Only materials drawn by a visible model's shape are counted.
/// </summary>
static class Mdl3Samplers
{
    sealed class Stat
    {
        public int InFile, External, Files;
        public Dictionary<int, int> ExternalN = [];
        public Dictionary<string, int> Names = [];
        public Dictionary<string, int> Shaders = [];
        public Dictionary<string, int> ExternalMaterials = [];
    }

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("mdl3-samplers <files.tsv> <groupRegex> <out.txt>"); return 1; }
        var rx = new Regex(args[1]);
        var files = File.ReadLines(args[0]).Select(l => l.Split('\t')).Where(c => rx.IsMatch(c[1])).ToList();
        var stats = new SortedDictionary<string, Stat>();
        int done = 0, materials = 0, noTexture = 0;
        foreach (var c in files)
        {
            Mdl3File m;
            try { m = Mdl3File.Load(c[0]); } catch { continue; }
            var used = Mdl3Obj.SelectShapes(m, false).Select(s => (int)m.Shapes[s].MaterialIndex).Where(i => i >= 0 && i < m.Materials.Count).ToHashSet();
            var seen = new HashSet<string>();
            foreach (int i in used)
            {
                var mat = m.Materials[i];
                materials++;
                if (mat.Samplers.Count == 0) noTexture++;
                string shader = mat.DataIndex >= 0 && mat.DataIndex < m.MaterialData.Count ? m.MaterialData[mat.DataIndex].Name : "?";
                foreach (var k in mat.Samplers)
                {
                    if (!stats.TryGetValue(k.Sampler, out var st)) stats[k.Sampler] = st = new Stat();
                    if (seen.Add(k.Sampler)) st.Files++;
                    st.Shaders[shader] = st.Shaders.GetValueOrDefault(shader) + 1;
                    if (k.External)
                    {
                        st.External++;
                        st.ExternalN[k.Index & 0x7FFF] = st.ExternalN.GetValueOrDefault(k.Index & 0x7FFF) + 1;
                        string who = $"mat '{mat.Name}'";
                        st.ExternalMaterials[who] = st.ExternalMaterials.GetValueOrDefault(who) + 1;
                    }
                    else
                    {
                        st.InFile++;
                        string name = k.Index < m.TextureInfos.Count ? m.TextureInfos[k.Index].Name : "?";
                        st.Names[name] = st.Names.GetValueOrDefault(name) + 1;
                    }
                }
            }
            if (++done % 200 == 0) Console.WriteLine($"{done}/{files.Count}");
        }
        using var w = new StreamWriter(args[2]);
        w.WriteLine($"{done} files, {materials} used materials, {noTexture} without any sampler");
        w.WriteLine("sampler: references in file / external, files using it; shaders; external n values; texture names");
        foreach (var (k, st) in stats.OrderByDescending(x => x.Value.InFile + x.Value.External))
        {
            w.WriteLine($"{k,-36} in file {st.InFile,7}  external {st.External,7}  files {st.Files,5}");
            w.WriteLine($"     shaders: {string.Join(", ", st.Shaders.OrderByDescending(x => x.Value).Take(6).Select(x => $"{x.Key} {x.Value}"))}");
            if (st.ExternalN.Count > 0)
                w.WriteLine($"     external n: {string.Join(", ", st.ExternalN.OrderByDescending(x => x.Value).Take(12).Select(x => $"{x.Key}:{x.Value}"))}");
            if (st.ExternalMaterials.Count > 0)
                w.WriteLine($"     external in: {string.Join(", ", st.ExternalMaterials.OrderByDescending(x => x.Value).Take(10).Select(x => $"{x.Key} {x.Value}"))}");
            if (st.Names.Count > 0)
                w.WriteLine($"     names: {string.Join(", ", st.Names.OrderByDescending(x => x.Value).Take(8).Select(x => $"{x.Key} {x.Value}"))}");
        }
        Console.WriteLine($"{done} files -> {args[2]}");
        return 0;
    }
}
