using System.Buffers.Binary;

namespace GTFormats.Txs3;

/// <summary>
/// DXT1 / DXT2-3 / DXT4-5 (BC1/BC2/BC3) block decoding to RGBA8. RSX stores the blocks as on PC:
/// little-endian 16-bit colours and indices, blocks in row order (DXT is never swizzled).
/// </summary>
public static class Dxt
{
    /// <param name="format">CELL_GCM base format: 0x86 DXT1, 0x87 DXT23, 0x88 DXT45.</param>
    public static void Decode(int format, ReadOnlySpan<byte> src, int w, int h, Span<byte> rgba)
    {
        int bw = (w + 3) / 4, bh = (h + 3) / 4, blockSize = format == 0x86 ? 8 : 16;
        Span<byte> block = stackalloc byte[64];
        for (int by = 0; by < bh; by++)
            for (int bx = 0; bx < bw; bx++)
            {
                var b = src.Slice((by * bw + bx) * blockSize, blockSize);
                switch (format)
                {
                    case 0x86: Colour(b, block, true); break;
                    case 0x87: Colour(b[8..], block, false); AlphaExplicit(b, block); break;
                    default: Colour(b[8..], block, false); AlphaInterpolated(b, block); break;
                }
                for (int py = 0; py < 4; py++)
                    for (int px = 0; px < 4; px++)
                    {
                        int x = bx * 4 + px, y = by * 4 + py;
                        if (x >= w || y >= h) continue;
                        block.Slice((py * 4 + px) * 4, 4).CopyTo(rgba.Slice((y * w + x) * 4));
                    }
            }
    }

    static void Colour(ReadOnlySpan<byte> b, Span<byte> outBlock, bool dxt1)
    {
        ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(b), c1 = BinaryPrimitives.ReadUInt16LittleEndian(b[2..]);
        Span<int> pal = stackalloc int[16];
        Rgb565(c0, pal[..4]); Rgb565(c1, pal.Slice(4, 4));
        bool fourColour = !dxt1 || c0 > c1;
        for (int k = 0; k < 3; k++)
        {
            pal[8 + k] = fourColour ? (2 * pal[k] + pal[4 + k]) / 3 : (pal[k] + pal[4 + k]) / 2;
            pal[12 + k] = fourColour ? (pal[k] + 2 * pal[4 + k]) / 3 : 0;
        }
        pal[3] = pal[7] = pal[11] = 255;
        pal[15] = fourColour ? 255 : 0;
        uint idx = BinaryPrimitives.ReadUInt32LittleEndian(b[4..]);
        for (int p = 0; p < 16; p++)
        {
            int c = (int)((idx >> (2 * p)) & 3) * 4;
            for (int k = 0; k < 4; k++) outBlock[p * 4 + k] = (byte)pal[c + k];
        }
    }

    static void Rgb565(ushort c, Span<int> o)
    {
        int r = c >> 11, g = (c >> 5) & 0x3F, bl = c & 0x1F;
        o[0] = (r << 3) | (r >> 2); o[1] = (g << 2) | (g >> 4); o[2] = (bl << 3) | (bl >> 2); o[3] = 255;
    }

    static void AlphaExplicit(ReadOnlySpan<byte> b, Span<byte> outBlock)
    {
        ulong a = BinaryPrimitives.ReadUInt64LittleEndian(b);
        for (int p = 0; p < 16; p++) outBlock[p * 4 + 3] = (byte)(((a >> (4 * p)) & 0xF) * 17);
    }

    static void AlphaInterpolated(ReadOnlySpan<byte> b, Span<byte> outBlock)
    {
        int a0 = b[0], a1 = b[1];
        Span<int> pal = stackalloc int[8];
        pal[0] = a0; pal[1] = a1;
        if (a0 > a1) for (int k = 1; k < 7; k++) pal[k + 1] = ((7 - k) * a0 + k * a1) / 7;
        else { for (int k = 1; k < 5; k++) pal[k + 1] = ((5 - k) * a0 + k * a1) / 5; pal[6] = 0; pal[7] = 255; }
        ulong bits = 0;
        for (int k = 0; k < 6; k++) bits |= (ulong)b[2 + k] << (8 * k);
        for (int p = 0; p < 16; p++) outBlock[p * 4 + 3] = (byte)pal[(int)((bits >> (3 * p)) & 7)];
    }
}
