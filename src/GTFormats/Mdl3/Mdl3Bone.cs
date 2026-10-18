using System.Numerics;

namespace GTFormats.Mdl3;

/// <summary>
/// Bone record, 0x48 bytes, in the table at header 0x50 (count at 0x1A); layout as PDTools' MDL3Bone (MIT):
/// 16 floats (matrix), u32 name offset, i16 parent, i16 id.
/// </summary>
public sealed class Mdl3Bone
{
    public const int Size = 0x48;

    public Matrix4x4 Matrix;
    public string Name = "";
    public short Parent;
    public short Id;

    public static Mdl3Bone Read(BigEndianReader r, int at)
    {
        var f = new float[16];
        for (int i = 0; i < 16; i++) f[i] = r.F32(at + i * 4);
        uint name = r.U32(at + 0x40);
        return new Mdl3Bone
        {
            Matrix = new Matrix4x4(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10], f[11], f[12], f[13], f[14], f[15]),
            Name = name != 0 ? r.CString((int)name) : "",
            Parent = r.S16(at + 0x44),
            Id = r.S16(at + 0x46),
        };
    }
}
