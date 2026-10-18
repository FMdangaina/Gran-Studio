using System.Buffers.Binary;
using System.Numerics;

namespace GTFormats.SpecDb;

/// <summary>
/// Salsa20/20 stream cipher (D. J. Bernstein), 256-bit key, 64-bit nonce, 64-bit block counter from 0. GT6 encrypts
/// its SQLite databases with it (specdb\GT6\DB0106.dat, database\gt6\menudb.dat), nonce all zero; keys as published
/// by Nenkai in GTToolsSharp (KeysetStore.cs, MIT) and the Gran Turismo modding guides.
/// </summary>
public static class Salsa20
{
    /// <summary>GT6 spec database key (from scripts/gt6/SpecDatabaseUtil.ad).</summary>
    public static readonly byte[] Gt6SpecDbKey = Convert.FromHexString("2D9EE83E63120EB25DF4981EE73C3BE194D0F059DE50C8D4FCB66C10D3EDC549");
    /// <summary>GT6 menu database key (from boot/boot_functions.ad).</summary>
    public static readonly byte[] Gt6MenuDbKey = Convert.FromHexString("81CE293873B342F4C96E850C977CBF7F9DF674DAC007CA1951B821509F3ADFA4");

    /// <summary>XORs the key stream into the data in place (encrypt = decrypt).</summary>
    public static void Crypt(Span<byte> data, ReadOnlySpan<byte> key, ulong nonce = 0)
    {
        if (key.Length != 32) throw new ArgumentException("Salsa20 key must be 32 bytes", nameof(key));
        Span<uint> input = stackalloc uint[16], x = stackalloc uint[16];
        Span<byte> block = stackalloc byte[64];
        input[0] = 0x61707865; input[5] = 0x3320646E; input[10] = 0x79622D32; input[15] = 0x6B206574;   // "expand 32-byte k"
        for (int i = 0; i < 4; i++)
        {
            input[1 + i] = BinaryPrimitives.ReadUInt32LittleEndian(key[(i * 4)..]);
            input[11 + i] = BinaryPrimitives.ReadUInt32LittleEndian(key[(16 + i * 4)..]);
        }
        input[6] = (uint)nonce; input[7] = (uint)(nonce >> 32);
        ulong counter = 0;
        for (int pos = 0; pos < data.Length; pos += 64, counter++)
        {
            input[8] = (uint)counter; input[9] = (uint)(counter >> 32);
            input.CopyTo(x);
            for (int r = 0; r < 10; r++)
            {
                QR(x, 0, 4, 8, 12); QR(x, 5, 9, 13, 1); QR(x, 10, 14, 2, 6); QR(x, 15, 3, 7, 11);     // columns
                QR(x, 0, 1, 2, 3); QR(x, 5, 6, 7, 4); QR(x, 10, 11, 8, 9); QR(x, 15, 12, 13, 14);     // rows
            }
            for (int i = 0; i < 16; i++) BinaryPrimitives.WriteUInt32LittleEndian(block[(i * 4)..], x[i] + input[i]);
            int n = Math.Min(64, data.Length - pos);
            for (int i = 0; i < n; i++) data[pos + i] ^= block[i];
        }
    }

    static void QR(Span<uint> x, int a, int b, int c, int d)
    {
        x[b] ^= BitOperations.RotateLeft(x[a] + x[d], 7);
        x[c] ^= BitOperations.RotateLeft(x[b] + x[a], 9);
        x[d] ^= BitOperations.RotateLeft(x[c] + x[b], 13);
        x[a] ^= BitOperations.RotateLeft(x[d] + x[c], 18);
    }
}
