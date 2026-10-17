using System.IO.Compression;

namespace GTFormats.Mdl3;

/// <summary>
/// v14 header 0xDC: map of the GT6 car <c>body_s</c> companion file (verified with `gtdev body-s`).
/// Header 0x20: u32 group count, u16, u16, u32 groups. Group 0x0C: u32 name (`/`, `custom_brake0`, ...), u32 n, u32 items.
/// Item 0x18: a stream (0x10) + u32 m, u32 sub streams (m x 0x10).
/// Stream 0x10: u32 0, u32 offset in body_s, u16 header chunk count, u16 data chunk count, u32 chunk list.
/// Chunk 8 bytes: u32 stored size (bit 31 = raw deflate, size in the low 16 bits), u32 size once inflated.
/// The chunks of a stream are contiguous in body_s from the stream offset (header chunks first) and form one bundle.
/// </summary>
public sealed class Mdl3StreamMap
{
    public sealed record Chunk(bool Deflated, int StoredSize, int Size);

    public sealed class Stream
    {
        public uint Offset;
        public List<Chunk> HeaderChunks = [], DataChunks = [];
        public int StoredSize => HeaderChunks.Concat(DataChunks).Sum(c => c.StoredSize);
        public int HeaderSize => HeaderChunks.Sum(c => c.Size);
        public int DataSize => DataChunks.Sum(c => c.Size);

        /// <summary>The bundle bytes (header chunks then data chunks, inflated) read from <paramref name="bodyS"/>.</summary>
        public byte[] Load(byte[] bodyS)
        {
            var bundle = new byte[HeaderSize + DataSize];
            int src = (int)Offset, dst = 0;
            foreach (var c in HeaderChunks.Concat(DataChunks))
            {
                if (!c.Deflated) Array.Copy(bodyS, src, bundle, dst, c.Size);
                else
                {
                    using var z = new DeflateStream(new MemoryStream(bodyS, src, c.StoredSize), CompressionMode.Decompress);
                    z.ReadExactly(bundle, dst, c.Size);
                    if (z.ReadByte() >= 0) throw new InvalidDataException($"body_s chunk at 0x{src:X} inflates past 0x{c.Size:X}");
                }
                src += c.StoredSize; dst += c.Size;
            }
            return bundle;
        }
    }

    public sealed record Item(Stream Stream, List<Stream> Subs);
    public sealed record Group(string Name, List<Item> Items);

    public List<Group> Groups { get; } = [];

    /// <summary>Every stream: items and their sub streams, with the group name.</summary>
    public IEnumerable<(string group, int item, int sub, Stream stream)> Streams()
    {
        foreach (var g in Groups)
            for (int i = 0; i < g.Items.Count; i++)
            {
                yield return (g.Name, i, -1, g.Items[i].Stream);
                for (int k = 0; k < g.Items[i].Subs.Count; k++) yield return (g.Name, i, k, g.Items[i].Subs[k]);
            }
    }

    /// <summary>
    /// Where each TXS3 image of the body is in <paramref name="bodyS"/>: one entry per bundle that holds it
    /// (several = variants: paint colours in the `/` sub streams, custom parts in the `custom_*` groups).
    /// </summary>
    public Dictionary<int, List<(string where, byte[] bundle, int offset)>> FindImages(byte[] bodyS)
    {
        var found = new Dictionary<int, List<(string, byte[], int)>>();
        foreach (var (g, item, sub, s) in Streams())
        {
            var b = new Mdl3StreamBundle(s.Load(bodyS));
            foreach (var (img, off) in b.Images())
            {
                if (!found.TryGetValue(img, out var l)) found[img] = l = [];
                l.Add(($"{g}[{item}]{(sub >= 0 ? "." + sub : "")}", b.Bytes, off));
            }
        }
        return found;
    }

    /// <summary>The map of a v14 file, or null if header 0xDC is 0.</summary>
    public static Mdl3StreamMap? Read(BigEndianReader r)
    {
        int h = (int)r.U32(0xDC);
        if (h == 0) return null;
        var map = new Mdl3StreamMap();
        int groups = (int)r.U32(h), ga = (int)r.U32(h + 8);
        for (int i = 0; i < groups; i++)
        {
            int g = ga + i * 0x0C, n = (int)r.U32(g + 4), ia = (int)r.U32(g + 8);
            uint name = r.U32(g);
            var items = new List<Item>();
            for (int j = 0; j < n; j++)
            {
                int it = ia + j * 0x18, m = (int)r.U32(it + 0x10), sa = (int)r.U32(it + 0x14);
                items.Add(new Item(ReadStream(r, it), [.. Enumerable.Range(0, m).Select(k => ReadStream(r, sa + k * 0x10))]));
            }
            map.Groups.Add(new Group(name == 0 ? "" : r.CString((int)name), items));
        }
        return map;
    }

    static Stream ReadStream(BigEndianReader r, int s)
    {
        var st = new Stream { Offset = r.U32(s + 4) };
        int c1 = r.U16(s + 8), c2 = r.U16(s + 0x0A), list = (int)r.U32(s + 0x0C);
        for (int i = 0; i < c1 + c2; i++)
        {
            uint a = r.U32(list + i * 8);
            var c = new Chunk((a & 0x80000000) != 0, (int)(a & 0x7FFFFFFF), (int)r.U32(list + i * 8 + 4));
            (i < c1 ? st.HeaderChunks : st.DataChunks).Add(c);
        }
        return st;
    }
}

/// <summary>
/// One inflated <c>body_s</c> bundle (header + data). Header (big-endian, its size = the header chunks):
/// +00 0, +04 header size, +08 bundle size, +0C data size, +10 0, +14 u16 shape count, +16 u16 image count,
/// +18 shape table (0x20 records, shape index first), +1C image table (0x10 records: u32 TXS3 image index,
/// u32 pixel offset in the bundle, 8 zero bytes). The pixels of the images are in the data part.
/// </summary>
public sealed class Mdl3StreamBundle
{
    public byte[] Bytes { get; }
    public BigEndianReader R { get; }
    public int HeaderSize => (int)R.U32(4);
    public int TotalSize => (int)R.U32(8);
    public int DataSize => (int)R.U32(0x0C);
    public int ShapeCount => R.U16(0x14);
    public int ImageCount => R.U16(0x16);
    public int ShapeTable => (int)R.U32(0x18);
    public int ImageTable => (int)R.U32(0x1C);

    public Mdl3StreamBundle(byte[] bytes) { Bytes = bytes; R = new BigEndianReader(bytes); }

    /// <summary>(TXS3 image index, pixel offset in the bundle) of every image in this bundle.</summary>
    public IEnumerable<(int image, int offset)> Images() =>
        Enumerable.Range(0, ImageCount).Select(i => ((int)R.U32(ImageTable + i * 0x10), (int)R.U32(ImageTable + i * 0x10 + 4)));

    /// <summary>Shape index of every shape record.</summary>
    public IEnumerable<int> Shapes() => Enumerable.Range(0, ShapeCount).Select(i => (int)R.U32(ShapeTable + i * 0x20));
}
