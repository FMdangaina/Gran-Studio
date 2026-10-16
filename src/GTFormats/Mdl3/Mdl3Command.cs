namespace GTFormats.Mdl3;

/// <summary>
/// One model setup command (PGL command stream of a model). Operand sizes from Nenkai's PDTools (MIT),
/// checked by `gtdev mdl3-models`: every stream must decode exactly to its size.
/// </summary>
public sealed class Mdl3Command
{
    public int Offset;          // position of the opcode inside the stream
    public byte Opcode;
    public int Length;          // opcode + operands
    public int[] Shapes = [];   // shape indices this command draws
    public int[] Jumps = [];    // absolute stream offsets this command can jump to

    public override string ToString() =>
        $"{Offset:X4} op{Opcode}" + (Shapes.Length > 0 ? $" shapes {string.Join(",", Shapes)}" : "") +
        (Jumps.Length > 0 ? $" -> {string.Join(",", Jumps.Select(j => j.ToString("X4")))}" : "");

    public static List<Mdl3Command> Decode(BigEndianReader r, int start, int size)
    {
        var list = new List<Mdl3Command>();
        int p = start, end = start + size;
        while (p < end)
        {
            var c = new Mdl3Command { Offset = p - start, Opcode = r.U8(p) };
            int q = p + 1;   // operands
            switch (c.Opcode)
            {
                case 0: case 14: case 15: case 24: case 25: case 27: case 28: case 35: case 38: case 39:
                case 40: case 41: case 42: case 44: case 61: case 70: case 71: case 72:
                    break;
                case 3: c.Shapes = [r.U8(q)]; q += 1; break;
                case 4: c.Shapes = [r.U16(q)]; q += 2; break;
                case 59: c.Shapes = [r.U8(q)]; q += 2; break;
                case 60: c.Shapes = [r.U16(q)]; q += 4; break;
                case 5: case 54:
                {
                    int n = r.U8(q + 2); q += 3;
                    c.Jumps = new int[n];
                    for (int i = 0; i < n; i++, q += 2) c.Jumps[i] = q + 2 - start + r.U16(q);
                    break;
                }
                case 9: c.Jumps = [q + 1 - start + r.U8(q)]; q += 1; break;
                case 10: c.Jumps = [q + 2 - start + r.U16(q)]; q += 2; break;
                case 74: case 75:
                {
                    int n = r.U8(q); q += 1;
                    c.Shapes = new int[n];
                    for (int i = 0; i < n; i++, q += 2) c.Shapes[i] = r.U16(q);
                    q += c.Opcode == 74 ? 2 : 4;
                    break;
                }
                case 8:
                {
                    q += 16;
                    int flag = r.U8(q); q += 1;
                    if ((flag & 0x80) != 0)
                    {
                        if ((flag & 0x40) != 0) q += 4;
                        if ((flag & 0x20) != 0) q += 2;
                        if ((flag & 0x10) != 0) q += 1;
                        q += (flag & 0x0F) * 4 + 1;
                    }
                    q += (flag & 0x0F) * 2;
                    break;
                }
                default:
                    int len = OperandSize(c.Opcode);
                    if (len < 0) throw new InvalidDataException($"Unknown model command opcode {c.Opcode} at +{c.Offset:X}");
                    q += len;
                    break;
            }
            c.Length = q - p;
            list.Add(c);
            p = q;
        }
        if (p != end) throw new InvalidDataException($"Command stream overruns its size by {p - end} bytes");
        return list;
    }

    static int OperandSize(byte op) => op switch
    {
        2 or 7 or 45 or 46 or 47 or 49 or 50 or 51 or 52 or 53 or 62 or 65 => 2,
        6 or 11 or 13 or 26 or 34 or 36 or 69 => 1,
        16 or 17 => 64,
        18 or 19 => 12,
        20 or 55 or 56 or 77 => 16,
        21 or 22 or 23 or 43 or 66 or 67 => 4,
        29 => 5,
        37 or 57 or 58 => 8,
        68 => 32,
        _ => -1,
    };
}
