using System.Numerics;

namespace GTFormats.Mdl3;

/// <summary>
/// Model record, 0x30 bytes, in the table at header 0x30 (count at 0x10).
/// A model is a group of shapes (e.g. GTBE_BODY); its command block says which shapes are drawn and how.
/// </summary>
public sealed class Mdl3Model
{
    public const int Size = 0x30;

    public float Radius;            // +00 bounding sphere radius (PDTools: "Unk")
    public Vector3 Center;          // +04 bounding sphere center
    public ushort Unk10;            // +10 always 0 in car files
    public Vector3[] Bounds = [];   // +12 count (8 = box corners), +14 offset
    public uint CommandsOffset;     // +18
    public uint CommandsSize;       // +1C
    public int VmInit, VmUpdate, VmUnk;  // +20/+24/+28 VM instruction pointers, -1 when unused
    public ushort Unk2C;            // +2C
    public ushort Flags;            // +2E
    public List<Mdl3Command> Commands = [];

    public static Mdl3Model Read(BigEndianReader r, int at)
    {
        var m = new Mdl3Model
        {
            Radius = r.F32(at),
            Center = new Vector3(r.F32(at + 4), r.F32(at + 8), r.F32(at + 0x0C)),
            Unk10 = r.U16(at + 0x10),
            CommandsOffset = r.U32(at + 0x18),
            CommandsSize = r.U32(at + 0x1C),
            VmInit = r.S32(at + 0x20),
            VmUpdate = r.S32(at + 0x24),
            VmUnk = r.S32(at + 0x28),
            Unk2C = r.U16(at + 0x2C),
            Flags = r.U16(at + 0x2E),
        };
        int boundsCount = r.U16(at + 0x12);
        uint boundsOffset = r.U32(at + 0x14);
        m.Bounds = new Vector3[boundsCount];
        for (int i = 0; i < boundsCount; i++)
        {
            int b = (int)boundsOffset + i * 12;
            m.Bounds[i] = new Vector3(r.F32(b), r.F32(b + 4), r.F32(b + 8));
        }
        m.Commands = Mdl3Command.Decode(r, (int)m.CommandsOffset, (int)m.CommandsSize);
        return m;
    }
}
