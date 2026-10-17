using System.Text.RegularExpressions;
using GTFormats;

/// <summary>
/// gtdev mdl3-matcheck &lt;files.tsv&gt; &lt;groupRegex&gt; &lt;out.txt&gt;
/// Checks the layout of the material block (0xE4), the SHDS block (0xFC) and the TXS3 header on every file:
/// table placement, index ranges, constant RSX command words, the material -> data -> shader chain.
/// Every check is a named counter; "fail" counters must be 0 for the layout to be called verified.
/// </summary>
static class Mdl3MatLayout
{
    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.WriteLine("mdl3-matcheck <files.tsv> <groupRegex> <out.txt>"); return 1; }
        var rx = new Regex(args[1]);
        var files = File.ReadAllLines(args[0]).Select(l => l.Split('\t')).Where(p => rx.IsMatch(p[1])).ToList();
        using var w = new StreamWriter(args[2]);

        foreach (var ver in files.GroupBy(p => p[2]).OrderBy(g => int.Parse(g.Key)))
        {
            var c = new SortedDictionary<string, int>();
            var ex = new Dictionary<string, string>();
            void Count(string k, string? file = null)
            {
                c[k] = c.GetValueOrDefault(k) + 1;
                if (file != null && !ex.ContainsKey(k)) ex[k] = file;
            }

            foreach (var p in ver)
            {
                var r = new BigEndianReader(File.ReadAllBytes(p[0]));
                try { Check(r, (k, fail) => Count(k, fail ? p[0] : null)); }
                catch (Exception e) { Count("fail: exception " + e.GetType().Name, p[0]); }
            }

            w.WriteLine($"================ v{ver.Key}: {ver.Count()} files");
            foreach (var (k, n) in c) w.WriteLine($"{n,9}  {k}{(ex.TryGetValue(k, out var f) ? "   e.g. " + f : "")}");
            w.WriteLine();
        }
        Console.WriteLine($"{files.Count} files -> {args[2]}");
        return 0;
    }

    /// <summary>report(name, isFailure)</summary>
    static void Check(BigEndianReader r, Action<string, bool> report)
    {
        void Ok(string k) => report(k, false);
        void Expect(bool cond, string k) => report((cond ? "ok: " : "fail: ") + k, !cond);
        bool InFile(long off, long len = 0) => off > 0 && off + len <= r.Length;

        const int mat = 0xE4, shds = 0xFC;
        uint runtimeSize = r.U32(0x6C);

        // ---- material header
        int matCount = r.U16(mat), dataCount = r.U16(mat + 2), byte4 = r.U8(mat + 4), gcmCount = r.U8(mat + 5), texCount = r.U16(mat + 6);
        uint matsAt = r.U32(mat + 8), dataAt = r.U32(mat + 0x0C), gcmAt = r.U32(mat + 0x10), texAt = r.U32(mat + 0x14);
        uint txs = r.U32(0x48);
        Expect(InFile(matsAt, matCount * 0x34L) && InFile(dataAt, dataCount * 0x28L) && InFile(gcmAt, gcmCount * 0xA0L) && InFile(texAt, texCount * 0x44L), "mat tables inside the file");
        Ok($"gap materials->data: {(long)dataAt - (matsAt + matCount * 0x34L):X}");
        Ok($"gap gcm->texInfo: {(long)texAt - (gcmAt + gcmCount * 0xA0L):X}");
        Ok($"gap texInfo->TXS3: {(long)txs - (texAt + texCount * 0x44L):X}");
        Expect(dataAt + dataCount * 0x28L <= gcmAt, "data table ends before gcm");

        // ---- materials (0x34)
        int maxP06 = -1;
        for (int i = 0; i < matCount; i++)
        {
            int a = (int)matsAt + i * 0x34;
            bool zero = true;
            for (int o = 0x10; o < 0x34; o += 4) zero &= r.U32(a + o) == 0;
            Expect(zero, "material +10..+33 zero");
            Expect(r.S16(a + 4) >= 0 && r.S16(a + 4) < dataCount, "material +04 data index < data count");
            maxP06 = Math.Max(maxP06, r.S16(a + 6));
            Expect(r.S16(a + 6) >= 0, "material +06 >= 0");
            Ok($"material flags {r.U16(a + 8):X}");
            int keys = r.U16(a + 0x0A); uint keysAt = r.U32(a + 0x0C);
            Expect(keys == 0 ? keysAt == 0 : InFile(keysAt, keys * 8L), "material sampler keys inside the file");
            string prev = "";
            for (int k = 0; k < keys; k++)
            {
                string name = r.CString((int)r.U32((int)keysAt + k * 8));
                Expect(string.CompareOrdinal(prev, name) < 0, "material sampler keys sorted by name");
                prev = name;
                uint ti = r.U32((int)keysAt + k * 8 + 4);
                if ((ti & 0x8000) != 0) Ok($"material sampler key external 0x8000|{ti & 0x7FFF} ({name})");
                else Expect(ti < texCount, "material sampler key -> texInfo index < texInfo count");
            }
            uint nameOff = r.U32(a);
            Expect(nameOff == 0 || InFile(nameOff), "material name offset 0 or inside");
        }
        if (matCount > 0)
        {
            Ok(maxP06 + 1 == byte4 ? "material +06 max+1 == mat header byte4" : maxP06 < byte4 ? "material +06 max+1 < byte4" : "material +06 max >= byte4");
            Ok(maxP06 < gcmCount ? "material +06 max < gcm count" : "material +06 max >= gcm count");
        }

        // ---- material data (0x28)
        int defCount = r.U16(shds + 0x16), progCount = r.U16(shds + 0x18), fpCount = r.U16(shds + 0x1A);
        uint defsAt = r.U32(shds + 0x1C), progsAt = r.U32(shds + 0x20);
        for (int i = 0; i < dataCount; i++)
        {
            int a = (int)dataAt + i * 0x28;
            Ok($"data +08 {r.U16(a + 8):X4}");
            int keys = r.U16(a + 0x0A); uint keysAt = r.U32(a + 0x10), paramsAt = r.U32(a + 0x0C);
            Expect(keys == 0 ? keysAt == 0 && paramsAt == 0 : InFile(keysAt, keys * 8L) && InFile(paramsAt), "data param keys + values present together");
            int maxSlot = -1;
            for (int k = 0; k < keys; k++) maxSlot = Math.Max(maxSlot, (int)r.U32((int)keysAt + k * 8 + 4));
            if (keys > 0) Ok(maxSlot + 1 == keys ? "data param slots: max+1 == key count" : maxSlot + 1 > keys ? "data param slots: max+1 > key count" : "data param slots: max+1 < key count");
            uint sr = r.U32(a + 0x14);
            Expect(InFile(sr, 0x28), "data +14 shader reference inside");
            if (InFile(sr, 0x28))
            {
                int id = (int)r.U32((int)sr + 4);
                Expect(id >= 0 && id < defCount, "shader reference id < SHDS def count");
                Expect(r.CString((int)r.U32((int)sr)) == r.CString((int)r.U32(a)), "shader reference name == data name");
            }
            Expect(r.U16(a + 0x22) >= 1, "data +22 list count >= 1");
            Expect(InFile(r.U32(a + 0x18), r.U16(a + 0x22) * 8L), "data +18 list inside");
            Expect(InFile(r.U32(a + 0x24), 0x20), "data +24 record inside");
        }
        if (dataCount > 0) Ok(defCount == dataCount ? "SHDS def count == data count" : "SHDS def count != data count");

        // ---- RSX state block (0xA0): three command headers at fixed places
        for (int i = 0; i < gcmCount; i++)
        {
            int a = (int)gcmAt + i * 0xA0;
            Expect(r.U32(a) == 0x00780304, "gcm +00 = 0x00780304 (30 regs from 0x304 ALPHA_TEST_ENABLE)");
            Expect(r.U32(a + 0x7C) == 0x000C0A6C, "gcm +7C = 0x000C0A6C (3 regs from 0xA6C DEPTH_FUNC)");
            Expect(r.U32(a + 0x8C) == 0x0004183C, "gcm +8C = 0x0004183C (1 reg 0x183C CULL_FACE_ENABLE)");
            Ok($"gcm +94 {r.U32(a + 0x94):X8} +98 {r.U32(a + 0x98):X8} +9C {r.U32(a + 0x9C):X8}");
        }

        // ---- texture register block (0x44)
        for (int i = 0; i < texCount; i++)
        {
            int a = (int)texAt + i * 0x44;
            Expect(r.U32(a) == 0x00201A00, "tex +00 = 0x00201A00 (8 regs from 0x1A00 TEXTURE_OFFSET)");
            Expect(r.U32(a + 4) == 0, "tex +04 offset 0 on disk");
            Expect(r.U32(a + 0x24) == 0x00041840, "tex +24 = 0x00041840 (1 reg 0x1840 TEXTURE_CONTROL3)");
            if (r.U16(0x0C) >= 14) Ok($"tex v14 +2C {r.U32(a + 0x2C):X8} +30 {r.U32(a + 0x30):X8} +34 {r.U32(a + 0x34):X8}");
            else Expect(r.U32(a + 0x2C) == 0 && r.U32(a + 0x30) == 0 && r.U32(a + 0x34) == 0, "tex +2C,+30,+34 zero");
            Ok($"tex +38 hi16 {r.U16(a + 0x38):X} +3C {r.U32(a + 0x3C):X}");
            Expect(InFile(r.U32(a + 0x40)), "tex +40 name inside");
            Expect((r.U32(a + 0x08) & 3) is 1 or 2, "tex location bits 1/2");
        }

        // ---- SHDS header + definitions + vertex programs
        Expect(r.Magic(shds) == "SHDS" && r.U32(shds + 8) == 0xFC, "SHDS magic, +08 = own offset 0xFC");
        Expect(r.U32(shds + 0x28) == runtimeSize, "SHDS +28 == MDL3 header 0x6C");
        Expect(InFile(defsAt, defCount * 0x0CL) && InFile(progsAt, progCount * 0x28L), "SHDS defs/programs inside");
        Ok($"SHDS +30 u16 {r.U16(shds + 0x30)}");
        Ok(r.U16(shds + 0x34) == fpCount ? "SHDS +34 == +1A" : "SHDS +34 != +1A");
        Ok(r.U16(shds + 0x32) == defCount ? "SHDS +32 == def count" : "SHDS +32 != def count");
        for (int i = 0; i < defCount; i++)
        {
            int a = (int)defsAt + i * 0x0C;
            Expect(r.S16(a + 8) >= 0 && r.S16(a + 8) < progCount, "def +08 program < SHDS +18");
            Expect(r.S16(a + 0x0A) >= 0 && r.S16(a + 0x0A) < fpCount, "def +0A index < SHDS +1A");
        }
        for (int i = 0; i < progCount; i++)
        {
            int a = (int)progsAt + i * 0x28;
            Expect(InFile(r.U32(a), r.U32(a + 4)), "program microcode inside");
            Expect(r.U32(a + 4) % 16 == 0, "program size multiple of 16 (RSX VP instruction)");
            int inputs = r.U16(a + 0x0A), consts = r.U16(a + 0x0C);
            Expect(inputs == 0 || InFile(r.U32(a + 0x14), inputs * 0x10L), "program inputs table inside");
            Expect(consts == 0 || InFile(r.U32(a + 0x18), consts * 0x10L), "program constants table inside");
        }

        // ---- TXS3 header
        Expect(r.Magic((int)txs) == "TXS3", "TXS3 magic");
        Expect(r.U32((int)txs + 0x20) == runtimeSize, "TXS3 +20 == MDL3 header 0x6C");
        Ok($"TXS3 +10 {r.U32((int)txs + 0x10)}");
        int t = (int)txs;
        Expect(r.U32(t + 8) == txs, "TXS3 +08 == own offset (reloc base)");
        Expect(r.U16(t + 0x14) == 0 && r.U32(t + 0x18) == 0, "TXS3 texture info count/offset 0 (they are in the material block)");
        int bufCount = r.U16(t + 0x16);
        uint bufAt = r.U32(t + 0x1C);
        Expect(bufAt == txs + 0x40, "TXS3 +1C buffers right after the 0x40 header");
        bool imagesInFile = runtimeSize <= r.Length;
        Ok(imagesInFile ? "image data start (0x6C) inside the file" : "image data start (0x6C) past the end of the file");
        long next = runtimeSize;
        for (int i = 0; i < bufCount; i++)
        {
            int b = (int)bufAt + i * 0x20;
            uint off = r.U32(b), size = r.U32(b + 4);
            Ok(off >= runtimeSize ? "buffer image at/after 0x6C" : off >= bufAt + bufCount * 0x20 ? "buffer image before 0x6C, after the buffer list" : "buffer image before the buffer list");
            if (off == 0) Ok("buffer image offset 0 (pixels not in this file)");
            else Expect(off + (long)size <= r.Length, "buffer image inside the file");
            Ok(off == next ? "buffer image contiguous with the previous one" : $"buffer image gap/overlap");
            next = off + size;
            Ok($"buffer +08 {r.U8(b + 8)}  +0B {r.U8(b + 0x0B)}  +10 {r.U16(b + 0x10):X} +12 {r.U16(b + 0x12):X} +18 {r.U32(b + 0x18):X} +1C {r.U32(b + 0x1C):X}");
        }
        if (bufCount > 0)
        {
            long end = Enumerable.Range(0, bufCount).Max(i => (long)r.U32((int)bufAt + i * 0x20) + r.U32((int)bufAt + i * 0x20 + 4));
            Ok(end == r.Length ? "images end at end of file" : end < r.Length ? "images end before end of file (more data after)" : "images end past end of file");
        }
        for (int i = 0; i < texCount; i++)
        {
            int a = (int)texAt + i * 0x44;
            uint img = r.U32(a + 0x38) & 0xFFFF;
            if (!Expect2(img < bufCount, "tex +3A image index < TXS3 buffer count")) continue;
            int b = (int)bufAt + (int)img * 0x20;
            Expect(r.U16(a + 0x1C) == r.U16(b + 0x0C) && r.U16(a + 0x1E) == r.U16(b + 0x0E), "tex width/height == buffer width/height");
            Expect(r.U8(a + 0x0A) == r.U8(b + 0x09), "tex format (FORMAT reg bits 8-15) == buffer format byte");
            Expect(r.U8(a + 0x09) == r.U8(b + 0x0A), "tex mipmap count (bits 16-23) == buffer +0A");
        }

        bool Expect2(bool cond, string k) { Expect(cond, k); return cond; }
    }
}
