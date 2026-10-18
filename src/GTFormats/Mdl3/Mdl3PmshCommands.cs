namespace GTFormats.Mdl3;

/// <summary>One SmoothMesh command: 5-bit opcode and its operands (as read); Depth = nesting inside op15 lists.</summary>
public sealed record Mdl3PmshCommand(int Opcode, int[] Operands, long BitOffset, int Depth)
{
    public override string ToString() => $"{new string('>', Depth)}op{Opcode}({string.Join(',', Operands)})";
}

/// <summary>
/// SmoothMesh command stream of a PMSH data block (+18, +2C quadwords), MSB-first bits. Opcodes, operand widths and
/// the job state (op7 / op21 / op22 toggles, 0x1C entry counts) from PDTools' SmoothMeshTessellationCommand* (MIT);
/// a list ends with op0, op15 holds a nested list.
/// </summary>
public static class Mdl3PmshCommands
{
    sealed class Reader(byte[] data, int at, int size, int indexBits, List<(int Field0, int Count)> entries1C)
    {
        public long Bit;
        public readonly List<Mdl3PmshCommand> List = [];
        int op7Layout, op22Layout = 1, lastLayout = -1;
        readonly int[] ids1C = [0, 0];
        bool op21Bit1;

        uint B(int n)
        {
            if (Bit + n > size * 8L) throw new InvalidDataException("command stream overrun");
            uint v = 0;
            for (int i = 0; i < n; i++, Bit++) v = (v << 1) | (uint)((data[at + (int)(Bit >> 3)] >> (7 - (int)(Bit & 7))) & 1);
            return v;
        }

        public void ReadList(int depth)
        {
            for (int guard = 0; guard < 100000; guard++)
            {
                long start = Bit;
                int op = (int)B(5);
                var o = new List<int>();
                switch (op)
                {
                    case 0: List.Add(new(op, [], start, depth)); return;
                    case 1: case 2: o.Add((int)B(12)); break;
                    case 7:
                    {
                        int id = (int)B(12);
                        o.Add(id);
                        if (lastLayout != -1) ids1C[op7Layout ^ 1] = lastLayout;
                        lastLayout = id;
                        op7Layout ^= 1;
                        break;
                    }
                    case 5:
                    {
                        o.Add((int)B(12));
                        int nv = (int)B(4), ni = (int)B(4);
                        o.Add(nv); o.Add(ni);
                        for (int i = 0; i < nv + ni; i++) { o.Add((int)B(16)); o.Add((int)B(10)); }
                        break;
                    }
                    case 6: case 8: case 16: break;
                    case 9: { int n = (int)B(6); o.Add(n); for (int i = 0; i < n; i++) { o.Add((int)B(12)); o.Add((int)B(5)); } break; }
                    case 10: { int n = (int)B(8); o.Add(n); o.Add((int)B(12)); for (int i = 0; i <= n; i++) { o.Add((int)B(5)); o.Add((int)B(10)); } break; }
                    case 11: o.Add((int)B(10)); o.Add((int)B(12)); break;
                    case 12: o.Add((int)B(20)); o.Add((int)B(9)); break;
                    case 13:
                        Bit = (start + 13) & ~7L;   // byte aligned after the opcode
                        for (int i = 0; i < 8; i++) o.Add((int)B(32));
                        break;
                    case 14: o.Add((int)B(12)); o.Add((int)B(12)); break;
                    case 15:
                    {
                        o.Add((int)B(32)); o.Add((int)B(13));
                        int bits = (int)B(12);
                        o.Add(bits);
                        List.Add(new(op, [.. o], start, depth));
                        long nested = Bit;
                        ReadList(depth + 1);
                        if (Bit - nested != bits) throw new InvalidDataException($"op15 nested list is {Bit - nested} bits, declared {bits}");
                        continue;
                    }
                    case 17: o.Add((int)B(2)); break;
                    case 21:
                    {
                        int n = (int)B(6), b1 = (int)B(1), extra = (int)B(1);
                        o.Add(n); o.Add(b1); o.Add(extra);
                        op21Bit1 = b1 != 0;
                        if (extra != 0) { Bit = (Bit + 7) & ~7L; B(0); Bit += (n + 1) * 320L; }
                        break;
                    }
                    case 22:
                    {
                        int sw = (int)B(1), n = (int)B(8);
                        o.Add(sw); o.Add(n);
                        if (sw != 0) op22Layout ^= 1;
                        int count = entries1C.Count > ids1C[op22Layout] ? entries1C[ids1C[op22Layout]].Count : 0;
                        int groups = ((n + 1 + 3) & ~3) / 4;
                        for (int i = 0; i < groups * count * 4; i++) o.Add((int)B(indexBits));
                        break;
                    }
                    case 23:
                    {
                        int n = (int)B(6);
                        o.Add(n);
                        for (int i = 0; i <= n; i++) o.Add((int)B(op21Bit1 ? 16 : 12));
                        break;
                    }
                    default:
                        throw new InvalidDataException($"unknown SmoothMesh command {op} at bit {start}");
                }
                List.Add(new(op, [.. o], start, depth));
            }
            throw new InvalidDataException("more than 100000 SmoothMesh commands");
        }
    }

    public static List<Mdl3PmshCommand> Decode(byte[] data, int at, int size, int indexBits, List<(int Field0, int Count)>? entries1C = null)
    {
        var rd = new Reader(data, at, size, indexBits, entries1C ?? []);
        rd.ReadList(0);
        return rd.List;
    }

    /// <summary>
    /// Triangles of a data block in mesh vertex numbers. The job keeps a 2^indexBits-slot vertex ring: op5 (slot,
    /// groups of (first vertex, count - 1)) loads mesh vertices into consecutive slots; op1 n draws 4 n triangles
    /// (groups of 4, the last group padded with (0,0,0)) whose indices (indexBits each, MSB first) are read in
    /// sequence from the index data and name slots.
    /// </summary>
    public static int[] Triangles(byte[] data, Mdl3PmshData d, out List<Mdl3PmshCommand> commands, out long indexBitsUsed,
                                  List<(int Field0, int Count)>? entries1C = null)
    {
        commands = Decode(data, (int)d.Commands, d.CommandQuadwords * 16, d.IndexBits, entries1C);
        var slots = new int[1 << d.IndexBits];
        Array.Fill(slots, -1);
        var tris = new List<int>();
        long bit = 0;
        // op5 loads are double-buffered: the load for the next batch is issued before the current batch draws,
        // and batches still read slots of earlier ones; a queued load takes effect at the next op14 (batch start)
        // An op5 holds at most 15 groups: a longer load continues in the next op5 at the slot where it ended.
        var pending = new List<List<Mdl3PmshCommand>>();
        static int End(Mdl3PmshCommand c)
        {
            int slot = c.Operands[0];
            for (int g = 0; g < c.Operands[1] + c.Operands[2]; g++) slot += c.Operands[4 + g * 2] + 1;
            return slot;
        }
        void Load(List<Mdl3PmshCommand> load)
        {
            foreach (var c in load)
            {
                int slot = c.Operands[0], groups = c.Operands[1] + c.Operands[2];
                for (int g = 0; g < groups; g++)
                {
                    int first = c.Operands[3 + g * 2], n = c.Operands[4 + g * 2] + 1;
                    for (int k = 0; k < n; k++) slots[(slot++) & (slots.Length - 1)] = first + k;
                }
            }
        }
        foreach (var c in commands)
        {
            if (c.Opcode == 5)
            {
                var last = pending.Count > 0 ? pending[^1][^1] : null;
                if (last != null && last.Operands[1] + last.Operands[2] == 15 && (End(last) & (slots.Length - 1)) == c.Operands[0]) pending[^1].Add(c);
                else pending.Add([c]);
            }
            else if (c.Opcode == 14 && pending.Count > 0) { Load(pending[0]); pending.RemoveAt(0); }
            else if (c.Opcode == 1 || c.Opcode == 2)
            {
                // op1: 4 n triangles; op2 (inside op15, SmoothMesh cage): 4 n quads, split into (a,b,c) (a,c,d)
                int corners = c.Opcode == 1 ? 3 : 4;
                for (int t = 0; t < c.Operands[0] * 4; t++)
                {
                    var v = new uint[corners];
                    for (int i = 0; i < corners; i++)
                        for (int b = 0; b < d.IndexBits; b++, bit++)
                            v[i] = (v[i] << 1) | (uint)((data[(int)d.Indices + (int)(bit >> 3)] >> (7 - (int)(bit & 7))) & 1);
                    if (v.All(x => x == v[0])) continue;   // padding of the last group of 4 (all 0)
                    tris.Add(slots[v[0]]); tris.Add(slots[v[1]]); tris.Add(slots[v[2]]);
                    if (corners == 4 && v[3] != v[2] && v[3] != v[0]) { tris.Add(slots[v[0]]); tris.Add(slots[v[2]]); tris.Add(slots[v[3]]); }
                }
            }
        }
        indexBitsUsed = bit;
        return [.. tris];
    }
}
