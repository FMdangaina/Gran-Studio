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

    /// <summary>Short name: the file name of the first texture name, else "image N".</summary>
    public string ShortName
    {
        get
        {
            string n = Names.FirstOrDefault() ?? "";
            n = n.Replace('\\', '/');
            if (n.Contains('/')) n = n[(n.LastIndexOf('/') + 1)..];
            return n.Length > 0 ? n : $"image {Image}";
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
            if (e.Record.Offset != 0)
                rgba = e.Record.DecodeLevel0(data.AsSpan((int)e.Record.Offset, (int)Math.Min(e.Record.Size, data.Length - e.Record.Offset)), e.Pitch);
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
}
