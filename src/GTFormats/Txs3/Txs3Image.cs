using System.Numerics;

namespace GTFormats.Txs3;

/// <summary>
/// TXS3 image record, 0x20 bytes (verified on every car/interior/wheel/tire file, `gtdev txs3-survey`):
/// +00 pixel offset (absolute in the MDL3; 0 in GT6 car bodies = pixels elsewhere), +04 size,
/// +08 cube (FORMAT bit 2), +09 format (CELL_GCM_TEXTURE_*, 0x20 = linear), +0A mipmap count,
/// +0B location (1 local / 2 main, like the FORMAT register in all but a few shared images),
/// +0C width, +0E height, +10 depth (3D slices; dimension 3), +12..+1F zero.
/// </summary>
public sealed class Txs3Image
{
    public const int RecordSize = 0x20;

    public uint Offset, Size;
    public bool Cube;
    public int Format, Mipmaps, Location, Width, Height, Depth;

    public bool Linear => (Format & 0x20) != 0;
    public int BaseFormat => Format & ~0x60;
    public int Faces => Cube ? 6 : 1;

    public static Txs3Image Read(BigEndianReader r, int at) => new()
    {
        Offset = r.U32(at),
        Size = r.U32(at + 4),
        Cube = r.U8(at + 8) != 0,
        Format = r.U8(at + 9),
        Mipmaps = r.U8(at + 0x0A),
        Location = r.U8(at + 0x0B),
        Width = r.U16(at + 0x0C),
        Height = r.U16(at + 0x0E),
        Depth = r.U16(at + 0x10),
    };

    /// <summary>The image records of the TXS3 block at <paramref name="txs"/> (MDL3 header 0x48).</summary>
    public static List<Txs3Image> ReadAll(BigEndianReader r, int txs)
    {
        int count = r.U16(txs + 0x16), at = (int)r.U32(txs + 0x1C);
        return Enumerable.Range(0, count).Select(i => Read(r, at + i * RecordSize)).ToList();
    }

    public static int BytesPerPixel(int baseFormat) => baseFormat switch
    {
        0x81 => 1, 0x85 or 0x95 or 0x9C or 0x9E or 0x9F => 4, 0x9A => 8, 0x9B => 16,
        0x82 or 0x83 or 0x84 or 0x8B or 0x8F or 0x94 or 0x97 or 0x98 or 0x99 or 0x9D or 0xAD or 0xAE => 2,
        _ => 0,
    };

    public static bool IsDxt(int baseFormat) => baseFormat is 0x86 or 0x87 or 0x88;

    /// <summary>Bytes of one mip level; linear rows use <paramref name="pitch"/> when it is > 0.</summary>
    public long LevelSize(int level, int pitch = 0)
    {
        int w = Math.Max(1, Width >> level), h = Math.Max(1, Height >> level);
        if (IsDxt(BaseFormat)) return (long)((w + 3) / 4) * ((h + 3) / 4) * (BaseFormat == 0x86 ? 8 : 16);
        return pitch > 0 ? (long)pitch * h : (long)w * h * BytesPerPixel(BaseFormat);
    }

    /// <summary>Bytes of one cube face: all mip levels of all depth slices, padded to 128.</summary>
    public long FaceSize(int pitch = 0)
    {
        long s = 0;
        for (int m = 0; m < Mipmaps; m++) s += LevelSize(m, pitch);
        return (s * Depth + 127) / 128 * 128;
    }

    /// <summary>Row pitch of a linear image: the texture info pitch (CONTROL3), the same on every mip level; 0 if not linear.</summary>
    public int RowPitch(int texInfoPitch) =>
        !Linear || IsDxt(BaseFormat) ? 0 : texInfoPitch > 0 ? texInfoPitch : Width * BytesPerPixel(BaseFormat);

    /// <summary>
    /// Record size == computed size. Linear images: rows at the pitch on every level; mipmapped non-power-of-2 ones
    /// are allocated as if the pitch were that of the next power-of-2 width (the pixels still use the real pitch).
    /// </summary>
    public bool SizeMatches(int texInfoPitch)
    {
        if (!Linear || IsDxt(BaseFormat)) return FaceSize() * Faces == Size;
        int alloc = (int)BitOperations.RoundUpToPowerOf2((uint)Width) * BytesPerPixel(BaseFormat);
        return FaceSize(RowPitch(texInfoPitch)) * Faces == Size || FaceSize(alloc) * Faces == Size;
    }

    /// <summary>
    /// Decodes mip level 0 of every face and slice to RGBA8, stacked top to bottom (face 0 slice 0, slice 1, ..., face 1 ...).
    /// <paramref name="pixels"/> holds the image bytes (<see cref="Size"/> long). Returns null for formats not handled.
    /// </summary>
    public byte[]? DecodeLevel0(ReadOnlySpan<byte> pixels, int texInfoPitch = 0)
    {
        int w = Width, h = Height, slices = Faces * Depth;
        var rgba = new byte[(long)w * h * 4 * slices];
        int pitch = RowPitch(texInfoPitch);
        long faceSize = Size / Faces;
        long sliceSize = LevelSize(0, pitch);
        for (int f = 0; f < Faces; f++)
        {
            var face = pixels.Slice((int)(f * faceSize));
            var dst = rgba.AsSpan((int)((long)f * Depth * w * h * 4));
            if (IsDxt(BaseFormat))
                for (int z = 0; z < Depth; z++)
                    Dxt.Decode(BaseFormat, face.Slice((int)(z * sliceSize)), w, h, dst.Slice(z * w * h * 4));
            else if (BaseFormat == 0x85) // A8R8G8B8: bytes B,G,R,A (remap 0xAA1B)
                DecodeArgb(face, w, h, Depth, pitch, dst);
            else return null;
        }
        return rgba;
    }

    void DecodeArgb(ReadOnlySpan<byte> src, int w, int h, int depth, int pitch, Span<byte> dst)
    {
        long n = (long)w * h * depth;
        for (long i = 0; i < n; i++)
        {
            long s;
            if (Linear) { long z = i / (w * h), y = i / w % h, x = i % w; s = z * pitch * h + y * pitch + x * 4; }
            else s = 4L * Morton(i, w, h, depth);
            dst[(int)(i * 4 + 0)] = src[(int)s + 2];
            dst[(int)(i * 4 + 1)] = src[(int)s + 1];
            dst[(int)(i * 4 + 2)] = src[(int)s + 0];
            dst[(int)(i * 4 + 3)] = src[(int)s + 3];
        }
    }

    /// <summary>
    /// RSX swizzle: index of linear pixel <paramref name="i"/> (x + y*w + z*w*h) in the swizzled data,
    /// bits of x, y, z interleaved (x first) while each dimension still has bits left.
    /// </summary>
    public static long Morton(long i, int w, int h, int d)
    {
        long x = i % w, y = i / w % h, z = i / ((long)w * h);
        long result = 0; int bit = 0;
        for (int b = 0; (1 << b) < Math.Max(w, Math.Max(h, d)); b++)
        {
            if ((1 << b) < w) result |= ((x >> b) & 1) << bit++;
            if ((1 << b) < h) result |= ((y >> b) & 1) << bit++;
            if ((1 << b) < d) result |= ((z >> b) & 1) << bit++;
        }
        return result;
    }
}
