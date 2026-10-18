namespace GTFormats.Mdl3;

/// <summary>An MDL3 file loaded in memory: header, models (with their commands), shapes and names.</summary>
public sealed class Mdl3File
{
    public BigEndianReader Data { get; }
    public Mdl3Header Header { get; }
    public List<Mdl3Model> Models { get; } = [];
    public List<Mdl3Shape> Shapes { get; } = [];
    public List<Mdl3Fvf> Fvfs { get; } = [];
    public List<Mdl3Bone> Bones { get; } = [];
    public List<Mdl3Material> Materials { get; } = [];
    public List<Mdl3MaterialData> MaterialData { get; } = [];
    public List<Mdl3TextureInfo> TextureInfos { get; } = [];
    public Dictionary<int, string> ModelNames { get; } = [];
    public Dictionary<int, List<string>> ShapeNames { get; } = [];

    public Mdl3File(byte[] data)
    {
        Data = new BigEndianReader(data);
        Header = new Mdl3Header(Data);

        int modelCount = Data.U16(0x10), shapeCount = Data.U16(0x14);
        int modelsAt = (int)Data.U32(0x30), shapesAt = (int)Data.U32(0x38);
        for (int i = 0; i < modelCount; i++) Models.Add(Mdl3Model.Read(Data, modelsAt + i * Mdl3Model.Size));
        for (int i = 0; i < shapeCount; i++) Shapes.Add(Mdl3Shape.Read(Data, shapesAt + i * Mdl3Shape.Size));
        int fvfCount = Data.U16(0x18), fvfAt = (int)Data.U32(0x40);
        for (int i = 0; i < fvfCount; i++) Fvfs.Add(Mdl3Fvf.Read(Data, fvfAt + i * Mdl3Fvf.Size));
        int boneCount = Data.U16(0x1A), bonesAt = (int)Data.U32(0x50);
        for (int i = 0; i < boneCount; i++) Bones.Add(Mdl3Bone.Read(Data, bonesAt + i * Mdl3Bone.Size));

        // Material block at 0xE4: u16 materials, u16 data, u8 ?, u8 state blocks, u16 texture infos, 4 offsets.
        const int mat = 0xE4;
        for (int i = 0; i < Data.U16(mat); i++) Materials.Add(Mdl3Material.Read(Data, (int)Data.U32(mat + 8) + i * Mdl3Material.Size));
        for (int i = 0; i < Data.U16(mat + 2); i++) MaterialData.Add(Mdl3MaterialData.Read(Data, (int)Data.U32(mat + 0x0C) + i * Mdl3MaterialData.Size));
        for (int i = 0; i < Data.U16(mat + 6); i++) TextureInfos.Add(Mdl3TextureInfo.Read(Data, (int)Data.U32(mat + 0x14) + i * Mdl3TextureInfo.Size));

        foreach (var (name, index) in Keys(0x12, 0x34)) ModelNames[index] = name;
        foreach (var (name, index) in Keys(0x16, 0x3C))
        {
            if (!ShapeNames.TryGetValue(index, out var l)) ShapeNames[index] = l = [];
            l.Add(name);
        }
    }

    public static Mdl3File Load(string path) => new(File.ReadAllBytes(path));

    Mdl3Pmsh? pmsh;
    bool pmshRead;

    /// <summary>GT6 packed meshes (header 0xD0), null when the file has none.</summary>
    public Mdl3Pmsh? Pmsh
    {
        get
        {
            if (!pmshRead) { pmsh = Mdl3Pmsh.Read(Data); pmshRead = true; }
            return pmsh;
        }
    }

    /// <summary>8-byte key records: u32 name offset, u32 index.</summary>
    public IEnumerable<(string name, int index)> Keys(int countField, int offsetField)
    {
        int count = Data.U16(countField);
        int at = (int)Data.U32(offsetField);
        for (int i = 0; i < count; i++)
            yield return (Data.CString((int)Data.U32(at + i * 8)), (int)Data.U32(at + i * 8 + 4));
    }
}
