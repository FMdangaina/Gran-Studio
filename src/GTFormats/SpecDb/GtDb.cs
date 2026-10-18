using System.Buffers.Binary;
using System.Text;

namespace GTFormats.SpecDb;

/// <summary>
/// One table of a Polyphony "GTDB" spec database (GT5 `specdb\GT5_JP3009\NAME.dbt` + `NAME.idi`): rows keyed by an
/// id with a label from the .idi, fixed-size rows that may be Huffman + template-row compressed (table attribute
/// bit 0). Re-implemented from Nenkai's PDTools.SpecDB (MIT, Copyright (c) Nenkai): DBT_DatabaseTable, Table,
/// IDI_LabelInformation. Read only; only what the car list needs.
/// </summary>
public sealed class GtDbTable
{
    public string Name { get; }
    public bool BigEndian { get; }
    public int RowSize { get; }
    public int RowCount { get; }
    public Dictionary<int, string> Labels { get; } = [];   // id -> label (the .idi can have ids without rows)

    readonly byte[] dbt;
    readonly int attribute, codeBookInfo, dataList, codeList, templateData, dataBlock;

    public GtDbTable(string folder, string name)
    {
        Name = name;
        dbt = File.ReadAllBytes(Path.Combine(folder, name + ".dbt"));
        if (Encoding.ASCII.GetString(dbt, 0, 4) != "GTDB") throw new InvalidDataException($"{name}.dbt: not GTDB");
        BigEndian = BinaryPrimitives.ReadUInt16LittleEndian(dbt.AsSpan(4)) >= 0x100;
        attribute = U16(4);
        RowCount = I32(8);
        RowSize = I32(12);
        int p = 0x10 + RowCount * 8;
        codeBookInfo = p;
        if ((attribute & 1) != 0)
        {
            dataList = p + 8;
            codeList = p + 0x208;
            int t = p + I32(p);
            templateData = t + 8;
            dataBlock = t + I32(t);
        }
        else dataBlock = p;

        var idi = File.ReadAllBytes(Path.Combine(folder, name + ".idi"));
        if (Encoding.ASCII.GetString(idi, 0, 4) != "GTID") throw new InvalidDataException($"{name}.idi: not GTID");
        int I(int at) => BigEndian ? BinaryPrimitives.ReadInt32BigEndian(idi.AsSpan(at)) : BinaryPrimitives.ReadInt32LittleEndian(idi.AsSpan(at));
        int count = I(4);
        for (int i = 0; i < count; i++)
        {
            int labelOffset = I(0x10 + i * 8), id = I(0x10 + i * 8 + 4);
            int s = 0x10 + count * 8 + labelOffset + 2;   // u16 length, then zero-terminated
            int e = Array.IndexOf(idi, (byte)0, s);
            Labels[id] = Encoding.UTF8.GetString(idi, s, e - s);
        }
    }

    int U16(int at) => BigEndian ? BinaryPrimitives.ReadUInt16BigEndian(dbt.AsSpan(at)) : BinaryPrimitives.ReadUInt16LittleEndian(dbt.AsSpan(at));
    int I32(int at) => BigEndian ? BinaryPrimitives.ReadInt32BigEndian(dbt.AsSpan(at)) : BinaryPrimitives.ReadInt32LittleEndian(dbt.AsSpan(at));

    /// <summary>Id of the row at an index (rows are sorted by id; VARIATION repeats an id per colour).</summary>
    public int RowId(int index) => I32(0x10 + index * 8);

    /// <summary>The row bytes at an index (decompressed).</summary>
    public byte[] Row(int index)
    {
        int at = dataBlock + I32(0x10 + index * 8 + 4);
        if ((attribute & 1) == 0) return dbt.AsSpan(at, RowSize).ToArray();
        var entry = Huffman(at);
        int type = entry[0] >> 6, template = entry[0] & 0x3F;
        switch (type)
        {
            case 0: return dbt.AsSpan(templateData + template * RowSize, RowSize).ToArray();
            case 1: return entry.AsSpan(1, RowSize).ToArray();
            case 2:
            {
                // template row patched: one bit per row byte (replace or keep), then the replacement bytes
                var row = dbt.AsSpan(templateData + template * RowSize, RowSize).ToArray();
                int repl = 1 + RowSize / 8 + (RowSize % 8 != 0 ? 1 : 0);
                for (int i = 0; i < RowSize; i++)
                    if ((entry[1 + i / 8] >> (i % 8) & 1) == 1 && repl < entry.Length) row[i] = entry[repl++];
                return row;
            }
            default: throw new InvalidDataException($"{Name}: row {index} compression type {type}");
        }
    }

    /// <summary>Huffman part: first byte = count of decoded bytes, then an LSB-first bit stream of codes.</summary>
    byte[] Huffman(int at)
    {
        int n = dbt[at];
        var outp = new byte[n];
        int bit = 0;
        for (int i = 0; i < n; i++)
        {
            int cur = bit / 8;
            uint v = 0;
            for (int k = 0; k < 4; k++) if (at + 1 + cur + k < dbt.Length) v |= (uint)dbt[at + 1 + cur + k] << (8 * k);
            v >>= bit - cur * 8;
            int lookup = dataList + (byte)v * 2;
            int size = dbt[lookup + 1];
            if (size != 0) outp[i] = dbt[lookup];
            else
            {
                size = LongCode(v, out outp[i]);
                if (size == 0) throw new InvalidDataException($"{Name}: bad Huffman code");
            }
            bit += size;
        }
        return outp;
    }

    /// <summary>Codes longer than 8 bits: binary search of the (size, code) list for each size 9..31.</summary>
    int LongCode(uint bits, out byte value)
    {
        int count = I32(codeBookInfo + 4);
        for (int size = 9; size < 32; size++)
        {
            uint target = bits & ((1u << size) - 1);
            int lo = -1, hi = count;
            while (lo + 1 != hi)
            {
                int mid = (lo + hi) / 2;
                int e = codeList + mid * 8;
                int s = dbt[e];
                uint code = (uint)I32(e + 4);
                if (s == size && code == target) { value = dbt[e + 1]; return size; }
                if (size > s || (size == s && target > code)) lo = mid; else hi = mid;
            }
        }
        value = 0;
        return 0;
    }
}

/// <summary>A "GTST" string database (`UnistrDB.sdb`, `british_StrDB.sdb`): string columns are indices into it.</summary>
public sealed class GtStringDb
{
    public List<string> Strings { get; } = [];

    public GtStringDb(string path)
    {
        var b = File.ReadAllBytes(path);
        if (Encoding.ASCII.GetString(b, 0, 4) != "GTST") throw new InvalidDataException($"{path}: not GTST");
        bool le = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(8)) == 1;
        int I(int at) => le ? BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at)) : BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(at));
        int count = I(4);
        for (int i = 0; i < count; i++)
        {
            int s = 0x10 + count * 4 + I(0x10 + i * 4) + 2;
            int e = Array.IndexOf(b, (byte)0, s);
            Strings.Add(Encoding.UTF8.GetString(b, s, e - s));
        }
    }

    public string this[int i] => i >= 0 && i < Strings.Count ? Strings[i] : "";
}
