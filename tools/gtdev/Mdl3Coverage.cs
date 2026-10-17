using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using GTFormats;
using GTFormats.Mdl3;

/// <summary>
/// gtdev mdl3-coverage &lt;files.tsv&gt; &lt;groupRegex&gt; &lt;out.txt&gt;   (or: mdl3-coverage &lt;one MDL3 file&gt; &lt;out.txt&gt;)
/// Byte coverage of an MDL3 file: every known structure claims its bytes; what nobody claims is listed.
/// The goal of roadmap 2f is "no unexplained non-zero byte" outside geometry and pixel data (items 3/4/5).
/// Table checks (counts, ranges, links between tables) run in the same pass and are reported as ok/fail counters.
/// </summary>
static class Mdl3Coverage
{
    public static int Run(string[] args)
    {
        if (args.Length == 2 && File.Exists(args[0]) && !args[0].EndsWith(".tsv"))
            return One(args[0], args[1]);
        if (args.Length < 3) { Console.WriteLine("mdl3-coverage <files.tsv> <groupRegex> <out.txt> | mdl3-coverage <file> <out.txt>"); return 1; }
        var rx = new Regex(args[1]);
        var files = File.ReadAllLines(args[0]).Select(l => l.Split('\t')).Where(p => rx.IsMatch(p[1])).ToList();
        using var w = new StreamWriter(args[2]);
        foreach (var ver in files.GroupBy(p => p[2]).OrderBy(g => int.Parse(g.Key)))
        {
            var counts = new ConcurrentDictionary<string, long>();
            var examples = new ConcurrentDictionary<string, string>();
            Parallel.ForEach(ver, new ParallelOptions { MaxDegreeOfParallelism = 6 }, p =>
            {
                var cov = new Coverage(File.ReadAllBytes(p[0]));
                try { cov.Explain(); }
                catch (Exception e) { cov.Fail("exception " + e.GetType().Name + ": " + e.Message.Split('\n')[0]); }
                foreach (var (k, n) in cov.Counters)
                {
                    counts.AddOrUpdate(k, n, (_, o) => o + n);
                    examples.TryAdd(k, p[0]);
                }
            });
            w.WriteLine($"================ v{ver.Key}: {ver.Count()} files");
            foreach (var (k, n) in counts.OrderBy(k => k.Key, StringComparer.Ordinal))
                w.WriteLine($"{n,12}  {k}{(k.StartsWith("fail") || k.StartsWith("unclaimed") ? "   e.g. " + examples[k] : "")}");
            w.WriteLine();
        }
        Console.WriteLine($"{files.Count} files -> {args[2]}");
        return 0;
    }

    static int One(string path, string outPath)
    {
        var cov = new Coverage(File.ReadAllBytes(path));
        try { cov.Explain(); }
        catch (Exception e) { cov.Fail("exception " + e); }
        using var w = new StreamWriter(outPath);
        w.WriteLine(path);
        foreach (var (k, n) in cov.Counters.OrderBy(k => k.Key, StringComparer.Ordinal)) w.WriteLine($"{n,12}  {k}");
        w.WriteLine();
        w.WriteLine("Unclaimed non-zero runs:");
        foreach (var (start, len, region) in cov.UnclaimedRuns(nonZeroOnly: true))
            w.WriteLine($"  {start:X8} +{len:X6}  in {region}: {Convert.ToHexString(cov.R.Data, start, Math.Min(len, 48))}");
        w.WriteLine();
        w.WriteLine("Claims (first byte of each label run):");
        foreach (var (start, len, label) in cov.Runs())
            w.WriteLine($"  {start:X8} +{len:X6}  {label}");
        Console.WriteLine($"-> {outPath}");
        return 0;
    }
}

/// <summary>Owner map of one file plus the counters of its checks.</summary>
sealed class Coverage
{
    public readonly BigEndianReader R;
    readonly ushort[] _owner;
    readonly List<string> _labels = [""];
    readonly Dictionary<string, ushort> _labelIds = [];
    public readonly Dictionary<string, long> Counters = [];
    readonly List<(uint start, string name)> _blocks = [];
    int Version => R.U16(0x0C);

    public Coverage(byte[] data)
    {
        R = new BigEndianReader(data);
        _owner = new ushort[data.Length];
        foreach (var f in Mdl3Header.Fields.Where(f => f.Kind == Mdl3Header.Kind.Offset32))
        {
            uint v = R.U32(f.Offset);
            if (v != 0 && v < data.Length) _blocks.Add((v, f.Name));
        }
        _blocks.Sort();
    }

    // ---------------------------------------------------------------- counters
    public void Count(string k, long n = 1) => Counters[k] = Counters.GetValueOrDefault(k) + n;
    public void Ok(string k) => Count("ok: " + k);
    public void Fail(string k) => Count("fail: " + k);
    public bool Expect(bool cond, string k) { if (cond) Ok(k); else Fail(k); return cond; }
    public void Stat(string k) => Count("stat: " + k);

    bool In(long off, long len = 0) => off > 0 && off + len <= R.Length;

    // ---------------------------------------------------------------- claims
    ushort Id(string label)
    {
        if (!_labelIds.TryGetValue(label, out var id))
        {
            id = (ushort)_labels.Count;
            _labels.Add(label);
            _labelIds[label] = id;
        }
        return id;
    }

    /// <summary>Claims [start, start+len) for a label. A byte claimed twice by different labels is an overlap (a layout error).</summary>
    public void Claim(long start, long len, string label)
    {
        if (len <= 0) return;
        if (start < 0 || start + len > R.Length) { Fail($"claim outside the file ({label})"); return; }
        ushort id = Id(label);
        ushort other = 0;
        for (long i = start; i < start + len; i++)
        {
            ushort o = _owner[i];
            if (o != 0 && o != id && !Shared(o) && !Shared(id)) other = o;
            if (o == 0 || Shared(o)) _owner[i] = id;
        }
        if (other != 0) Fail($"overlap: {label} / {_labels[other]}");
    }

    bool Shared(ushort id) => _labels[id] == "strings";

    string Str(long off)
    {
        if (!In(off)) { Fail("string pointer outside the file"); return ""; }
        string s = R.CString((int)off);
        Claim(off, s.Length + 1, "strings");
        return s;
    }

    public IEnumerable<(int start, int len, string label)> Runs()
    {
        int i = 0;
        while (i < _owner.Length)
        {
            int j = i;
            while (j < _owner.Length && _owner[j] == _owner[i]) j++;
            yield return (i, j - i, _owner[i] == 0 ? "-" : _labels[_owner[i]]);
            i = j;
        }
    }

    string Region(long off)
    {
        string name = "header";
        foreach (var (start, n) in _blocks) if (start <= off) name = n;
        return name;
    }

    public IEnumerable<(int start, int len, string region)> UnclaimedRuns(bool nonZeroOnly)
    {
        foreach (var (start, len, label) in Runs())
        {
            if (label != "-") continue;
            if (nonZeroOnly && R.Data.AsSpan(start, len).IndexOfAnyExcept((byte)0) < 0) continue;
            yield return (start, len, Region(start));
        }
    }

    // ---------------------------------------------------------------- explain
    public void Explain()
    {
        Expect(R.Magic(0) == "MDL3", "magic");
        Claim(0, Mdl3Header.Size, "header");

        Models();
        Keys("modelKeys", 0x12, 0x34, R.U16(0x10));
        Shapes();
        Keys("shapeKeys", 0x16, 0x3C, R.U16(0x14));
        Fvfs();
        ShapeData();
        Materials();
        Shaders();
        TextureSet();
        SamplerKeyTable();
        ShaderReferences();
        Bones();
        // 2f tables
        Table54();
        RegisterTable();
        Table5C();
        VmCode();
        Table78();
        Block94();
        ModelMaterialLists();
        MaterialAnimation();
        VmContext();
        WingData();
        TableA4();
        TableC0();
        PackedMesh();
        StreamMap();

        // summary
        long claimed = 0, zeroFree = 0, nonZeroFree = 0;
        for (int i = 0; i < _owner.Length; i++)
        {
            if (_owner[i] != 0) claimed++;
            else if (R.Data[i] == 0) zeroFree++;
            else nonZeroFree++;
        }
        Count("bytes: claimed", claimed);
        Count("bytes: unclaimed zero", zeroFree);
        Count("bytes: unclaimed non-zero", nonZeroFree);
        foreach (var (s, len, region) in UnclaimedRuns(true))
            Count($"unclaimed non-zero bytes in {region}", R.Data.AsSpan(s, len).ToArray().Count(b => b != 0));
        foreach (var (label, bytes) in Runs().GroupBy(x => x.label).Select(g => (g.Key, g.Sum(x => (long)x.len))))
            if (label != "-") Count($"bytes: {label}", bytes);
    }

    void Keys(string label, int countField, int offField, int indexLimit)
    {
        int n = R.U16(countField); uint at = R.U32(offField);
        if (n == 0) return;
        if (!Expect(In(at, n * 8L), $"{label} inside")) return;
        Claim(at, n * 8L, label);
        string prev = "";
        bool sorted = true, inRange = true;
        for (int i = 0; i < n; i++)
        {
            string s = Str(R.U32((int)at + i * 8));
            if (string.CompareOrdinal(prev, s) > 0) sorted = false;
            prev = s;
            if (indexLimit >= 0 && R.U32((int)at + i * 8 + 4) >= indexLimit) inRange = false;
        }
        Expect(sorted, $"{label} sorted by name");
        if (indexLimit >= 0) Expect(inRange, $"{label} index in range");
    }

    // ---------------------------------------------------------------- 2a-2e structures
    void Models()
    {
        int n = R.U16(0x10); uint at = R.U32(0x30);
        Claim(at, n * 0x30L, "models");
        for (int i = 0; i < n; i++)
        {
            int m = (int)at + i * 0x30;
            // +12 u8 flags, +13 u8 bound point count (PDTools reads a u16 count; 0x0408 is flag 4 + 8 points)
            int bc = R.U8(m + 0x13); uint bo = R.U32(m + 0x14);
            Stat($"model +12 flags {R.U8(m + 0x12):X} points {bc}");
            if (bc > 0) Claim(bo, bc * 12L, "model bounds");
            Claim(R.U32(m + 0x18), R.U32(m + 0x1C), "model commands");
        }
    }

    readonly List<Mdl3Fvf> _fvfs = [];

    void Fvfs()
    {
        int n = R.U16(0x18); uint at = R.U32(0x40);
        Claim(at, n * (long)Mdl3Fvf.Size, "fvf");
        for (int i = 0; i < n; i++)
        {
            int a = (int)at + i * Mdl3Fvf.Size;
            var f = Mdl3Fvf.Read(R, a);
            _fvfs.Add(f);
            Str(R.U32(a));
            int ec = R.U8(a + 0x18); uint eo = R.U32(a + 8);
            Claim(eo, ec * 8L, "fvf elements");
            for (int e = 0; e < ec; e++) Str(R.U32((int)eo + e * 8));
            if (f.ArrayDefOffset != 0) Claim(f.ArrayDefOffset, 0x10, "fvf array def");
            // +14: one byte per element (RSX vertex attribute slot), 0x10 bytes
            uint slots = R.U32(a + 0x14);
            Stat($"fvf +14 attribute slots present: {slots != 0}");
            if (slots != 0)
            {
                Claim(slots, 0x10, "fvf attribute slots");
                bool restZero = true;
                for (int e = ec; e < 0x10; e++) restZero &= R.U8((int)slots + e) == 0;
                Expect(restZero, "fvf attribute slots: zero after the element count");
            }
        }
    }

    void Shapes()
    {
        int n = R.U16(0x14); uint at = R.U32(0x38);
        Claim(at, n * 0x30L, "shapes");
    }

    /// <summary>Called after the FVFs are read: plain vertex/index buffers and bboxes.</summary>
    void ShapeData()
    {
        int n = R.U16(0x14); uint at = R.U32(0x38);
        for (int i = 0; i < n; i++)
        {
            var s = Mdl3Shape.Read(R, (int)at + i * 0x30);
            if (s.BBoxOffset != 0) Claim(s.BBoxOffset, 0x60, "shape bbox");
            if (s.VerticesOffset != 0 && s.FvfIndex >= 0 && s.FvfIndex < _fvfs.Count)
            {
                var f = _fvfs[s.FvfIndex];
                long size = (long)s.VertexCount * f.VertexSize;
                if (f.ArrayDefOffset != 0) size = Math.Max(size, f.ArrayDataOffset + (long)f.ArrayElementSize * f.ArrayLength);
                Claim(s.VerticesOffset, size, "plain vertices");
            }
            if (s.TriOffset != 0) Claim(s.TriOffset, s.TriLength * 2L, "plain indices");
            if (s.PackedRefOffset != 0)
            {
                // 0x40: 12 x f32, u32 offset, u32 packed mesh index (PDTools MDL3ShapePackedMeshRef)
                int p = (int)s.PackedRefOffset;
                Claim(p, 0x40, "shape packed refs");
                Stat($"shape packed ref: +30 set {R.U32(p + 0x30) != 0}, floats zero {Enumerable.Range(0, 12).All(k => R.U32(p + k * 4) == 0)}, +38 {R.U32(p + 0x38):X} +3C {R.U32(p + 0x3C):X}");
                if (Version >= 9 && R.U32(0xD0) != 0)
                    Expect(R.U32(p + 0x34) == 0xFFFFFFFF || R.U32(p + 0x34) < R.U16((int)R.U32(0xD0) + 0x10), "shape packed ref +34 < PMSH mesh count or -1");
            }
        }
    }

    void Materials()
    {
        const int mat = 0xE4;
        Claim(mat, 0x18, "material block header");
        int matCount = R.U16(mat), dataCount = R.U16(mat + 2), gcmCount = R.U8(mat + 5), texCount = R.U16(mat + 6);
        uint matsAt = R.U32(mat + 8), dataAt = R.U32(mat + 0x0C), gcmAt = R.U32(mat + 0x10), texAt = R.U32(mat + 0x14);
        Claim(matsAt, matCount * 0x34L, "materials");
        for (int i = 0; i < matCount; i++)
        {
            int a = (int)matsAt + i * 0x34;
            if (R.U32(a) != 0) Str(R.U32(a));
            int keys = R.U16(a + 0x0A); uint keysAt = R.U32(a + 0x0C);
            Claim(keysAt, keys * 8L, "material sampler keys");
            for (int k = 0; k < keys; k++) Str(R.U32((int)keysAt + k * 8));
        }
        Claim(dataAt, dataCount * 0x28L, "material data");
        for (int i = 0; i < dataCount; i++)
        {
            int a = (int)dataAt + i * 0x28;
            Str(R.U32(a));
            int keys = R.U16(a + 0x0A);
            if (keys > 0)
            {
                Claim(R.U32(a + 0x0C), keys * 16L, "material constants");
                uint ka = R.U32(a + 0x10);
                Claim(ka, keys * 8L, "material constant keys");
                for (int k = 0; k < keys; k++) Str(R.U32((int)ka + k * 8));
            }
            // +18: list of (u32 short count, u32 offset); count at +22
            int lc = R.U16(a + 0x22); uint la = R.U32(a + 0x18);
            Claim(la, lc * 8L, "material data +18 list");
            for (int k = 0; k < lc; k++)
            {
                uint sc = R.U32((int)la + k * 8), so = R.U32((int)la + k * 8 + 4);
                if (so != 0) Claim(so, sc * 2L, "material data +18 shorts");
            }
            // +1C: +20 pairs of u16
            int pairs = R.U16(a + 0x20); uint pa = R.U32(a + 0x1C);
            Expect(pairs == 0 ? pa == 0 : In(pa, pairs * 4L), "material data +1C pairs present iff +20 > 0");
            if (pairs > 0) Claim(pa, pairs * 4L, "material data +1C pairs");
            Expect(R.U32(a + 0x24) == R.U32((int)R.U32(a + 0x14) + 0x0C), "material data +24 == its shader reference +0C (first pass)");
        }
        Claim(gcmAt, gcmCount * 0xA0L, "render states");
        Claim(texAt, texCount * 0x44L, "texture infos");
        for (int i = 0; i < texCount; i++) Str(R.U32((int)texAt + i * 0x44 + 0x40));
    }

    void Shaders()
    {
        const int s = 0xFC;
        Claim(s, 0x44, "SHDS header");
        int defCount = R.U16(s + 0x16), vpCount = R.U16(s + 0x18), fpCount = R.U16(s + 0x1A);
        uint defsAt = R.U32(s + 0x1C), vpsAt = R.U32(s + 0x20), fpsAt = R.U32(s + 0x2C);
        Claim(defsAt, defCount * 0x0CL, "SHDS definitions");
        for (int i = 0; i < defCount; i++) if (R.U32((int)defsAt + i * 0xC) != 0) Str(R.U32((int)defsAt + i * 0xC));
        Claim(vpsAt, vpCount * 0x28L, "SHDS vertex programs");
        for (int i = 0; i < vpCount; i++)
        {
            int a = (int)vpsAt + i * 0x28;
            Claim(R.U32(a), R.U32(a + 4), "SHDS VP microcode");
            int inputs = R.U16(a + 0x0A), consts = R.U16(a + 0x0C), n10 = R.U16(a + 0x08);
            if (n10 > 0) { Claim(R.U32(a + 0x10), n10 * 0x10L, "SHDS VP +10 entries"); for (int k = 0; k < n10; k++) Str(R.U32((int)R.U32(a + 0x10) + k * 0x10)); }
            if (inputs > 0) { Claim(R.U32(a + 0x14), inputs * 0x10L, "SHDS VP inputs"); for (int k = 0; k < inputs; k++) Str(R.U32((int)R.U32(a + 0x14) + k * 0x10)); }
            if (consts > 0) { Claim(R.U32(a + 0x18), consts * 0x10L, "SHDS VP constants"); for (int k = 0; k < consts; k++) Str(R.U32((int)R.U32(a + 0x18) + k * 0x10)); }
        }
        Claim(fpsAt, fpCount * 0x48L, "SHDS fragment programs");
        for (int i = 0; i < fpCount; i++)
        {
            int a = (int)fpsAt + i * 0x48;
            Claim(R.U32(a), R.U32(a + 4), "SHDS FP headers");
            // named 0x10 tables: +10 (count u16 +08), +14 (u8 +0B), +18 (u16 +0C), +38 (u8 +0A)
            foreach (var (off, cnt, label) in new[] { (0x10, R.U16(a + 8), "SHDS FP +10 entries"), (0x14, (int)R.U8(a + 0x0B), "SHDS FP samplers"),
                                                      (0x18, R.U16(a + 0x0C), "SHDS FP constants"), (0x38, (int)R.U8(a + 0x0A), "SHDS FP global samplers") })
            {
                if (cnt == 0) continue;
                uint t = R.U32(a + off);
                Claim(t, cnt * 0x10L, label);
                for (int k = 0; k < cnt; k++) if (R.U32((int)t + k * 0x10) != 0) Str(R.U32((int)t + k * 0x10));
            }
            Stat($"SHDS FP +0E {R.U16(a + 0x0E)} +3C {R.U32(a + 0x3C):X}");
        }

        // +38 (count +30): 0x10 records
        int n38 = R.U16(s + 0x30), n3C = R.U16(s + 0x32), n40 = R.U16(s + 0x34);
        uint at38 = R.U32(s + 0x38), at3C = R.U32(s + 0x3C), at40 = R.U32(s + 0x40);
        if (n38 > 0)
        {
            Claim(at38, n38 * 0x10L, "SHDS +38 records");
            for (int i = 0; i < n38; i++)
            {
                int a = (int)at38 + i * 0x10;
                int names = R.U16(a + 2); uint na = R.U32(a + 4);
                Stat($"SHDS +38 record +00 {R.U16(a)}, {names} names");
                Claim(na, names * 4L, "SHDS +38 name lists");
                for (int k = 0; k < names; k++) Stat($"SHDS +38 name {Str(R.U32((int)na + k * 4))}");
                Claim(R.U32(a + 8), 0x10, "SHDS +38 sub records");
                int sub = (int)R.U32(a + 8);
                Stat($"SHDS +38 sub {R.U16(sub)} {R.U16(sub + 2)} B {Convert.ToHexString(R.Data, (int)R.U32(sub + 8), 4)}");
                Claim(R.U32(sub + 4), R.U16(sub + 2), "SHDS +38 sub data A");
                Claim(R.U32(sub + 8), 4, "SHDS +38 sub data B");
            }
        }
        // +3C (count +32): pointers to variable-size records: u32 name, u32, u32 n, u32 offset of n bytes (inside the record), ...
        if (n3C > 0)
        {
            Claim(at3C, n3C * 4L, "SHDS +3C pointers");
            for (int i = 0; i < n3C; i++)
            {
                int a = (int)R.U32((int)at3C + i * 4);
                Str(R.U32(a));
                long n = R.U32(a + 8), list = R.U32(a + 0x0C);
                Expect(list == a + 0x27, "SHDS +3C record: list at +27");
                Claim(a, 0x27 + n, "SHDS +3C records");
            }
        }
        Stat($"SHDS +32 == material data count: {n3C == R.U16(0xE4 + 2)}");
        // +40 (count +34): 0x20 records: u32 patch block, u32 FP microcode offset, u32 size
        if (n40 > 0)
        {
            Claim(at40, n40 * 0x20L, "SHDS +40 records");
            var fpHeaders = Enumerable.Range(0, fpCount).Select(i => R.U32((int)fpsAt + i * 0x48)).ToHashSet();
            for (int i = 0; i < n40; i++)
            {
                int a = (int)at40 + i * 0x20;
                Expect(fpHeaders.Contains(R.U32(a)), "SHDS +40 record +00 == the header of an FP record (+2C table)");
                Claim(R.U32(a + 4), R.U32(a + 8), "SHDS FP microcode");
                Expect(R.U32(a + 8) % 16 == 0, "SHDS FP microcode size multiple of 16");
                for (int o = 0x0C; o < 0x20; o += 4) if (R.U32(a + o) != 0) Fail("SHDS +40 record +0C..+1F not zero");
            }
            Stat($"SHDS +40 count == FP count: {n40 == fpCount}");
        }
    }

    /// <summary>The textureKeys table (0x8E/0x90) is the concatenation of all material sampler key lists.</summary>
    void SamplerKeyTable()
    {
        int n = R.U16(0x8E); uint at = R.U32(0x90);
        if (n == 0) return;
        // which 8-byte key records lie inside the table: sampler keys of materials, constant keys of material data
        var inside = new HashSet<long>();
        uint matsAt = R.U32(0xE4 + 8), dataAt = R.U32(0xE4 + 0x0C);
        for (int i = 0; i < R.U16(0xE4); i++)
        {
            int a = (int)matsAt + i * 0x34;
            for (int k = 0; k < R.U16(a + 0x0A); k++) inside.Add(R.U32(a + 0x0C) + k * 8L);
        }
        int samplerRecs = inside.Count(o => o >= at && o < at + n * 8L);
        for (int i = 0; i < R.U16(0xE4 + 2); i++)
        {
            int a = (int)dataAt + i * 0x28;
            for (int k = 0; k < R.U16(a + 0x0A); k++) inside.Add(R.U32(a + 0x10) + k * 8L);
        }
        int all = inside.Count(o => o >= at && o < at + n * 8L);
        Expect(all == n, "textureKeys table = exactly the material sampler keys + material constant keys");
        Stat($"textureKeys: {samplerRecs} sampler keys + {all - samplerRecs} constant keys of {n}");
    }

    /// <summary>
    /// Header 0x88 (count 0x2C): shader references, 0x28 each: u32 name, u32 SHDS definition, u32 0, u32 passes (0x20 each, count u16 +10),
    /// u16 +12 (1 iff +14 is set), u32 +14 (8-byte record), u32 +18 constant key count, u32 +1C constant keys, +20/+24 zero.
    /// Pass: u16 n0, u16 n1, u32 list0 (n0 x 8), u32 list1 (n1 x 8), u32 SHDS vertex program record, u32 SHDS fragment program record, u16 x4, u32 0.
    /// </summary>
    void ShaderReferences()
    {
        int n = R.U16(0x2C); uint at = R.U32(0x88);
        if (n == 0) return;
        Claim(at, n * 0x28L, "shader references");
        uint vps = R.U32(0xFC + 0x20), fps = R.U32(0xFC + 0x2C);
        int vpCount = R.U16(0xFC + 0x18), fpCount = R.U16(0xFC + 0x1A);
        for (int i = 0; i < n; i++)
        {
            int a = (int)at + i * 0x28;
            Str(R.U32(a));
            int passes = R.U16(a + 0x10); uint pa = R.U32(a + 0x0C);
            Claim(pa, passes * 0x20L, "shader passes");
            for (int k = 0; k < passes; k++)
            {
                // u16 n0, u16 n1, u32 list0 (n0 x 8), u32 list1 (n1 x 8)
                int p = (int)pa + k * 0x20;
                int n0 = R.U16(p), n1 = R.U16(p + 2);
                Expect(n0 == 0 ? R.U32(p + 4) == 0 : In(R.U32(p + 4), n0 * 8L), "pass list0 present iff n0 > 0");
                Expect(n1 == 0 ? R.U32(p + 8) == 0 : In(R.U32(p + 8), n1 * 8L), "pass list1 present iff n1 > 0");
                if (n0 > 0) Claim(R.U32(p + 4), n0 * 8L, "shader pass lists");
                if (n1 > 0) Claim(R.U32(p + 8), n1 * 8L, "shader pass lists");
                uint vp = R.U32(p + 0x0C), fp = R.U32(p + 0x10);
                Expect(vp >= vps && vp < vps + vpCount * 0x28L && (vp - vps) % 0x28 == 0, "pass +0C -> SHDS vertex program record");
                Expect(fp >= fps && fp < fps + fpCount * 0x48L && (fp - fps) % 0x48 == 0, "pass +10 -> SHDS fragment program record");
            }
            Stat($"shader reference +12 {R.U16(a + 0x12)}");
            uint r14 = R.U32(a + 0x14);
            if (r14 != 0)
            {
                Claim(r14, 8, "shader reference +14 record");
                Expect(R.U32((int)r14) == r14 + 4, "shader ref +14 record +00 points to its +04");
                Stat($"shader ref +14 record +04 {R.U16((int)r14 + 4)} +06 {R.U16((int)r14 + 6)}");
            }
            Expect((R.U16(a + 0x12) == 1) == (r14 != 0), "shader ref +12 == 1 iff +14 record present");
            Expect(R.U32(a + 0x20) == 0 && R.U32(a + 0x24) == 0, "shader reference +20/+24 zero");
        }
    }

    void TextureSet()
    {
        uint t = R.U32(0x48);
        Claim(t, 0x40, "TXS3 header");
        int n = R.U16((int)t + 0x16); uint at = R.U32((int)t + 0x1C);
        Claim(at, n * 0x20L, "TXS3 image records");
        for (int i = 0; i < n; i++)
        {
            int b = (int)at + i * 0x20;
            uint off = R.U32(b), size = R.U32(b + 4);
            if (off != 0 && off + (long)size <= R.Length) Claim(off, size, "image pixels");
        }
    }

    void Bones()
    {
        int n = R.U16(0x1A); uint at = R.U32(0x50);
        if (n == 0) return;
        Claim(at, n * 0x48L, "bones");
        for (int i = 0; i < n; i++) if (R.U32((int)at + i * 0x48 + 0x40) != 0) Str(R.U32((int)at + i * 0x48 + 0x40));
    }

    // ---------------------------------------------------------------- 2f tables
    /// <summary>0x54 (count 0x2A): pointers to named float blocks: u32 name, u16 tag, u16 count, count x f32.</summary>
    void Table54()
    {
        int n = R.U16(0x2A); uint at = R.U32(0x54);
        if (n == 0) return;
        Claim(at, n * 4L, "t54 pointers");
        for (int i = 0; i < n; i++)
        {
            uint p = R.U32((int)at + i * 4);
            if (!Expect(In(p, 8), "t54 block inside")) continue;
            string name = Str(R.U32((int)p));
            int count = R.U16((int)p + 6);
            Claim(p, 8 + count * 4L, "t54 blocks");
            Stat($"t54 {name} tag {R.U16((int)p + 4)} count {count}");
        }
    }

    /// <summary>0x58 (count 0x1E): VM host registers, u32 name, u16 array length, u16 register index.</summary>
    void RegisterTable()
    {
        int n = R.U16(0x1E); uint at = R.U32(0x58);
        if (n == 0) return;
        Claim(at, n * 8L, "VM registers");
        var idx = new List<int>();
        string prev = "";
        bool sorted = true;
        for (int i = 0; i < n; i++)
        {
            string s = Str(R.U32((int)at + i * 8));
            if (string.CompareOrdinal(prev, s) > 0) sorted = false;
            prev = s;
            Stat($"VM register array length {R.U16((int)at + i * 8 + 4)}");
            idx.Add(R.U16((int)at + i * 8 + 6));
        }
        Expect(sorted, "VM registers sorted by name");
        Expect(idx.Distinct().Count() == n, "VM register indices unique");
        Stat(idx.Order().SequenceEqual(Enumerable.Range(0, n)) ? "VM register indices are 0..n-1" : $"VM register indices with gaps (max {idx.Max()}, n {n})");
    }

    /// <summary>0x5C (count 0x22): VM outputs read by the game (wiper UVs): u32 name, u32 0x10000 | slot.</summary>
    void Table5C()
    {
        int n = R.U16(0x22); uint at = R.U32(0x5C);
        if (n == 0) return;
        Claim(at, n * 8L, "VM outputs");
        for (int i = 0; i < n; i++)
        {
            Str(R.U32((int)at + i * 8));
            uint v = R.U32((int)at + i * 8 + 4);
            Expect(v >> 16 == 1, "VM output value high half = 1");
            Expect((v & 0xFFFF) < R.U16(0x20), "VM output slot < VM stack size (0x20)");
        }
    }

    /// <summary>0x64: VM bytecode, 0x60 = code length, 0x68 = end of the VM block (code + zeroed storage).</summary>
    void VmCode()
    {
        uint at = R.U32(0x64), len = R.U32(0x60);
        if (at == 0) return;
        Claim(at, len, "VM bytecode");
    }

    /// <summary>
    /// 0x78 (count 0x26): per material, u16 material, u16 n, 12 bytes 0, u32 offset of n x 12 bytes runtime space.
    /// One known exception: GT5 car\hq\00010101 has a last record with material == material count (83).
    /// </summary>
    void Table78()
    {
        int n = R.U16(0x26); uint at = R.U32(0x78);
        if (n == 0) return;
        Claim(at, n * 0x14L, "t78 records");
        for (int i = 0; i < n; i++)
        {
            int a = (int)at + i * 0x14;
            Expect(R.U16(a) < R.U16(0xE4), "t78 material index < material count");
            if (i > 0) Expect(R.U16(a) > R.U16(a - 0x14), "t78 material indices strictly increasing");
            int k = R.U16(a + 2);
            Expect(R.U32(a + 4) == 0 && R.U32(a + 8) == 0 && R.U32(a + 12) == 0, "t78 +04..+0F zero");
            Claim(R.U32(a + 0x10), k * 12L, "t78 runtime space");
        }
    }

    /// <summary>
    /// 0x94 (no count field): GT5 packed-geometry stream block (probably PS3 Edge geometry descriptors, item 4).
    /// Header: u16 n1, n2, n3, n4, n5, 0, u16, 0; u32 p1 (n1 x 0x20), p2 (n2 pointers to 0x20 records),
    /// p3 (n3 x 0x20, each with +04 size / +08 offset of a stream description), p4 (n4 x (u32 count, u32 offset) RSX attribute formats),
    /// p5 (n5 x (u32 count, u32 offset)), p6, p7 (runtime space); u16 x4.
    /// </summary>
    void Block94()
    {
        uint h = R.U32(0x94);
        if (h == 0) return;
        Claim(h, 0x34, "t94 header");
        int n1 = R.U16((int)h), n2 = R.U16((int)h + 2), n3 = R.U16((int)h + 4), n4 = R.U16((int)h + 6), n5 = R.U16((int)h + 8);
        uint p1 = R.U32((int)h + 0x10), p2 = R.U32((int)h + 0x14), p3 = R.U32((int)h + 0x18), p4 = R.U32((int)h + 0x1C), p5 = R.U32((int)h + 0x20);
        Stat($"t94 n4 == n1: {n4 == n1}, n5 == n3: {n5 == n3}");
        Claim(p1, n1 * 0x20L, "t94 p1 records");
        Claim(p2, n2 * 4L, "t94 p2 pointers");
        for (int i = 0; i < n2; i++) Claim(R.U32((int)p2 + i * 4), 0x20, "t94 p2 records");
        Claim(p3, n3 * 0x20L, "t94 p3 records");
        for (int i = 0; i < n3; i++)
        {
            int a = (int)p3 + i * 0x20;
            Claim(R.U32(a + 8), R.U32(a + 4), "t94 p3 stream descriptions");
        }
        Claim(p4, n4 * 8L, "t94 p4 lists");
        for (int i = 0; i < n4; i++) Claim(R.U32((int)p4 + i * 8 + 4), R.U32((int)p4 + i * 8) * 4L, "t94 p4 attribute formats");
        Claim(p5, n5 * 8L, "t94 p5 lists");
        for (int i = 0; i < n5; i++) Claim(R.U32((int)p5 + i * 8 + 4), R.U32((int)p5 + i * 8) * 12L, "t94 p5 entries");
        uint p6 = R.U32((int)h + 0x24), p7 = R.U32((int)h + 0x28);
        Stat($"t94 p7 - p6 = {p7 - p6:X}, p6 value {R.F32((int)p6)}");
        Claim(p6, p7 - p6, "t94 p6");
    }

    /// <summary>0xB0: one 0x40 record per model: +00 offset of a u16 list (count +30) = the sorted materials the model draws.</summary>
    void ModelMaterialLists()
    {
        uint at = R.U32(0xB0);
        int n = R.U16(0x10);
        if (at == 0) return;
        Expect(R.U32(0xB4) == R.U32(0x30), "header 0xB4 == models offset 0x30");
        Claim(at, n * 0x40L, "model material lists");
        uint matsAt = R.U32(0xE4 + 8), shapesAt = R.U32(0x38);
        for (int i = 0; i < n; i++)
        {
            int a = (int)at + i * 0x40;
            int count = R.U16(a + 0x30);
            uint lo = R.U32(a);
            var list = new List<int>();
            if (count > 0) { Claim(lo, count * 2L, "model material list entries"); for (int k = 0; k < count; k++) list.Add(R.U16((int)lo + k * 2)); }
            int m = (int)R.U32(0x30) + i * 0x30;
            var shapes = Mdl3Command.Decode(R, (int)R.U32(m + 0x18), (int)R.U32(m + 0x1C)).SelectMany(c => c.Shapes).Distinct();
            var mats = shapes.Select(s => (int)R.S16((int)shapesAt + s * 0x30 + 4)).Distinct().Order().ToList();
            Expect(mats.SequenceEqual(list), "model material list == sorted materials of the shapes the model draws");
            for (int o = 4; o < 0x28; o += 4) if (R.U32(a + o) != 0) Fail("model material list record +04..+27 not zero");
            Stat($"model material list +28 {R.U16(a + 0x28)} +2A {R.U16(a + 0x2A)} +2C {R.U16(a + 0x2C)} +2E {R.U16(a + 0x2E)} vs count {count}");
        }
    }

    /// <summary>
    /// 0xB8: material parameter animation. Header 0x20: u16 n4, u16 n8, u32 p4, u32 p8, i32 vm, i32 vm, u32 p14, u16 n14, u16 bindings, u16, u16.
    /// p4: n4 x 0x10 (u32 name, i16 VM storage index or -1, i16 rows, u32 rows offset, f32 default); row 0x10:
    /// u16 material data, u16 constant slot, u16 binding index, u16 0, u16 curve x4 (0xFFFF none).
    /// p8: n8 x 8 (u16, u16 point count, u32 offset of (x, y) f32 pairs) = piecewise linear curves.
    /// p14: n14 x 0x10 named entries.
    /// </summary>
    void MaterialAnimation()
    {
        uint h = R.U32(0xB8);
        if (h == 0) return;
        Claim(h, 0x20, "material animation header");
        int n4 = R.U16((int)h), n8 = R.U16((int)h + 2);
        uint p4 = R.U32((int)h + 4), p8 = R.U32((int)h + 8), p14 = R.U32((int)h + 0x14);
        int n14 = R.U16((int)h + 0x18), bindings = R.U16((int)h + 0x1A);
        int dataCount = R.U16(0xE4 + 2);
        Claim(p4, n4 * 0x10L, "material animation params");
        int maxBinding = -1;
        for (int i = 0; i < n4; i++)
        {
            int a = (int)p4 + i * 0x10;
            Str(R.U32(a));
            int rows = R.S16(a + 6); uint ro = R.U32(a + 8);
            if (rows > 0) Claim(ro, rows * 0x10L, "material animation rows");
            for (int k = 0; k < rows; k++)
            {
                int r = (int)ro + k * 0x10;
                Expect(R.U16(r) < dataCount, "anim row material data < data count");
                maxBinding = Math.Max(maxBinding, R.U16(r + 4));
                for (int c = 0; c < 4; c++) { int cv = R.U16(r + 8 + c * 2); if (cv != 0xFFFF) Expect(cv < n8, "anim row curve < curve count"); }
                Stat($"anim row +06 {R.U16(r + 6)}");
            }
        }
        if (n4 > 0) Expect(maxBinding + 1 == bindings, "anim header +1A == max binding index + 1");
        Claim(p8, n8 * 8L, "material animation curves");
        for (int i = 0; i < n8; i++)
        {
            int a = (int)p8 + i * 8;
            Claim(R.U32(a + 4), R.U16(a + 2) * 8L, "material animation curve points");
        }
        Claim(p14, n14 * 0x10L, "material animation +14 entries");
        for (int i = 0; i < n14; i++) Str(R.U32((int)p14 + i * 0x10));
    }

    /// <summary>0xBC: 0x20 bytes, VM entry points (+00, +04) and values.</summary>
    void VmContext()
    {
        uint h = R.U32(0xBC);
        if (h == 0) return;
        Claim(h, 0x20, "VM context");
        Stat($"VM context +10 {R.U32((int)h + 0x10):X} +14 {R.U32((int)h + 0x14):X} +18 {R.U32((int)h + 0x18):X} +1C {R.U32((int)h + 0x1C):X}");
        // +08 u16 count, +0C list: curves (u16 type, u16 point count, u32 offset of (x, y) f32 pairs)
        uint list = R.U32((int)h + 0x0C);
        int n = R.U16((int)h + 8);
        Expect(n == 0 ? list == 0 : In(list, n * 8L), "VM context curve list present iff +08 count > 0");
        if (n > 0)
        {
            Claim(list, n * 8L, "VM context curves");
            for (int i = 0; i < n; i++)
            {
                int e = (int)list + i * 8;
                Stat($"VM context curve type {R.U16(e)} points {R.U16(e + 2)}");
                if (R.U16(e + 2) > 0) Claim(R.U32(e + 4), R.U16(e + 2) * 8L, "VM context curve points");
            }
        }
    }

    /// <summary>0x9C (count 0x98): joint constraints, 0x80 each (PDTools: "wing data"); keys at 0xA0 (count 0x9A).</summary>
    void WingData()
    {
        int n = R.U16(0x98); uint at = R.U32(0x9C);
        if (n > 0) Claim(at, n * 0x80L, "joint constraints");
        Keys("joint keys", 0x9A, 0xA0, n);
    }

    /// <summary>0xA4 (count 0x28): pointers to 0x280-byte rigid body blocks (box, inertia tensor, edge list).</summary>
    void TableA4()
    {
        int n = R.U16(0x28); uint at = R.U32(0xA4);
        if (n == 0) return;
        Claim(at, n * 4L, "rigid body pointers");
        for (int i = 0; i < n; i++) Claim(R.U32((int)at + i * 4), 0x280, "rigid bodies");
    }

    /// <summary>
    /// 0xC0 (count 0xC4, v8): pointers to soft-body (FEM damage?) blocks. Block header 0x40: u16 nodes, u16, u16, u16 tets, u16,
    /// u16 tris, u16 n6, u16 n7; u32 value; offsets: nodes (vec3), masses?, tet matrices (0x1B0 each), constants (0x50), tris (u16 x3), list (u16 x n6).
    /// </summary>
    void TableC0()
    {
        int n = R.U16(0xC4); uint at = R.U32(0xC0);
        if (n == 0) return;
        Claim(at, n * 4L, "soft body pointers");
        for (int i = 0; i < n; i++)
        {
            int p = (int)R.U32((int)at + i * 4);
            Claim(p, 0x40, "soft body header");
            int nodes = R.U16(p), tets = R.U16(p + 6), tris = R.U16(p + 0x0A), n6 = R.U16(p + 0x0C);
            Claim(R.U32(p + 0x14), nodes * 12L, "soft body nodes");
            Claim(R.U32(p + 0x18), nodes * 4L, "soft body node values");
            Claim(R.U32(p + 0x1C), tets * 0x1B0L, "soft body tet matrices");
            Claim(R.U32(p + 0x20), 0x50, "soft body constants");
            Claim(R.U32(p + 0x24), tris * 6L, "soft body triangles");
            Claim(R.U32(p + 0x28), n6 * 2L, "soft body list");
            Expect(R.U32(p + 0x3C) == p, "soft body +3C == own offset");
        }
    }

    /// <summary>v14: PMSH header at 0xD0 and packed mesh keys at 0xCC (count 0xCA).</summary>
    void PackedMesh()
    {
        uint h = R.U32(0xD0);
        if (h == 0) return;
        Expect(R.Magic((int)h) == "PMSH", "PMSH magic");
        Claim(h, 0x40, "PMSH header");
        int meshes = R.U16((int)h + 0x10), n1C = R.U16((int)h + 0x12), layouts = R.U16((int)h + 0x14), decls = R.U16((int)h + 0x16);
        uint meshAt = R.U32((int)h + 0x18), at1C = R.U32((int)h + 0x1C), layoutAt = R.U32((int)h + 0x20), declAt = R.U32((int)h + 0x24);
        Claim(meshAt, meshes * 0x30L, "PMSH meshes");
        for (int i = 0; i < meshes; i++)
        {
            // +00 i16 declaration, +02 i16 bit layout array, +06 u8 data count, +08 0x10 record, +10 smooth mesh data (0x30 each), +14 bbox (8 corners)
            int a = (int)meshAt + i * 0x30;
            int decl = R.S16(a), layout = R.S16(a + 2);
            Expect(decl < decls && layout < layouts, "PMSH mesh declaration / layout index in range");
            Stat($"PMSH mesh +04 {R.S16(a + 4)} +06 {R.U8(a + 6)} +07 {R.U8(a + 7)} +08 set {R.U32(a + 8) != 0} +10 set {R.U32(a + 0x10) != 0} +14 set {R.U32(a + 0x14) != 0}");
            // +08: +06 x +07 entries of 8 bytes; +14: one bbox (0x60) per smooth mesh data
            if (R.U32(a + 8) != 0) Claim(R.U32(a + 8), R.U8(a + 6) * R.U8(a + 7) * 8L, "PMSH mesh +08 records");
            if (R.U32(a + 0x14) != 0) Claim(R.U32(a + 0x14), R.U8(a + 6) * 0x60L, "PMSH mesh bboxes");
            if (R.U32(a + 0x10) == 0) continue;
            Claim(R.U32(a + 0x10), R.U8(a + 6) * 0x30L, "PMSH smooth mesh data");
            for (int k = 0; k < R.U8(a + 6); k++)
            {
                // PDTools PMSHSmoothMeshData: +00 packed vertices, +0C raw vertices, +14 packed indices, +18 commands;
                // +22 vertex count (both vertex arrays), +26 output vertex count (>= +22, larger when the SmoothMesh job adds vertices),
                // +2A index quadwords, +2C command quadwords, +2E index bit size
                int s = (int)R.U32(a + 0x10) + k * 0x30;
                int flags = R.U16(s + 0x20);
                Stat($"PMSH smooth +04 set {R.U32(s + 4) != 0} +08 set {R.U32(s + 8) != 0} +10 set {R.U32(s + 0x10) != 0} +20 {flags:X} +24 set {R.U16(s + 0x24) != 0} +28 {R.U16(s + 0x28)}");
                int packedCount = R.U16(s + 0x22), rawCount = packedCount;
                Expect(R.U16(s + 0x26) >= packedCount, "PMSH smooth +26 output count >= +22 vertex count");
                // +08: +24 entries of 10 bytes; +10: +28 quadwords, then per-vertex extra streams of 1 byte (flag 0x40) and
                // 4 bytes (flag 0x80), each padded to 16 bytes (the padding is not always zero)
                Expect((R.U16(s + 0x24) != 0) == (R.U32(s + 8) != 0), "PMSH smooth +08 set iff +24 count > 0");
                if (R.U32(s + 8) != 0) Claim(R.U32(s + 8), R.U16(s + 0x24) * 10L, "PMSH smooth +08 entries");
                static long Pad16(long n) => (n + 15) & ~15L;
                long size10 = R.U16(s + 0x28) * 16L + ((flags & 0x40) != 0 ? Pad16(packedCount) : 0) + ((flags & 0x80) != 0 ? Pad16(packedCount * 4L) : 0);
                Expect((size10 != 0) == (R.U32(s + 0x10) != 0), "PMSH smooth +10 set iff +28 or flags 0x40/0x80");
                if (R.U32(s + 0x10) != 0) Claim(R.U32(s + 0x10), size10, "PMSH smooth +10 streams");
                if (R.U32(s + 0x14) != 0) Claim(R.U32(s + 0x14), R.U16(s + 0x2A) * 16L, "PMSH packed indices");
                if (R.U32(s + 0x18) != 0) Claim(R.U32(s + 0x18), R.U16(s + 0x2C) * 16L, "PMSH smooth commands");
                if (decl >= 0 && decl < decls && R.U32(s + 0x0C) != 0)
                    Claim(R.U32(s + 0x0C), rawCount * (long)R.U8((int)declAt + decl * 0x10 + 3), "PMSH raw vertices");
                if (layout >= 0 && layout < layouts && R.U32(s) != 0)
                {
                    // one bit-packed array per layout: ceil(count * bits / 8) bytes each
                    int la = (int)layoutAt + layout * 8;
                    long size = 0;
                    for (int l = 0; l < (int)R.U32(la); l++) size += (packedCount * (long)R.U32((int)R.U32(la + 4) + l * 0x30) + 7) / 8;
                    Claim(R.U32(s), size, "PMSH packed vertices");
                }
            }
        }
        Claim(at1C, n1C * 8L, "PMSH 0x1C entries");
        for (int i = 0; i < n1C; i++)
        {
            // u8, u8 count, u16 data size, u32 data offset (PDTools PMSHMeshUnk0x1C)
            int a = (int)at1C + i * 8;
            Stat($"PMSH 0x1C entry +00 {R.U8(a)} +01 {R.U8(a + 1)}");
            if (R.U32(a + 4) != 0) Claim(R.U32(a + 4), R.U16(a + 2), "PMSH 0x1C data");
        }
        Claim(layoutAt, layouts * 8L, "PMSH bit layout arrays");
        for (int i = 0; i < layouts; i++) Claim(R.U32((int)layoutAt + i * 8 + 4), R.U32((int)layoutAt + i * 8) * 0x30L, "PMSH bit layouts");
        uint at30 = R.U32((int)h + 0x30); int n34 = (int)R.U32((int)h + 0x34);
        Expect(n34 == 0 ? at30 == 0 : In(at30, n34 * 0x20L), "PMSH +30 records present iff +34 count > 0");
        if (n34 > 0) Claim(at30, n34 * 0x20L, "PMSH +30 records");
        Stat($"PMSH +04 {R.U16((int)h + 4)} {R.U16((int)h + 6)}, +0C == header 0x6C: {R.U32((int)h + 0x0C) == R.U32(0x6C)}, +1C count {n1C}");
        Expect(R.U32((int)h + 8) == h, "PMSH +08 == own offset");
        Claim(declAt, decls * 0x10L, "PMSH vertex declarations");
        for (int i = 0; i < decls; i++)
        {
            int a = (int)declAt + i * 0x10;
            int count = R.U8(a) + R.U8(a + 1);
            Claim(R.U32(a + 8), count * 4L, "PMSH declaration elements");
            uint names = R.U32(a + 0x0C);
            Claim(names, count * 4L, "PMSH declaration element names");
            for (int k = 0; k < count; k++) Str(R.U32((int)names + k * 4));
        }
        // packed mesh keys (0xCA/0xCC): (shader family name, material data +04 index) pairs
        int nk = R.U16(0xCA); uint ka = R.U32(0xCC);
        Claim(ka, nk * 8L, "packedMeshKeys");
        uint dataAt = R.U32(0xE4 + 0x0C);
        var dataKeys = Enumerable.Range(0, R.U16(0xE4 + 2)).Select(i => (R.CString((int)R.U32((int)dataAt + i * 0x28)), R.U32((int)dataAt + i * 0x28 + 4))).ToHashSet();
        for (int i = 0; i < nk; i++)
        {
            string name = Str(R.U32((int)ka + i * 8));
            Expect(dataKeys.Contains((name, R.U32((int)ka + i * 8 + 4))), "packedMeshKey == (name, +04 index) of a material data");
        }
    }

    /// <summary>
    /// v14 header 0xDC: shape stream map (data in GT6 body_s, item 5). Header 0x20: u32 group count, u16, u16, u32 groups.
    /// Group 0xC: u32 name, u32 n, u32 items (n x 0x18). Item: stream (0x10) + u32 m, u32 sub streams (m x 0x10).
    /// Stream: u32 0, u32 value, u16 c1, u16 c2, u32 chunk list ((c1 + c2) x 8: u32 0x80000000|size or size, u32 offset).
    /// </summary>
    void StreamMap()
    {
        uint h = R.U32(0xDC);
        if (h == 0 || Version < 14) return;
        Claim(h, 0x20, "stream map header");
        int groups = (int)R.U32((int)h); uint ga = R.U32((int)h + 8);
        Stat($"stream map header u16 {R.U16((int)h + 4)} {R.U16((int)h + 6)}, groups {groups}");
        Claim(ga, groups * 0xCL, "stream map groups");
        void Stream(int s, string label)
        {
            int pairs = R.U16(s + 8) + R.U16(s + 0x0A);
            Expect(R.U32(s) == 0, "stream map stream +00 zero");
            if (pairs > 0) Claim(R.U32(s + 0x0C), pairs * 8L, "stream map chunk lists");
            for (int k = 0; k < pairs; k++)
            {
                int p = (int)R.U32(s + 0x0C) + k * 8;
                uint a = R.U32(p), b = R.U32(p + 4);
                if ((a & 0x80000000) != 0) { Expect((a & 0x7FFF0000) == 0 && b % 0x80 == 0, "stream map compressed chunk: u16 size, offset 0x80-aligned"); }
                else Stat($"stream map plain pair a==b {a == b}");
            }
        }
        for (int g = 0; g < groups; g++)
        {
            int ge = (int)ga + g * 0xC;
            Str(R.U32(ge));
            int n = (int)R.U32(ge + 4); uint ia = R.U32(ge + 8);
            Claim(ia, n * 0x18L, "stream map items");
            for (int i = 0; i < n; i++)
            {
                int it = (int)ia + i * 0x18;
                Stream(it, "item");
                int m = (int)R.U32(it + 0x10); uint sa = R.U32(it + 0x14);
                if (m > 0) Claim(sa, m * 0x10L, "stream map sub streams");
                for (int k = 0; k < m; k++) Stream((int)sa + k * 0x10, "sub");
            }
        }
    }
}
