using GTFormats;

/// <summary>
/// Minimal RSX (NV40) fragment program disassembler, research only: opcode, destination, sources (temp / input /
/// inline constant), texture unit. Words are stored with their 16-bit halves swapped. Field layout from the public
/// nouveau nvfx shader headers (NV40 fragment program).
/// </summary>
static class RsxFp
{
    static readonly string[] Ops =
    [
        "NOP", "MOV", "MUL", "ADD", "MAD", "DP3", "DP4", "DST", "MIN", "MAX", "SLT", "SGE", "SLE", "SGT", "SNE", "SEQ",
        "FRC", "FLR", "KIL", "PK4B", "UP4B", "DDX", "DDY", "TEX", "TXP", "TXD", "RCP", "RSQ", "EX2", "LG2", "LIT", "LRP",
        "STR", "SFL", "COS", "SIN", "PK2H", "UP2H", "POW", "PK4UB", "UP4UB", "PK2US", "UP2US", "op2B", "DP2A", "op2D", "TXL", "op2F",
        "TXB", "op31", "op32", "DIV", "op34", "op35", "op36", "op37", "DP2", "NRM", "op3A", "op3B", "op3C", "op3D", "op3E", "op3F",
    ];
    static readonly string[] Inputs = ["WPOS", "COL0", "COL1", "FOGC", "TC0", "TC1", "TC2", "TC3", "TC4", "TC5", "TC6", "TC7", "in12", "in13", "FACE", "in15"];

    static uint W(BigEndianReader r, int at) { uint v = r.U32(at); return (v >> 16) | (v << 16); }

    public static IEnumerable<string> Disassemble(BigEndianReader r, int code, int size)
    {
        for (int pc = 0; pc + 16 <= size;)
        {
            uint d0 = W(r, code + pc);
            int op = (int)((d0 >> 24) & 0x3F);
            int dst = (int)((d0 >> 1) & 0x3F);
            int input = (int)((d0 >> 13) & 0xF);
            int unit = (int)((d0 >> 17) & 0xF);
            string mask = new([.. "xyzw".Where((_, i) => (d0 >> (9 + i) & 1) != 0)]);
            bool hasConst = false;
            var srcs = new List<string>();
            for (int s = 1; s <= 3; s++)
            {
                uint d = W(r, code + pc + s * 4);
                int type = (int)(d & 3), reg = (int)((d >> 2) & 0x3F);
                if (op == 0) break;
                string sw = new([.. Enumerable.Range(0, 4).Select(c => "xyzw"[(int)((d >> (9 + c * 2)) & 3)])]);
                string neg = (d >> 17 & 1) != 0 ? "-" : "";
                srcs.Add(type switch { 0 => $"{neg}R{reg}.{sw}", 1 => $"{neg}{Inputs[input]}.{sw}", 2 => $"{neg}C.{sw}", _ => "?" });
                if (type == 2) hasConst = true;
            }
            string line = $"{pc / 16,3}: {Ops[op],-5} {((d0 & (1u << 30)) != 0 ? "(none)" : $"R{dst}")}.{mask} {string.Join(", ", srcs.Take(op is 4 or 0x1F ? 3 : op is 1 or 0x10 or 0x11 or 0x17 or 0x18 or 0x1A or 0x1B or 0x1C or 0x1D or 0x22 or 0x23 or 0x39 ? 1 : 2))}" +
                          (op is 0x17 or 0x18 or 0x2E or 0x30 ? $"  tex{unit}" : "") + ((d0 & 1) != 0 ? "  END" : "");
            pc += 16;
            if (hasConst && pc + 16 <= size)
            {
                var f = Enumerable.Range(0, 4).Select(k => BitConverter.UInt32BitsToSingle(W(r, code + pc + k * 4))).ToArray();
                line += $"   C = ({string.Join(", ", f.Select(x => x.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)))})";
                pc += 16;
            }
            yield return line;
        }
    }
}
