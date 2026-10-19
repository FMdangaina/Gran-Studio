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
public sealed class ObjMaterials(Mdl3File file, Mdl3TextureSet textures, ObjTextureFolder? folder = null)
{
    readonly List<(string Name, int Material, string Comment)> entries = [];
    readonly HashSet<string> taken = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<int, string?> written = [];      // image -> file name in the texture folder (null = no pixels)

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
        string mtlDir = Path.GetDirectoryName(Path.GetFullPath(mtlPath))!;
        var textureFolder = folder ?? new ObjTextureFolder(mtlDir);
        textureFolder.Save = savePng;
        // the texture as the .mtl refers to it: its name, or a relative path when the textures are in another folder
        string Ref(string f) => Path.GetRelativePath(mtlDir, Path.Combine(textureFolder.Directory, f));
        string? Texture(int image) => this.Texture(image, textureFolder) is { } f ? Ref(f) : null;
        string Swatch(System.Numerics.Vector3 c) => Ref(textureFolder.Swatch(c));
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
            string? tex = look.Image >= 0 ? Texture(look.Image) : null;
            sb.AppendLine($"map_Kd {tex ?? Swatch(c)}");
            if (look.Image >= 0 && look.Sampler != "diffuseMapSampler") sb.AppendLine($"# map_Kd from {look.Sampler}");
            if (mat != null)
                foreach (var key in mat.Samplers)
                {
                    if (key.External) { sb.AppendLine($"# {key.Sampler}: external {key.Index & 0x7FFF} (drawn by the game at run time)"); continue; }
                    if (key.Index >= file.TextureInfos.Count) continue;
                    int image = file.TextureInfos[key.Index].ImageIndex;
                    if (image == look.Image && key.Sampler is "diffuseMapSampler" or "colorMapSampler") continue;
                    string? f = Texture(image);
                    if (f == null) { sb.AppendLine($"# {key.Sampler}: no pixels found"); continue; }
                    sb.AppendLine(MtlMaps.TryGetValue(key.Sampler, out var map) ? $"{map} {f}" : $"# {key.Sampler} {f}");
                }
            sb.AppendLine();
        }
        File.WriteAllText(mtlPath, sb.ToString());
    }

    /// <summary>Level 0 of an image, saved in the texture folder under its texture name (file name in that folder).</summary>
    string? Texture(int image, ObjTextureFolder textureFolder)
    {
        if (written.TryGetValue(image, out var f)) return f;
        f = null;
        if (image < textures.Entries.Count && textures.Decode(image) is { } rgba)
        {
            var e = textures.Entries[image];
            string stem = e.SharedFrom != null ? Clean(e.ShortName.Replace(' ', '_')) : Clean(Path.GetFileNameWithoutExtension(e.ShortName));   // race_number_a.01
            f = textureFolder.Add(stem, rgba, e.Record.Width, rgba.Length / 4 / e.Record.Width);
        }
        return written[image] = f;
    }
}

/// <summary>
/// The folder the textures of an export go to (user 2026-10-19: one folder for all the textures of a car, next to the
/// mesh folders): each image saved once; the same name with other pixels gets _2, _3... Shared by several .mtl files.
/// </summary>
public sealed class ObjTextureFolder(string directory)
{
    public string Directory { get; } = directory;
    public Action<string, byte[], int, int> Save { get; set; } = (_, _, _, _) => { };
    readonly Dictionary<string, string> hashes = new(StringComparer.OrdinalIgnoreCase);   // file name -> pixel hash

    /// <summary>Saves the pixels as stem.png (stem_2.png... when the name holds other pixels), returns the file name.</summary>
    public string Add(string stem, byte[] rgba, int w, int h)
    {
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(rgba)) + w;
        for (int k = 1; ; k++)
        {
            string f = k == 1 ? stem + ".png" : $"{stem}_{k}.png";
            if (hashes.TryGetValue(f, out var h0))
            {
                if (h0 == hash) return f;
                continue;
            }
            hashes[f] = hash;
            System.IO.Directory.CreateDirectory(Directory);
            Save(Path.Combine(Directory, f), rgba, w, h);
            return f;
        }
    }

    /// <summary>An 8x8 PNG of one colour (colour_RRGGBB.png).</summary>
    public string Swatch(System.Numerics.Vector3 c)
    {
        byte R(float v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        var px = new byte[8 * 8 * 4];
        for (int i = 0; i < 64; i++) { px[i * 4] = R(c.X); px[i * 4 + 1] = R(c.Y); px[i * 4 + 2] = R(c.Z); px[i * 4 + 3] = 255; }
        return Add($"colour_{R(c.X):X2}{R(c.Y):X2}{R(c.Z):X2}", px, 8, 8);
    }
}
