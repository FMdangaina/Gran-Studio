using System.Numerics;
using System.Text.RegularExpressions;
using GTFormats;
using GTFormats.Mdl3;

/// <summary>
/// gtdev mdl3-fvf &lt;files.tsv&gt; &lt;groupRegex&gt; &lt;out.txt&gt;
/// Checks every flexible vertex format (elements inside the vertex, no overlaps, known types), lists attribute
/// names/types, and for shapes with a plain vertex buffer decodes the positions and compares them with the shape bbox.
/// </summary>
static class Mdl3FvfCheck
{
    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.WriteLine("mdl3-fvf <files.tsv> <groupRegex> <out.txt>"); return 1; }
        var rx = new Regex(args[1]);
        var files = File.ReadAllLines(args[0]).Select(l => l.Split('\t')).Where(p => rx.IsMatch(p[1])).ToList();
        using var w = new StreamWriter(args[2]);

        foreach (var ver in files.GroupBy(p => p[2]).OrderBy(g => int.Parse(g.Key)))
        {
            int fvfs = 0, outside = 0, overlap = 0, unknownType = 0, withArray = 0;
            var sizes = new Dictionary<int, int>();
            var attrs = new Dictionary<string, int>();      // "name type x count"
            var unk04 = new Dictionary<uint, int>();
            var gaps = new Dictionary<string, int>();       // per array: vertex size - sum of element sizes
            var arrayDefs = new Dictionary<string, int>();
            // plain shape checks
            int plain = 0, plainBadFvf = 0, plainPastEnd = 0, inBox = 0, tight = 0, noPos = 0, noBox = 0;
            var posTypes = new Dictionary<string, int>();
            var examples = new List<string>();
            var outsideKinds = new Dictionary<string, int>();
            var arrayShapes = new Dictionary<string, int>();
            var nLow = new List<float>(); var nHigh = new List<float>();
            var uvRange = new Dictionary<string, (float lo, float hi)>();

            foreach (var p in ver)
            {
                var f = Mdl3File.Load(p[0]);
                foreach (var fvf in f.Fvfs)
                {
                    fvfs++;
                    sizes[fvf.VertexSize] = sizes.GetValueOrDefault(fvf.VertexSize) + 1;
                    unk04[fvf.Unk04] = unk04.GetValueOrDefault(fvf.Unk04) + 1;
                    bool hasArrays = fvf.Elements.Any(e => e.ArrayIndex != 0);
                    if (hasArrays) withArray++;
                    string ak = $"{(hasArrays ? "arrays" : "no arrays")}: len {fvf.ArrayLength} elemSize {fvf.ArrayElementSize:X} vsize {fvf.ArrayVertexSize:X} default {fvf.ArrayVertexSizeDefault:X} dataOff {(fvf.ArrayDataOffset == 0 ? "0" : "set")}";
                    arrayDefs[ak] = arrayDefs.GetValueOrDefault(ak) + 1;

                    // Each array index is its own vertex: index 0 uses VertexSize, the others ArrayVertexSize.
                    foreach (var arr in fvf.Elements.GroupBy(e => e.ArrayIndex))
                    {
                        int size = arr.Key == 0 ? fvf.VertexSize : fvf.ArrayVertexSize;
                        var used = new bool[256];
                        int sum = 0;
                        foreach (var e in arr)
                        {
                            string k = $"{e.Name} {e.Type}x{e.Count}" + (e.ArrayIndex != 0 ? $" arr{e.ArrayIndex}" : "");
                            attrs[k] = attrs.GetValueOrDefault(k) + 1;
                            if (e.ByteSize < 0) { unknownType++; continue; }
                            sum += e.ByteSize;
                            if (e.Start + e.ByteSize > size) outside++;
                            for (int b = e.Start; b < Math.Min(256, e.Start + e.ByteSize); b++)
                            {
                                if (used[b]) { overlap++; break; }
                                used[b] = true;
                            }
                        }
                        string gk = $"arr{arr.Key}:{size - sum}";
                        gaps[gk] = gaps.GetValueOrDefault(gk) + 1;
                    }
                }

                foreach (var s in f.Shapes.Where(s => s.VertexCount > 0 && s.VerticesOffset > 0))
                {
                    plain++;
                    if (s.FvfIndex < 0 || s.FvfIndex >= f.Fvfs.Count) { plainBadFvf++; continue; }
                    var fvf = f.Fvfs[s.FvfIndex];
                    if (s.VerticesOffset + s.VertexCount * fvf.VertexSize > f.Data.Length) { plainPastEnd++; continue; }
                    var pos = fvf["position"] ?? fvf["position_1"];
                    if (pos == null) { noPos++; continue; }
                    long last = fvf.Elements.Max(e => fvf.ElementOffset(e, (int)s.VertexCount - 1) + e.ByteSize);
                    if (s.VerticesOffset + last > f.Data.Length) { plainPastEnd++; continue; }
                    posTypes[$"{pos.Type}x{pos.Count}"] = posTypes.GetValueOrDefault($"{pos.Type}x{pos.Count}") + 1;
                    if (s.BBoxOffset == 0) { noBox++; continue; }

                    var (bmin, bmax) = Box(f.Data, (int)s.BBoxOffset);
                    var vmin = new Vector3(float.MaxValue); var vmax = new Vector3(float.MinValue);
                    float eps = 1e-3f * Math.Max(1f, (bmax - bmin).Length());
                    int out_ = 0;
                    for (int v = 0; v < s.VertexCount; v++)
                    {
                        var q = Decode(f.Data, (int)(s.VerticesOffset + fvf.ElementOffset(pos, v)), pos);
                        vmin = Vector3.Min(vmin, q); vmax = Vector3.Max(vmax, q);
                        if (Vector3.Min(q - bmin, bmax - q) is var d && Math.Min(d.X, Math.Min(d.Y, d.Z)) < -eps) out_++;
                    }
                    bool isTight = (vmin - bmin).Length() <= eps && (bmax - vmax).Length() <= eps;
                    if (out_ == 0) inBox++;
                    if (isTight) tight++;
                    // Normals: length of the decoded CMP vector for two bit orders (x in the low bits / x in the high bits).
                    var nrm = fvf["normal"];
                    if (nrm is { Type: RsxVertexType.CMP })
                        for (int v = 0; v < s.VertexCount; v++)
                        {
                            uint raw = f.Data.U32((int)(s.VerticesOffset + fvf.ElementOffset(nrm, v)));
                            nLow.Add(Math.Abs(Cmp(raw).Length() - 1));
                            nHigh.Add(Math.Abs(CmpHigh(raw).Length() - 1));
                        }
                    // UVs: value range per element type.
                    foreach (var uv in fvf.Elements.Where(e => e.Name.StartsWith("map")))
                        for (int v = 0; v < s.VertexCount; v += 4)
                        {
                            var q = Decode(f.Data, (int)(s.VerticesOffset + fvf.ElementOffset(uv, v)), uv);
                            string k = $"{uv.Name} {uv.Type}x{uv.Count}";
                            var (lo, hi) = uvRange.GetValueOrDefault(k, (float.MaxValue, float.MinValue));
                            uvRange[k] = (Math.Min(lo, Math.Min(q.X, q.Y)), Math.Max(hi, Math.Max(q.X, q.Y)));
                        }

                    if (fvf.Elements.Any(e => e.ArrayIndex != 0) && f.ShapeNames.TryGetValue(f.Shapes.IndexOf(s), out var an))
                    {
                        var parts = an[0].Split('|');
                        string node = string.Join("|", parts.Skip(Math.Max(0, parts.Length - 3)));
                        arrayShapes[node] = arrayShapes.GetValueOrDefault(node) + 1;
                    }
                    string key = out_ == 0 ? "inside" : $"outside, arrays={fvf.Elements.Any(e => e.ArrayIndex != 0)}, fvf={fvf.Name}";
                    outsideKinds[key] = outsideKinds.GetValueOrDefault(key) + 1;
                    if (out_ > 0 && examples.Count < 8)
                        examples.Add($"{Path.GetFileName(Path.GetDirectoryName(p[0]))}\\{Path.GetFileName(p[0])} shape {f.Shapes.IndexOf(s)} fvf {fvf.Name} {out_}/{s.VertexCount} verts out: verts {vmin}..{vmax} box {bmin}..{bmax} {(f.ShapeNames.TryGetValue(f.Shapes.IndexOf(s), out var nm) ? nm[0] : "")}");
                }
            }

            w.WriteLine($"================ version {ver.Key}: {ver.Count()} files, {fvfs} vertex formats");
            w.WriteLine($"elements past the vertex size: {outside}; overlapping: {overlap}; unknown type: {unknownType}; with array def: {withArray}");
            w.WriteLine("vertex sizes: " + string.Join(" ", sizes.OrderBy(k => k.Key).Select(k => $"{k.Key:X}:{k.Value}")));
            w.WriteLine("unused bytes per array (size - sum of elements): " + string.Join(" ", gaps.OrderBy(k => k.Key).Select(k => $"{k.Key}x{k.Value}")));
            w.WriteLine("array definitions:");
            foreach (var a in arrayDefs.OrderByDescending(k => k.Value).Take(12)) w.WriteLine($"  {a.Value,6}  {a.Key}");
            w.WriteLine("unk04: " + string.Join(" ", unk04.OrderByDescending(k => k.Value).Take(8).Select(k => $"{k.Key:X}:{k.Value}")));
            w.WriteLine("attributes:");
            foreach (var a in attrs.OrderBy(k => k.Key)) w.WriteLine($"  {a.Key,-40} {a.Value}");
            w.WriteLine($"plain shapes {plain}: bad fvf {plainBadFvf}, buffer past end {plainPastEnd}, no position {noPos}, no bbox {noBox}");
            w.WriteLine("  position types: " + string.Join(" ", posTypes.Select(k => $"{k.Key}:{k.Value}")));
            w.WriteLine($"  positions inside bbox: {inBox}; bbox tight (min/max equal): {tight}");
            foreach (var k in outsideKinds.OrderByDescending(k => k.Value).Take(10)) w.WriteLine($"  {k.Value,6}  {k.Key}");
            foreach (var e in examples) w.WriteLine($"  example: {e}");
            if (nLow.Count > 0)
            {
                string Stat(List<float> l) { l.Sort(); return $"median |len-1| {l[l.Count / 2]:0.0000}, 99% {l[(int)(l.Count * 0.99)]:0.0000}, within 0.01: {l.Count(x => x < 0.01f) * 100.0 / l.Count:0.0}%"; }
                w.WriteLine($"normals CMP ({nLow.Count} vertices): x low bits: {Stat(nLow)}");
                w.WriteLine($"                              x high bits: {Stat(nHigh)}");
            }
            w.WriteLine("UV ranges (every 4th vertex):");
            foreach (var u in uvRange.OrderBy(k => k.Key)) w.WriteLine($"  {u.Key,-20} {u.Value.lo,10:0.###} .. {u.Value.hi,10:0.###}");
            w.WriteLine($"plain shapes with extra arrays: {arrayShapes.Values.Sum()}, most common nodes:");
            foreach (var a in arrayShapes.OrderByDescending(k => k.Value).Take(15)) w.WriteLine($"  {a.Value,5}  {a.Key}");
            w.WriteLine();
        }
        Console.WriteLine($"{files.Count} files -> {args[2]}");
        return 0;
    }

    static (Vector3 min, Vector3 max) Box(BigEndianReader r, int at)
    {
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var v = new Vector3(r.F32(at + i * 12), r.F32(at + i * 12 + 4), r.F32(at + i * 12 + 8));
            min = Vector3.Min(min, v); max = Vector3.Max(max, v);
        }
        return (min, max);
    }

    static Vector3 Decode(BigEndianReader r, int at, Mdl3FvfElement e) => e.Type switch
    {
        RsxVertexType.F => new Vector3(r.F32(at), r.F32(at + 4), e.Count > 2 ? r.F32(at + 8) : 0),
        RsxVertexType.SF => new Vector3((float)r.F16(at), (float)r.F16(at + 2), e.Count > 2 ? (float)r.F16(at + 4) : 0),
        RsxVertexType.S1 => new Vector3(r.S16(at) / 32767f, r.S16(at + 2) / 32767f, e.Count > 2 ? r.S16(at + 4) / 32767f : 0),
        RsxVertexType.S32K => new Vector3(r.S16(at), r.S16(at + 2), e.Count > 2 ? r.S16(at + 4) : 0),
        RsxVertexType.CMP => Cmp(r.U32(at)),
        _ => Vector3.Zero,
    };

    // RSX CMP: x = bits 0-10, y = 11-21 (signed 11 bit), z = 22-31 (signed 10 bit), normalized.
    static Vector3 Cmp(uint v)
    {
        int x = (int)(v << 21) >> 21, y = (int)(v << 10) >> 21, z = (int)v >> 22;
        return new Vector3(x / 1023f, y / 1023f, z / 511f);
    }

    // Alternative order: x = bits 21-31, y = 10-20 (signed 11 bit), z = 0-9 (signed 10 bit).
    static Vector3 CmpHigh(uint v)
    {
        int x = (int)v >> 21, y = (int)(v << 11) >> 21, z = (int)(v << 22) >> 22;
        return new Vector3(x / 1023f, y / 1023f, z / 511f);
    }
}
