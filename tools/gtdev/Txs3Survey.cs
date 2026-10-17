using System.Text.RegularExpressions;
using GTFormats;

/// <summary>
/// gtdev txs3-survey &lt;files.tsv&gt; &lt;groupRegex&gt; &lt;out.txt&gt;
/// Every TXS3 image record of every file: format byte (CELL_GCM_TEXTURE_*, LN 0x20 / UN 0x40 flags),
/// mipmap count, cube flag, record bytes +08/+0B/+10.., and the computed pixel size (all mip levels, 6 faces for cubes)
/// against the record size. Counters per version, "fail" counters must be 0.
/// </summary>
static class Txs3Survey
{
    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.WriteLine("txs3-survey <files.tsv> <groupRegex> <out.txt>"); return 1; }
        var rx = new Regex(args[1]);
        var files = File.ReadAllLines(args[0]).Select(l => l.Split('\t')).Where(p => rx.IsMatch(p[1])).ToList();
        using var w = new StreamWriter(args[2]);

        foreach (var grp in files.GroupBy(p => p[1] + " v" + p[2]).OrderBy(g => g.Key))
        {
            var c = new SortedDictionary<string, int>();
            var ex = new Dictionary<string, string>();
            foreach (var p in grp)
            {
                void Count(string k, string? detail)
                {
                    c[k] = c.GetValueOrDefault(k) + 1;
                    if (detail != null && !ex.ContainsKey(k)) ex[k] = p[0] + " " + detail;
                }
                try { Check(new BigEndianReader(File.ReadAllBytes(p[0])), Count); }
                catch (Exception e) { Count("fail: exception " + e.GetType().Name + " " + e.Message, ""); }
            }
            w.WriteLine($"================ {grp.Key}: {grp.Count()} files");
            foreach (var (k, n) in c) w.WriteLine($"{n,9}  {k}{(ex.TryGetValue(k, out var f) ? "   e.g. " + f : "")}");
            w.WriteLine();
            w.Flush();
        }
        Console.WriteLine($"{files.Count} files -> {args[2]}");
        return 0;
    }

    /// <summary>Bytes per pixel (0 = block compressed, handled apart), by base format (flags 0x20 LN / 0x40 UN removed).</summary>
    static readonly Dictionary<int, int> Bpp = new()
    {
        [0x81] = 1, [0x82] = 2, [0x83] = 2, [0x84] = 2, [0x85] = 4, [0x8B] = 2, [0x8F] = 2,
        [0x90] = 4, [0x91] = 4, [0x92] = 2, [0x93] = 2, [0x94] = 2, [0x95] = 4, [0x97] = 2, [0x98] = 2, [0x99] = 2,
        [0x9A] = 8, [0x9B] = 16, [0x9C] = 4, [0x9D] = 2, [0x9E] = 4, [0x9F] = 4, [0xAD] = 2, [0xAE] = 2,
    };

    public static string FormatName(int f) => (f & ~0x60) switch
    {
        0x81 => "B8", 0x82 => "A1R5G5B5", 0x83 => "A4R4G4B4", 0x84 => "R5G6B5", 0x85 => "A8R8G8B8",
        0x86 => "DXT1", 0x87 => "DXT23", 0x88 => "DXT45", 0x8B => "G8B8", 0x8F => "R6G5B5",
        0x94 => "X16", 0x95 => "Y16_X16", 0x97 => "R5G5B5A1", 0x98 => "HILO8", 0x99 => "HILO_S8",
        0x9A => "W16Z16Y16X16F", 0x9B => "W32Z32Y32X32F", 0x9C => "X32F", 0x9D => "D1R5G5B5", 0x9E => "D8R8G8B8",
        0x9F => "Y16X16F", 0xAD => "B8R8_G8R8", 0xAE => "R8B8_R8G8", _ => $"?{f & ~0x60:X2}",
    } + ((f & 0x20) != 0 ? "+LN" : "") + ((f & 0x40) != 0 ? "+UN" : "");

    /// <summary>Size of one face: mip levels w,h halving down to 1, linear rows at <paramref name="pitch"/> when given.</summary>
    public static long LevelsSize(int fmt, int w, int h, int mips, int pitch = 0)
    {
        int b = fmt & ~0x60;
        long total = 0;
        for (int m = 0; m < mips; m++)
        {
            int lw = Math.Max(1, w >> m), lh = Math.Max(1, h >> m);
            if (b is 0x86 or 0x87 or 0x88)
                total += (long)((lw + 3) / 4) * ((lh + 3) / 4) * (b == 0x86 ? 8 : 16);
            else if (Bpp.TryGetValue(b, out int bpp))
                total += pitch > 0 ? (long)pitch * lh : (long)lw * lh * bpp;
            else return -1;
        }
        return total;
    }

    public static long Align128(long n) => (n + 127) / 128 * 128;

    static void Check(BigEndianReader r, Action<string, string?> count)
    {
        void Ok(string k) => count(k, null);
        void Expect(bool cond, string k, string detail = "") => count((cond ? "ok: " : "fail: ") + k, cond ? null : detail);

        const int mat = 0xE4;
        int texCount = r.U16(mat + 6);
        uint texAt = r.U32(mat + 0x14);
        int t = (int)r.U32(0x48);
        if (r.Magic(t) != "TXS3") { Ok("no TXS3"); return; }
        int bufCount = r.U16(t + 0x16);
        int bufAt = (int)r.U32(t + 0x1C);
        Ok($"images per file: {(bufCount == 0 ? "0" : bufCount < 10 ? "1-9" : bufCount < 50 ? "10-49" : "50+")}");

        // texture info -> image: cube flag (FORMAT bit 2), pitch (control3 low 20 bits)
        var cube = new Dictionary<int, bool>();
        var pitch = new Dictionary<int, int>();
        int bufAt0 = (int)r.U32(t + 0x1C);
        var users = new Dictionary<int, int>();
        var locMatch = new HashSet<int>();
        for (int i = 0; i < texCount; i++)
        {
            int a = (int)texAt + i * 0x44;
            int img = r.U16(a + 0x3A);
            if (img >= bufCount) continue;
            uint format = r.U32(a + 0x08);
            cube[img] = (format & 4) != 0;
            pitch[img] = (int)(r.U32(a + 0x28) & 0xFFFFF);
            Ok($"tex FORMAT dimension {(format >> 4) & 0xF}, border {(format >> 3) & 1}, location {format & 3}");
            int rb = bufAt0 + img * 0x20;
            Expect(r.U8(rb + 8) == ((format >> 2) & 1), "rec +08 == FORMAT cube bit");
            Ok($"remap {r.U32(a + 0x14):X5} {FormatName(r.U8(rb + 9))}");
            users[img] = users.GetValueOrDefault(img) + 1;
            if (r.U8(rb + 0x0B) == (format & 3)) locMatch.Add(img);
            else Ok("tex FORMAT location != rec +0B (texture info of a shared image)");
            Expect((r.U16(rb + 0x10) > 1) == (((format >> 4) & 0xF) == 3), "rec +10 depth > 1 <=> FORMAT dimension 3");
            Expect((r.U32(a + 0x28) >> 20) == r.U16(rb + 0x10), "control3 depth == rec +10");
        }

        foreach (var (img, n) in users)
        {
            Ok($"image users: {(n == 1 ? "1" : "2+")}");
            Expect(locMatch.Contains(img), "rec +0B == FORMAT location of one of its texture infos", $"img {img}");
        }

        for (int i = 0; i < bufCount; i++)
        {
            int b = bufAt + i * 0x20;
            uint off = r.U32(b), size = r.U32(b + 4);
            int fmt = r.U8(b + 9), mips = r.U8(b + 0x0A), w = r.U16(b + 0x0C), h = r.U16(b + 0x0E);
            bool isCube = cube.GetValueOrDefault(i);
            string d = $"img {i} {FormatName(fmt)} {w}x{h} mips {mips} size {size}{(isCube ? " cube" : "")}";
            Ok($"format {FormatName(fmt)}{(isCube ? " cube" : "")}");
            Ok($"rec +08 {r.U8(b + 8)} +0B {r.U8(b + 0x0B)} +10 {r.U16(b + 0x10)} +12 {r.U16(b + 0x12)} +14..1F {(r.U32(b + 0x14) | r.U32(b + 0x18) | r.U32(b + 0x1C)) == 0}");
            if (!cube.ContainsKey(i)) Ok("image not used by any texture info");
            Ok($"pow2 {((w & (w - 1)) == 0 && (h & (h - 1)) == 0 ? "yes" : "no")}{((fmt & 0x20) != 0 ? " LN" : " SZ")}");
            int maxMips = 1 + (int)Math.Log2(Math.Max(w, h));
            Ok(mips == maxMips ? "mips full chain" : mips == 1 ? "mips 1" : mips < maxMips ? "mips partial chain" : "mips > full chain");

            // record +10 = depth (3D slices, contiguous); each cube face (all mips, all slices) is padded to 128 bytes
            int depth = r.U16(b + 0x10);
            int faces = isCube ? 6 : 1;
            if (depth > 1) Ok($"depth {depth} (3D) {FormatName(fmt)} {w}x{h} mips {mips}");
            long face = LevelsSize(fmt, w, h, mips) * depth;
            if (face < 0) { Expect(false, "known format", d); continue; }
            long plain = Align128(face) * faces;
            Ok(face % 128 == 0 ? "face size already a multiple of 128" : "face size padded to 128");
            if (plain == size) { Ok($"size == computed (packed{(isCube ? ", cube" : "")})"); continue; }
            int pt = pitch.GetValueOrDefault(i);
            long pitched = (fmt & 0x20) != 0 && pt > 0 ? Align128(LevelsSize(fmt, w, h, mips, pt) * depth) * faces : -1;
            if (pitched == size) { Ok($"size == computed (pitch rows, mips {(mips == 1 ? "1" : ">1")})"); continue; }
            // linear with mipmaps: every level keeps the pitch of a power-of-2 width
            int bp = Bpp.GetValueOrDefault(fmt & ~0x60);
            long pow2Pitched = (fmt & 0x20) != 0 && bp > 0
                ? Align128(LevelsSize(fmt, w, h, mips, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)w) * bp) * depth) * faces : -1;
            if (pow2Pitched == size) { Ok($"size == computed (pow2 pitch rows, mips {(mips == 1 ? "1" : ">1")}, texinfo pitch {(pt == w * bp ? "w*bpp" : pt.ToString())})"); continue; }
            Expect(false, "size == computed", $"{d} computed {plain} pitch {pt} pitched {pitched} diff {(long)size - plain}");
        }
    }
}
