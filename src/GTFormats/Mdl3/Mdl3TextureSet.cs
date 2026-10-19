using GTFormats.Txs3;

namespace GTFormats.Mdl3;

/// <summary>One TXS3 image of a file, with what refers to it.</summary>
public sealed class Mdl3TextureEntry
{
    public int Image;                                    // TXS3 image index
    public Txs3Image Record = null!;
    public List<string> Names = [];                      // texture info names (Polyphony source paths), distinct
    public List<(int Material, string MaterialName, string Sampler)> Users = [];
    public int Pitch;                                    // texture info CONTROL3 pitch (linear images)
    public List<string> Where = [];                      // "file", or the body_s bundles holding the pixels
    public string? SharedFrom;                           // a texture of another game file (race numbers, meter digits)
    internal byte[]? Data;                               // the bytes its offset is relative to, when not the set's file

    /// <summary>Short name: the file name of the first texture name, else "image N".</summary>
    public string ShortName
    {
        get
        {
            string n = Names.FirstOrDefault() ?? "";
            n = n.Replace('\\', '/');
            if (n.Contains('/')) n = n[(n.LastIndexOf('/') + 1)..];
            return n.Length > 0 ? n : SharedFrom != null ? Path.GetFileName(SharedFrom) : $"image {Image}";
        }
    }

    /// <summary>Format as text: DXT1 / DXT3 / DXT5 / A8R8G8B8 (+ linear), cube, 3D.</summary>
    public string FormatText
    {
        get
        {
            string f = Record.BaseFormat switch { 0x86 => "DXT1", 0x87 => "DXT3", 0x88 => "DXT5", 0x85 => "A8R8G8B8", var x => $"0x{x:X2}" };
            if (Record.Linear) f += " linear";
            if (Record.Cube) f += ", cube";
            if (Record.Depth > 1) f += $", 3D x{Record.Depth}";
            return f;
        }
    }
}

/// <summary>
/// The textures of an MDL3 for a texture browser: every TXS3 image with its names and users (material, sampler), the
/// external samplers (textures the game provides at run time), and the decoding of level 0 (pixels in the file, or
/// for GT6 car bodies in <c>body_s</c>, see docs/txs3.md). Decoded images are cached.
/// </summary>
public sealed class Mdl3TextureSet
{
    public Mdl3File? File { get; }
    public List<Mdl3TextureEntry> Entries { get; } = [];
    public List<(int Material, string MaterialName, string Sampler, int Index)> External { get; } = [];

    readonly Dictionary<int, byte[]?> decoded = [];
    readonly byte[] data;                                // the MDL3 or the stand-alone TXS3 file
    Dictionary<int, List<(string where, byte[] bundle, int offset)>>? bodySImages;
    bool bodySRead;

    /// <summary>Is this a stand-alone TXS3 file ("TXS3" magic: car thumbnails, `decken` race numbers)?</summary>
    public static bool IsTxs3(byte[] file) => file.Length >= 0x20 && file[0] == 'T' && file[1] == 'X' && file[2] == 'S' && file[3] == '3';

    /// <summary>A stand-alone TXS3 file: its images (pixel offsets absolute in the file; +08 is 2 there, not a cube flag).</summary>
    public Mdl3TextureSet(byte[] txs3)
    {
        data = txs3;
        var images = Txs3Image.ReadAll(new BigEndianReader(txs3), 0);
        for (int i = 0; i < images.Count; i++)
        {
            images[i].Cube = false;
            Entries.Add(new Mdl3TextureEntry { Image = i, Record = images[i], Where = ["file"] });
        }
    }

    public Mdl3TextureSet(Mdl3File m)
    {
        File = m;
        data = m.Data.Data;
        int txs = (int)m.Data.U32(0x48);
        if (txs == 0) return;
        var images = Txs3Image.ReadAll(m.Data, txs);
        for (int i = 0; i < images.Count; i++) Entries.Add(new Mdl3TextureEntry { Image = i, Record = images[i] });
        foreach (var info in m.TextureInfos)
            if (info.ImageIndex < Entries.Count)
            {
                var e = Entries[info.ImageIndex];
                if (info.Name.Length > 0 && !e.Names.Contains(info.Name)) e.Names.Add(info.Name);
                if (e.Pitch == 0) e.Pitch = info.Pitch;
            }
        for (int mi = 0; mi < m.Materials.Count; mi++)
            foreach (var key in m.Materials[mi].Samplers)
            {
                if (key.External) { External.Add((mi, m.Materials[mi].Name, key.Sampler, key.Index & 0x7FFF)); continue; }
                if (key.Index >= m.TextureInfos.Count) continue;
                int img = m.TextureInfos[key.Index].ImageIndex;
                if (img < Entries.Count) Entries[img].Users.Add((mi, m.Materials[mi].Name, key.Sampler));
            }
        foreach (var e in Entries)
            if (e.Record.Offset != 0) e.Where.Add("file");
        FindShared();   // here, so the entry list does not change while a browser lists it
    }

    /// <summary>For GT6 bodies: which body_s bundles hold the pixels (filled on first use, also by <see cref="Decode"/>).</summary>
    public void FindBodyS()
    {
        if (bodySRead) return;
        bodySRead = true;
        if (File?.SourcePath == null || !System.IO.File.Exists(File.SourcePath + "_s") || Mdl3StreamMap.Read(File.Data) is not { } map) return;
        bodySImages = map.FindImages(System.IO.File.ReadAllBytes(File.SourcePath + "_s"));
        foreach (var (img, l) in bodySImages)
            if (img < Entries.Count) Entries[img].Where.AddRange(l.Select(x => "body_s " + x.where));
    }

    /// <summary>Level 0 of an image as RGBA (faces and 3D slices stacked top to bottom), null when the pixels are not found.</summary>
    public byte[]? Decode(int image)
    {
        lock (decoded) return DecodeLocked(image);   // the browser decodes thumbnails in the background
    }

    byte[]? DecodeLocked(int image)
    {
        if (decoded.TryGetValue(image, out var rgba)) return rgba;
        var e = Entries[image];
        try
        {
            var bytes = e.Data ?? data;
            if (e.Record.Offset != 0)
                rgba = e.Record.DecodeLevel0(bytes.AsSpan((int)e.Record.Offset, (int)Math.Min(e.Record.Size, bytes.Length - e.Record.Offset)), e.Pitch);
            else
            {
                FindBodyS();
                if (bodySImages != null && bodySImages.TryGetValue(image, out var where) && where.Count > 0)
                    rgba = e.Record.DecodeLevel0(where[0].bundle.AsSpan(where[0].offset, (int)e.Record.Size), e.Pitch);
            }
        }
        catch { rgba = null; }
        decoded[image] = rgba;
        return rgba;
    }

    // ---- shared textures: other game files the car uses (ROADMAP 12c / 11.2) ----

    bool sharedRead;

    /// <summary>The race number image (car\decken) used for the Decken materials, -1 when none.</summary>
    public int DeckenImage { get; private set; } = -1;

    /// <summary>
    /// Adds the textures of other game files this model uses, as entries with <see cref="Mdl3TextureEntry.SharedFrom"/>:
    /// the race numbers (`car\decken`, TXS3 per font a-h and number 00-99; the game picks one at run time, number 01 of
    /// font a is shown) when a Decken material takes an external texture, and the digits of the car's `meter` file
    /// (STRB holding TXS3 blocks) when a meter material does. The other external samplers (number plates, extra meters,
    /// paint colour shift) are drawn by the game at run time: no file holds them.
    /// </summary>
    public void FindShared()
    {
        if (sharedRead || File?.SourcePath == null) return;
        sharedRead = true;
        try
        {
            string path = Path.GetFullPath(File.SourcePath);
            string? car = CarFolder(path);
            if (car == null) return;
            bool Uses(string word) => External.Any(x => x.MaterialName.Contains(word, StringComparison.OrdinalIgnoreCase));
            if (Uses("decken"))
            {
                string dir = Path.Combine(car, "decken");
                string? f = new[] { "a.01", ".01" }.Select(n => Path.Combine(dir, n)).FirstOrDefault(System.IO.File.Exists)
                            ?? (Directory.Exists(dir) ? Directory.GetFiles(dir).Order().Skip(1).FirstOrDefault() : null);
                if (f != null)
                {
                    int before = Entries.Count;
                    AddSharedTxs3(System.IO.File.ReadAllBytes(f), f, "race number " + Path.GetFileName(f));
                    if (Entries.Count > before)
                    {
                        DeckenImage = before;
                        Entries[before].Users.AddRange(External.Where(x => x.MaterialName.Contains("decken", StringComparison.OrdinalIgnoreCase) && x.Sampler == "diffuseMapSampler")
                            .Select(x => (x.Material, x.MaterialName, x.Sampler + " (external)")));
                    }
                }
            }
            if (Uses("meter") && MeterFile(path, car) is { } meter)
            {
                // TXS3 blocks (aligned), each one cut up to the next: their offsets are relative to the block
                var bytes = System.IO.File.ReadAllBytes(meter);
                var starts = new List<int>();
                for (int i = 0; i + 4 <= bytes.Length; i += 4)
                    if (bytes[i] == 'T' && bytes[i + 1] == 'X' && bytes[i + 2] == 'S' && bytes[i + 3] == '3') starts.Add(i);
                for (int k = 0; k < starts.Count; k++)
                    AddSharedTxs3(bytes[starts[k]..(k + 1 < starts.Count ? starts[k + 1] : bytes.Length)], meter, $"meter digit {k}");
            }
        }
        catch { }
    }

    void AddSharedTxs3(byte[] bytes, string from, string name)
    {
        foreach (var img in Txs3Image.ReadAll(new BigEndianReader(bytes), 0))
        {
            img.Cube = false;   // +08 is 2 in stand-alone TXS3, not a cube flag
            Entries.Add(new Mdl3TextureEntry { Image = Entries.Count, Record = img, Names = [name], Where = ["shared: " + from], SharedFrom = from, Data = bytes });
        }
    }

    /// <summary>The `car` folder above a model (GT5 car\hq\CODE, wheel\hq\CODE; GT6 car\MMMM\NNNN\...).</summary>
    public static string? CarFolder(string modelPath)
    {
        for (var d = Path.GetDirectoryName(modelPath); d != null; d = Path.GetDirectoryName(d))
        {
            if (Path.GetFileName(d).Equals("car", StringComparison.OrdinalIgnoreCase)) return d;
            if (Directory.Exists(Path.Combine(d, "car")) && Directory.Exists(Path.Combine(d, "car", "decken"))) return Path.Combine(d, "car");
        }
        return null;
    }

    /// <summary>The meter file of a car model: GT6 car\MMMM\NNNN\meter, GT5 car\meter\CODE.</summary>
    static string? MeterFile(string modelPath, string car)
    {
        var rel = Path.GetRelativePath(car, modelPath).Split(Path.DirectorySeparatorChar);
        string? f = rel.Length >= 3 && rel[0].Length == 4 && rel[1].Length == 4 ? Path.Combine(car, rel[0], rel[1], "meter")   // GT6
            : rel.Length == 2 ? Path.Combine(car, "meter", rel[1].Split('.')[0])                                              // GT5 car\hq\CODE
            : null;
        return f != null && System.IO.File.Exists(f) ? f : null;
    }

    // ---- paint colour (ROADMAP 13): GT6 colour variations _CLR_VARI_k ----

    int variationBlock = -1;   // material table offset between variation k and k + 1, 0 none (filled on first use)
    int variationCount;

    /// <summary>
    /// GT6 bodies repeat their whole material table once per colour variation (MX-5: m0, m85, m170, m255 =
    /// `_BODY_DIRT_CLR_VARI_0..3`), then once for the `_PaintExtraBRDF` paints; the shapes use the first block and the
    /// game moves to block k for variation k. Variations (V) are not always the colours: V = number of colours (MX-5: 4)
    /// or 1 (RX-8: 7 colours, the colour from the database). Returns V (0 = no variations).
    /// </summary>
    public int VariationCount { get { FindVariations(); return variationCount; } }

    /// <summary>The variation shown (0 .. VariationCount - 1).</summary>
    public int Variation { get; private set; }

    /// <summary>The body paint colour (database ColorChip), null = the colour of the variation's colour-shift image.</summary>
    public System.Numerics.Vector3? PaintColor { get; private set; }

    public void SetPaint(int variation, System.Numerics.Vector3? color)
    {
        FindVariations();
        lock (looks)
        {
            Variation = Math.Clamp(variation, 0, Math.Max(0, variationCount - 1));
            PaintColor = color;
            looks.Clear();
        }
    }

    void FindVariations()
    {
        if (variationBlock >= 0 || File == null) return;
        variationBlock = 0;
        var mats = File.Materials;
        var rx = new System.Text.RegularExpressions.Regex(@"_CLR_VARI_(\d+)$");
        int top = -1;
        foreach (var m in mats) if (rx.Match(m.Name) is { Success: true } x) top = Math.Max(top, int.Parse(x.Groups[1].Value));
        variationCount = top + 1;
        for (int i = 0; i < mats.Count && variationCount > 1; i++)
            if (mats[i].Name.EndsWith("_CLR_VARI_0"))
            {
                string next = mats[i].Name[..^1] + "1";
                int j = mats.FindIndex(i + 1, m => m.Name == next);
                if (j > i) { variationBlock = j - i; break; }
            }
    }

    /// <summary>The material drawn for a shape's material with the current variation (the same index for variation 0).</summary>
    public int Material(int material)
    {
        FindVariations();
        if (File == null || Variation == 0 || variationBlock <= 0 || material < 0) return material;
        int target = material + Variation * variationBlock;
        if (target >= File.Materials.Count) return material;
        string a = File.Materials[material].Name, b = File.Materials[target].Name;
        // the same material of the other block: same name, or _CLR_VARI_0 -> _CLR_VARI_k
        return b == a || a.EndsWith("_CLR_VARI_0") && b == a[..^1] + Variation || a.Contains("_CLR_VARI_") && b.StartsWith(a[..a.IndexOf("_CLR_VARI_")]) ? target : material;
    }

    /// <summary>A body paint material: its colour-shift image changes with the variation (the soft top's does not), or V = 1.</summary>
    bool IsPaint(int material)
    {
        if (File == null || !File.Materials[material].Name.Contains("_CLR_VARI_")) return false;
        if (variationCount <= 1 || variationBlock <= 0) return true;
        // compare with the same material in the other variation block
        int baseMat = material % variationBlock, other = baseMat + variationBlock;
        if (other >= File.Materials.Count) return true;
        int? Img(int mi) => File.Materials[mi]["scatterColorShiftMapSampler"] is { External: false } k && k.Index < File.TextureInfos.Count ? File.TextureInfos[k.Index].ImageIndex : null;
        return Img(baseMat) != Img(other);
    }

    // ---- what a material looks like (viewer colour and texture, export Kd and map_Kd) ----

    readonly Dictionary<int, Mdl3Look> looks = [];

    /// <summary>
    /// How a material looks without the game's lighting: its texture (diffuse map in the file or body_s, the colour map
    /// of Raytrace materials, the race number of Decken materials) and its base colour, from the first source found:
    /// the paint colour (GT6 `_CLR_VARI_` materials: the colour-shift image of the body colour), `diffuseColor0`, the
    /// colour baked in the shader (<see cref="Mdl3ShaderColor"/>), else grey.
    /// </summary>
    public Mdl3Look Look(int material)
    {
        lock (looks)
        {
            if (looks.TryGetValue(material, out var l)) return l;
            return looks[material] = MakeLook(material);
        }
    }

    Mdl3Look MakeLook(int material)
    {
        var m = File;
        var grey = new System.Numerics.Vector3(0.6f);
        if (m == null || material < 0 || material >= m.Materials.Count) return new(grey, "default", -1, "");
        var mat = m.Materials[material];
        int image = -1; string sampler = "";
        foreach (var s in new[] { "diffuseMapSampler", "colorMapSampler" })
            if (Image(mat[s]) is int i) { image = i; sampler = s; break; }
        if (image < 0 && mat.Name.Contains("decken", StringComparison.OrdinalIgnoreCase) && mat["diffuseMapSampler"] is { External: true })
        {
            if (DeckenImage >= 0 && Decode(DeckenImage) != null) { image = DeckenImage; sampler = "diffuseMapSampler (race number, shared)"; }
        }
        var data = mat.DataIndex >= 0 && mat.DataIndex < m.MaterialData.Count ? m.MaterialData[mat.DataIndex] : null;
        // paint: the colour-shift image of the body colour (4x1 or 32x1 = the base colour; else the first row of the
        // reflect table), one row images first
        foreach (var ci in new[] { "scatterColorShiftMapSampler", "reflectColorShiftMapSampler" }.Select(s => Image(mat[s])).OfType<int>().OrderBy(i => Entries[i].Record.Height == 1 ? 0 : 1))
            if ((mat.Name.Contains("_CLR_VARI_") || Entries[ci].Record.Width <= 4 && Entries[ci].Record.Height == 1) && Decode(ci) is { } px)
            {
                if (PaintColor is { } paint && IsPaint(material)) return new(paint, "paint (database colour)", image, sampler);
                int w = Entries[ci].Record.Width;
                var sum = System.Numerics.Vector3.Zero;
                for (int x = 0; x < w; x++) sum += new System.Numerics.Vector3(px[x * 4], px[x * 4 + 1], px[x * 4 + 2]);
                return new(sum / w / 255f, "paint (colour-shift image)", image, sampler);
            }
        if (data != null && data.Constants.TryGetValue("diffuseColor0", out var c))
            return new(new(Math.Clamp(c[0], 0, 1), Math.Clamp(c[1], 0, 1), Math.Clamp(c[2], 0, 1)), "diffuseColor0", image, sampler);
        if (image < 0 && Mdl3ShaderColor.Of(m, material) is { } sc) return new(sc, "shader constant", image, sampler);
        return new(image >= 0 ? new System.Numerics.Vector3(0.86f) : grey, image >= 0 ? "texture" : "default", image, sampler);
    }

    /// <summary>The image of a sampler key when it is in the file (or body_s) and decodes, else null.</summary>
    int? Image(Mdl3SamplerKey? key)
    {
        if (File == null || key == null || key.External || key.Index >= File.TextureInfos.Count) return null;
        int img = File.TextureInfos[key.Index].ImageIndex;
        return img < Entries.Count && Decode(img) != null ? img : null;
    }
}

/// <summary>
/// A material as the viewer draws it and the export writes it: base colour (and where it comes from), the texture image
/// (-1 none) and the sampler it was taken from.
/// </summary>
public sealed record Mdl3Look(System.Numerics.Vector3 Color, string ColorSource, int Image, string Sampler);
