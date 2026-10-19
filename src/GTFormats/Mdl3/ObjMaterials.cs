using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GTFormats.Mdl3;

/// <summary>
/// OBJ materials for ZModeler 2.2.6 (ROADMAP 12): ZModeler ignores OBJ <c>o</c>/<c>g</c> and names each object after its
/// <c>usemtl</c> material, so every object gets its own material named <c>object__GTmaterial</c>
/// (<c>BODY.bonnet.s169__BODY_SS</c>): the object and the GT material both readable in ZModeler (12a). Every object has a
/// texture file (12c): its texture (diffuse, Raytrace colour map, race number), saved under its Polyphony texture name,
/// or else a small PNG of its colour (<c>colour_RRGGBB.png</c>: paint, constant or shader colour, see
/// <see cref="Mdl3TextureSet.Look"/>). Alpha = map_d, specular = map_Ks, normal = bump; the other samplers and the
/// external ones (drawn by the game at run time) are listed as comments.
/// </summary>
public sealed class ObjMaterials(Mdl3File file, Mdl3TextureSet textures)
{
    readonly List<(string Name, int Material, string Comment)> entries = [];
    readonly HashSet<string> taken = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<int, string?> written = [];      // image -> file name in the output folder (null = no pixels)
    readonly HashSet<string> files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A unique material name for an object: object__material (letters, digits, <c>._-</c>; repeats get <c>_2</c>...).</summary>
    public string Add(string objectName, int material, string comment = "")
    {
        string mat = material >= 0 && material < file.Materials.Count ? MaterialName(file.Materials[material].Name) : "";
        string baseName = Clean(objectName) + (mat.Length > 0 ? "__" + mat : ""), name = baseName;
        for (int k = 2; !taken.Add(name); k++) name = $"{baseName}_{k}";
        entries.Add((name, material, comment));
        return name;
    }

    /// <summary>The GT material name for an OBJ name: Maya namespace and `_CLR_VARI_k` (paint variation) removed, max 32.</summary>
    public static string MaterialName(string name)
    {
        string n = name.Split(' ')[0];
        if (n.Contains(':')) n = n[(n.LastIndexOf(':') + 1)..];
        n = Regex.Replace(n, @"_CLR_VARI_\d+$", "");
        n = Clean(n, "");
        return n.Length > 32 ? n[..32] : n;
    }

    public static string Clean(string s) => Clean(s, "object");

    static string Clean(string s, string empty)
    {
        var sb = new StringBuilder();
        foreach (char c in s) sb.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_');
        string r = sb.ToString().Trim('_');
        if (r.Length > 60) r = r[..60];
        return r.Length > 0 ? r : empty;
    }

    static readonly Dictionary<string, string> MtlMaps = new()
    {
        ["alphaMapSampler"] = "map_d",
        ["specularMapSampler"] = "map_Ks",
        ["normalMapSampler"] = "bump",
    };

    /// <summary>Writes the .mtl and the textures (PNG, through <paramref name="savePng"/>) into the folder of <paramref name="mtlPath"/>.</summary>
    public void Write(string mtlPath, Action<string, byte[], int, int> savePng)
    {
        var ci = CultureInfo.InvariantCulture;
        string dir = Path.GetDirectoryName(Path.GetFullPath(mtlPath))!;
        var sb = new StringBuilder();
        sb.AppendLine("# GTViewer: one material per object, named object__GTmaterial (ZModeler 2 names objects after usemtl)");
        foreach (var (name, mi, comment) in entries)
        {
            sb.AppendLine($"newmtl {name}");
            var mat = mi >= 0 && mi < file.Materials.Count ? file.Materials[mi] : null;
            var data = mat != null && mat.DataIndex >= 0 && mat.DataIndex < file.MaterialData.Count ? file.MaterialData[mat.DataIndex] : null;
            sb.AppendLine($"# GT material {mi} {mat?.Name} ({data?.Name}){(comment.Length > 0 ? " " + comment : "")}");
            var look = textures.Look(mi);
            var c = look.Color;
            sb.AppendLine($"# colour: {look.ColorSource}");
            sb.AppendLine(string.Create(ci, $"Kd {c.X:0.###} {c.Y:0.###} {c.Z:0.###}"));
            // the texture of the object: its own, else a swatch of its colour
            string? tex = look.Image >= 0 ? Texture(look.Image, dir, savePng) : null;
            sb.AppendLine($"map_Kd {tex ?? Swatch(c, dir, savePng)}");
            if (look.Image >= 0 && look.Sampler != "diffuseMapSampler") sb.AppendLine($"# map_Kd from {look.Sampler}");
            if (mat != null)
                foreach (var key in mat.Samplers)
                {
                    if (key.External) { sb.AppendLine($"# {key.Sampler}: external {key.Index & 0x7FFF} (drawn by the game at run time)"); continue; }
                    if (key.Index >= file.TextureInfos.Count) continue;
                    int image = file.TextureInfos[key.Index].ImageIndex;
                    if (image == look.Image && key.Sampler is "diffuseMapSampler" or "colorMapSampler") continue;
                    string? f = Texture(image, dir, savePng);
                    if (f == null) { sb.AppendLine($"# {key.Sampler}: no pixels found"); continue; }
                    sb.AppendLine(MtlMaps.TryGetValue(key.Sampler, out var map) ? $"{map} {f}" : $"# {key.Sampler} {f}");
                }
            sb.AppendLine();
        }
        File.WriteAllText(mtlPath, sb.ToString());
    }

    /// <summary>Level 0 of an image saved once per folder as &lt;texture name&gt;.png (image index added on a name clash).</summary>
    string? Texture(int image, string dir, Action<string, byte[], int, int> savePng)
    {
        if (written.TryGetValue(image, out var f)) return f;
        f = null;
        if (image < textures.Entries.Count && textures.Decode(image) is { } rgba)
        {
            var e = textures.Entries[image];
            string stem = Clean(Path.GetFileNameWithoutExtension(e.ShortName));
            if (e.SharedFrom != null) stem = Clean(e.ShortName.Replace(' ', '_'));   // race_number_a.01
            f = stem + ".png";
            if (!files.Add(f)) { f = $"{stem}_{image:D3}.png"; files.Add(f); }
            savePng(Path.Combine(dir, f), rgba, e.Record.Width, rgba.Length / 4 / e.Record.Width);
        }
        return written[image] = f;
    }

    /// <summary>An 8x8 PNG of one colour (colour_RRGGBB.png), once per folder.</summary>
    string Swatch(System.Numerics.Vector3 c, string dir, Action<string, byte[], int, int> savePng)
    {
        byte R(float v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        string f = $"colour_{R(c.X):X2}{R(c.Y):X2}{R(c.Z):X2}.png";
        if (files.Add(f))
        {
            var px = new byte[8 * 8 * 4];
            for (int i = 0; i < 64; i++) { px[i * 4] = R(c.X); px[i * 4 + 1] = R(c.Y); px[i * 4 + 2] = R(c.Z); px[i * 4 + 3] = 255; }
            savePng(Path.Combine(dir, f), px, 8, 8);
        }
        return f;
    }
}
