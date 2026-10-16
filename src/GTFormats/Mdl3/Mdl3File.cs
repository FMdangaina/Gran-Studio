namespace GTFormats.Mdl3;

/// <summary>An MDL3 file loaded in memory: header, models (with their commands), shapes and names.</summary>
public sealed class Mdl3File
{
    public BigEndianReader Data { get; }
    public Mdl3Header Header { get; }
    public List<Mdl3Model> Models { get; } = [];
    public List<Mdl3Shape> Shapes { get; } = [];
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

        foreach (var (name, index) in Keys(0x12, 0x34)) ModelNames[index] = name;
        foreach (var (name, index) in Keys(0x16, 0x3C))
        {
            if (!ShapeNames.TryGetValue(index, out var l)) ShapeNames[index] = l = [];
            l.Add(name);
        }
    }

    public static Mdl3File Load(string path) => new(File.ReadAllBytes(path));

    /// <summary>8-byte key records: u32 name offset, u32 index.</summary>
    public IEnumerable<(string name, int index)> Keys(int countField, int offsetField)
    {
        int count = Data.U16(countField);
        int at = (int)Data.U32(offsetField);
        for (int i = 0; i < count; i++)
            yield return (Data.CString((int)Data.U32(at + i * 8)), (int)Data.U32(at + i * 8 + 4));
    }
}
