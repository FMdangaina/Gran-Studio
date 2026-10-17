using System.Numerics;

namespace GTFormats.Mdl3;

/// <summary>Attribute of a packed vertex stream: 4 bytes (u8 id, u8 type &lt;&lt; 4 | count, u16 byte offset).</summary>
/// <remarks>Ids: 1 position, 2 normal, 5 texcoord, 0x90/0x91 extra sets. Types: 0 F32, 1 F16, 2 I16N, 4 X11Y11Z10N, 5 U8N.</remarks>
public sealed record Mdl3PackedAttribute(int Id, int Type, int Count, int Offset)
{
    public int ByteSize => Type switch { 0 => 4 * Count, 1 or 2 or 3 => 2 * Count, 4 => 4, 5 or 6 => Count, _ => -1 };
}

/// <summary>Vertex layout block: u16 attribute count, u16 stride, attributes.</summary>
public sealed class Mdl3PackedLayout
{
    public int Stride;
    public List<Mdl3PackedAttribute> Attributes = [];

    public Mdl3PackedAttribute? this[int id] => Attributes.FirstOrDefault(a => a.Id == id);

    public static Mdl3PackedLayout Read(BigEndianReader r, ref int at)
    {
        var l = new Mdl3PackedLayout { Stride = r.U16(at + 2) };
        int n = r.U16(at);
        for (int i = 0; i < n; i++)
        {
            int a = at + 4 + i * 4;
            l.Attributes.Add(new(r.U8(a), r.U8(a + 1) >> 4, r.U8(a + 1) & 0xF, r.U16(a + 2)));
        }
        at += 4 + 4 * n;
        return l;
    }
}

/// <summary>
/// Stream description of one packed shape: GT5 geometry block (header 0x94) third table, 0x20-byte records
/// (+04 size, +08 offset of the description). Description: u16 size, u16 flag (8: input + output layouts, 4: one layout),
/// [flag 8: u16 output stride, u16 0], layouts, then optionally the car position frame (6 floats).
/// </summary>
public sealed class Mdl3PackedStreamDesc
{
    public int Flag;
    public Mdl3PackedLayout Input = new(), Output = new();
    public float[]? Frame;      // F.xyz, G.xyz: position = grid * F / 2^21 + (F + G)

    public static Mdl3PackedStreamDesc Read(BigEndianReader r, int index)
    {
        int g = (int)r.U32(0x94);
        int rec = (int)r.U32(g + 0x18) + index * 0x20;
        int size = (int)r.U32(rec + 4), start = (int)r.U32(rec + 8);
        var d = new Mdl3PackedStreamDesc { Flag = r.U16(start + 2) };
        int at;
        if (d.Flag == 8)
        {
            at = start + 8;
            d.Input = Mdl3PackedLayout.Read(r, ref at);
            d.Output = Mdl3PackedLayout.Read(r, ref at);
        }
        else
        {
            at = start + 4;
            d.Input = d.Output = Mdl3PackedLayout.Read(r, ref at);
        }
        if (start + size - at >= 24) d.Frame = [.. Enumerable.Range(0, 6).Select(i => r.F32(at + i * 4))];
        return d;
    }
}

/// <summary>One packed segment (at most a few hundred vertices, PS3 Edge style).</summary>
public sealed class Mdl3PackedSegment
{
    public int Offset;                  // segment record
    public int VertexCount, TriangleCount;
    public int IndexOffset, IndexSize;
    public int[] Streams = [];          // vertex stream offsets (S+0x20 byte 0 = count)
    public int PositionFormatSize;      // 0: positions are F32 in stream 0; 0x10/0x34: fixed point in the last stream
    public int[] PositionBits = [0, 0, 0];
    public int[] GridOffset = [0, 0, 0];    // format +28/+2C/+30 >> 2: added to the raw values (16-bit car grid)
    public int[] LodVertexCounts = [];      // progressive LODs (S+0x22 entries): vertices of each level, LOD0 = all
    public int[] LodIndexInfos = [];        // index info record of each level
    public int MorphStreamCount;            // S+0x23, S+0x08 -> list (LOD count - 1 when LODs are present; open)

    /// <summary>Index data of one LOD level (LOD0 = <see cref="IndexOffset"/>).</summary>
    public Mdl3PackedSegment Lod(BigEndianReader r, int level)
    {
        int info = LodIndexInfos[level];
        return new Mdl3PackedSegment
        {
            Offset = Offset, Streams = Streams, PositionFormatSize = PositionFormatSize, PositionBits = PositionBits,
            GridOffset = GridOffset, LodVertexCounts = LodVertexCounts, LodIndexInfos = LodIndexInfos, MorphStreamCount = MorphStreamCount,
            VertexCount = LodVertexCounts[level], TriangleCount = r.U16(info), IndexSize = r.U16(info + 4), IndexOffset = (int)r.U32(info + 8),
        };
    }
}

/// <summary>
/// GT5 (MDL3 v8) packed shape: shape +2C -> 0x40 record, +30 -> header H (+04 -> L, +0C u16 segment count);
/// L +04 -> M (u16 1, u16 stream description index, u16, u16 segment count, +08 -> segment pointer list).
/// Segment record S: +00 -> stream offset list (8 bytes each), +04 -> index info (u16 triangles, u16 0, u16 size, u16 0, u32 offset),
/// +20 u8 stream count, +24 -> u32 vertex count, +28 -> position format, +2C u16 position format size.
/// Position format: +0C..+0E bits per axis, +28/+2C/+30 grid offsets x4.
/// Found by `gtdev mdl3-packed`; layout of the stream descriptions as in the RSX attribute codes.
/// </summary>
public sealed class Mdl3PackedShape
{
    public int StreamDescIndex;
    public Mdl3PackedStreamDesc Desc = new();
    public List<Mdl3PackedSegment> Segments = [];

    /// <summary>True for a GT5 packed shape (no vertex buffer, a packed reference with a segment header).</summary>
    public static bool IsPacked(BigEndianReader r, Mdl3Shape s) =>
        s.VerticesOffset == 0 && s.PackedRefOffset != 0 && r.U32((int)s.PackedRefOffset + 0x30) != 0;

    public static Mdl3PackedShape Read(BigEndianReader r, Mdl3Shape s)
    {
        int h = (int)r.U32((int)s.PackedRefOffset + 0x30);
        int l = (int)r.U32(h + 4), m = (int)r.U32(l + 4), list = (int)r.U32(m + 8);
        var p = new Mdl3PackedShape { StreamDescIndex = r.U16(m + 2) };
        p.Desc = Mdl3PackedStreamDesc.Read(r, p.StreamDescIndex);
        int count = r.U16(h + 0x0C);
        for (int k = 0; k < count; k++)
        {
            int at = (int)r.U32(list + k * 4);
            int info = (int)r.U32(at + 4), streams = (int)r.U32(at), counts = (int)r.U32(at + 0x24);
            int lods = r.U8(at + 0x22);
            var seg = new Mdl3PackedSegment
            {
                Offset = at,
                Streams = [.. Enumerable.Range(0, r.U8(at + 0x20)).Select(i => (int)r.U32(streams + i * 8))],
                PositionFormatSize = r.U16(at + 0x2C),
                // count block: one 8-byte entry per LOD (u16 kept, u16 added, u32 LOD count); LOD k uses the first kept + added vertices
                LodVertexCounts = [.. Enumerable.Range(0, lods).Select(i => r.U16(counts + i * 8) + r.U16(counts + i * 8 + 2))],
                // index info: one 0x10 entry per LOD (u16 triangles, u16 0, u16 size, u16 0, u32 offset, u32 -1)
                LodIndexInfos = [.. Enumerable.Range(0, lods).Select(i => info + i * 0x10)],
                MorphStreamCount = r.U8(at + 0x23),
            };
            seg.VertexCount = seg.LodVertexCounts[0];
            seg.TriangleCount = r.U16(info);
            seg.IndexSize = r.U16(info + 4);
            seg.IndexOffset = (int)r.U32(info + 8);
            if (seg.PositionFormatSize != 0)
            {
                int fmt = (int)r.U32(at + 0x28);
                seg.PositionBits = [r.U8(fmt + 0x0C), r.U8(fmt + 0x0D), r.U8(fmt + 0x0E)];
                if (r.U16(fmt) >= 0x34) seg.GridOffset = [.. Enumerable.Range(0, 3).Select(i => (int)(r.U32(fmt + 0x28 + i * 4) >> 2))];
            }
            p.Segments.Add(seg);
        }
        return p;
    }

    /// <summary>The car position frame: the first stream description in the file that carries one (null if none).</summary>
    public static float[]? CarFrame(BigEndianReader r)
    {
        int g = (int)r.U32(0x94);
        if (g == 0) return null;
        for (int i = 0; i < r.U16(g + 4); i++)
            if (Mdl3PackedStreamDesc.Read(r, i).Frame is { } f) return f;
        return null;
    }

    /// <summary>16-bit grid values (raw + offset) of a fixed-point position stream; <paramref name="end"/> = first byte after the bits.</summary>
    public static int[,] Grid(BigEndianReader r, Mdl3PackedSegment seg, out int end)
    {
        var bits = new BitReader(r.Data, seg.Streams[^1]);
        var g = new int[seg.VertexCount, 3];
        for (int v = 0; v < seg.VertexCount; v++)
            for (int a = 0; a < 3; a++)
                g[v, a] = (int)bits.Read(seg.PositionBits[a]) + seg.GridOffset[a];
        end = bits.BytePosition;
        return g;
    }

    public Vector3[] Positions(BigEndianReader r, Mdl3PackedSegment seg, float[]? frame)
    {
        var p = new Vector3[seg.VertexCount];
        if (seg.PositionFormatSize != 0)
        {
            if (frame == null) throw new InvalidDataException("fixed-point positions but no car frame");
            var scale = new Vector3(frame[0], frame[1], frame[2]) / (1 << 21);
            var bias = new Vector3(frame[0] + frame[3], frame[1] + frame[4], frame[2] + frame[5]);
            var g = Grid(r, seg, out _);
            for (int v = 0; v < p.Length; v++) p[v] = new Vector3(g[v, 0], g[v, 1], g[v, 2]) * scale + bias;
            return p;
        }
        var a = Desc.Input[1] ?? throw new InvalidDataException("no position attribute");
        for (int v = 0; v < p.Length; v++) p[v] = Attribute(r, seg, a, v);
        return p;
    }

    /// <summary>One input attribute of vertex v from stream 0 (up to 3 components).</summary>
    public Vector3 Attribute(BigEndianReader r, Mdl3PackedSegment seg, Mdl3PackedAttribute a, int v)
    {
        int at = seg.Streams[0] + v * Desc.Input.Stride + a.Offset;
        float C(int i) => i >= a.Count ? 0 : a.Type switch
        {
            0 => r.F32(at + i * 4),
            1 => (float)r.F16(at + i * 2),
            2 => r.S16(at + i * 2) / 32767f,
            3 => r.S16(at + i * 2),
            5 => r.U8(at + i) / 255f,
            6 => r.U8(at + i),
            _ => 0,
        };
        if (a.Type == 4)
        {
            uint u = r.U32(at);
            return new Vector3(((int)(u << 21) >> 21) / 1023f, ((int)(u << 10) >> 21) / 1023f, ((int)u >> 22) / 511f);
        }
        return new Vector3(C(0), C(1), C(2));
    }

    /// <summary>
    /// Compressed triangles: header u16 bits, u16 value count, u16 value count again, u16 0; the values (vertex indices,
    /// <c>bits</c> each, MSB first); then from the next 2-byte boundary one 2-bit code per triangle:
    /// 3 = three new values, 0 = (a, c, new) and 1 = (c, b, new) from the previous triangle (a, b, c). 2 is never used.
    /// </summary>
    public static int[] Triangles(BigEndianReader r, Mdl3PackedSegment seg, out int valueCount, out int valuesUsed, out int end)
    {
        if (seg.TriangleCount == 0 && seg.IndexOffset == 0) { valueCount = valuesUsed = end = 0; return []; }   // empty last LOD
        int at = seg.IndexOffset, bitsPer = r.U16(at);
        valueCount = r.U16(at + 2);
        var values = new BitReader(r.Data, at + 8);
        var vals = new int[valueCount];
        for (int i = 0; i < valueCount; i++) vals[i] = (int)values.Read(bitsPer);
        int codesAt = values.BytePosition;
        codesAt += (codesAt - at) & 1;
        var codes = new BitReader(r.Data, codesAt);
        var tris = new int[seg.TriangleCount * 3];
        int next = 0, a = 0, b = 0, c = 0;
        for (int t = 0; t < seg.TriangleCount; t++)
        {
            switch (codes.Read(2))
            {
                case 3: a = vals[next++]; b = vals[next++]; c = vals[next++]; break;
                case 0: b = c; c = vals[next++]; break;     // (a, c, new)
                case 1: a = c; c = vals[next++]; break;     // (c, b, new)
                default: throw new InvalidDataException("index code 2");
            }
            tris[t * 3] = a; tris[t * 3 + 1] = b; tris[t * 3 + 2] = c;
        }
        valuesUsed = next;
        end = codes.BytePosition;
        return tris;
    }
}

/// <summary>MSB-first bit reader.</summary>
public sealed class BitReader(byte[] data, int start)
{
    int pos = start, bitsLeft;
    ulong acc;

    public int BytePosition => pos;

    public uint Read(int n)
    {
        if (n == 0) return 0;
        while (bitsLeft < n) { acc = (acc << 8) | data[pos++]; bitsLeft += 8; }
        bitsLeft -= n;
        uint v = (uint)(acc >> bitsLeft) & (uint)((1UL << n) - 1);
        acc &= (1UL << bitsLeft) - 1;
        return v;
    }
}
