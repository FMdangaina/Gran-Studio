using System.Globalization;
using System.Text;
using GTFormats;
using GTFormats.Mdl3;
using GTFormats.Txs3;

/// <summary>
/// mdl3-obj &lt;file&gt; &lt;out.obj&gt;: export the plain and GT5 packed shapes as an OBJ, one group per shape, with an .mtl
/// and the diffuse textures as PNG. Research export to look at the geometry. Also writes &lt;out&gt;.txt with per-shape stats.
/// </summary>
static class Mdl3Obj
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("mdl3-obj <file> <out.obj>  |  mdl3-obj <files.tsv> <groupRegex> (rank files by plain vertices)");
            return 1;
        }
        if (args[0].EndsWith(".tsv", StringComparison.OrdinalIgnoreCase)) return Rank(args[0], args[1]);
        var m = Mdl3File.Load(args[0]);
        var r = m.Data;
        var obj = new StringBuilder();
        var log = new StringBuilder();
        var ci = CultureInfo.InvariantCulture;
        int vBase = 1, vtBase = 1, vnBase = 1, exported = 0, skipped = 0, packed = 0;
        float[]? frame = null;
        string mtlName = Path.GetFileNameWithoutExtension(args[1]) + ".mtl";
        obj.AppendLine($"mtllib {mtlName}");
        var usedMaterials = new SortedSet<int>();

        for (int s = 0; s < m.Shapes.Count; s++)
        {
            var sh = m.Shapes[s];
            string name = m.ShapeNames.TryGetValue(s, out var n) ? n[0] : $"shape{s}";
            if (Mdl3PackedShape.IsPacked(r, sh))
            {
                try
                {
                    frame ??= Mdl3PackedShape.CarFrame(r);
                    var ps = Mdl3PackedShape.Read(r, sh);
                    var uvAttr = PackedUv(m, sh, ps);
                    var nAttr = ps.Desc.Input[2] is { Type: 4 } na ? na : null;
                    obj.AppendLine($"g s{s:D4}_{Root(name)}_{Short(name)}");
                    if (sh.MaterialIndex >= 0) { obj.AppendLine($"usemtl m{sh.MaterialIndex:D3}"); usedMaterials.Add(sh.MaterialIndex); }
                    int triSum = 0;
                    foreach (var seg in ps.Segments)
                    {
                        var p = ps.Positions(r, seg, frame);
                        foreach (var q in p) obj.AppendLine(string.Create(ci, $"v {q.X} {q.Y} {q.Z}"));
                        if (uvAttr != null)
                            for (int v = 0; v < seg.VertexCount; v++)
                            {
                                var t = ps.Attribute(r, seg, uvAttr, v);
                                obj.AppendLine(string.Create(ci, $"vt {t.X} {1 - t.Y}"));
                            }
                        if (nAttr != null)
                            for (int v = 0; v < seg.VertexCount; v++)
                            {
                                var t = ps.Attribute(r, seg, nAttr, v);
                                obj.AppendLine(string.Create(ci, $"vn {t.X} {t.Y} {t.Z}"));
                            }
                        var pt = Mdl3PackedShape.Triangles(r, seg, out _, out _, out _);
                        for (int i = 0; i < pt.Length; i += 3)
                            obj.AppendLine($"f {PCorner(pt[i])} {PCorner(pt[i + 1])} {PCorner(pt[i + 2])}");
                        string PCorner(int i) => (uvAttr, nAttr) switch
                        {
                            (not null, not null) => $"{i + vBase}/{i + vtBase}/{i + vnBase}",
                            (not null, null) => $"{i + vBase}/{i + vtBase}",
                            (null, not null) => $"{i + vBase}//{i + vnBase}",
                            _ => $"{i + vBase}",
                        };
                        vBase += seg.VertexCount;
                        if (uvAttr != null) vtBase += seg.VertexCount;
                        if (nAttr != null) vnBase += seg.VertexCount;
                        triSum += seg.TriangleCount;
                    }
                    log.AppendLine($"{s,4} PACKED segments={ps.Segments.Count} verts={ps.Segments.Sum(x => x.VertexCount)} tris={triSum} (+26 {sh.TriCount}) " +
                                   $"uv={(uvAttr == null ? "-" : $"{uvAttr.Id:X2}")}  {name}");
                    exported++;
                    packed++;
                }
                catch (Exception e)
                {
                    log.AppendLine($"{s,4} PACKED FAILED {e.Message}  {name}");
                    skipped++;
                }
                continue;
            }
            if (sh.VerticesOffset == 0 || sh.TriOffset == 0 || sh.FvfIndex < 0)
            {
                log.AppendLine($"{s,4} v={sh.VertexCount,6} triCount={sh.TriCount,6} flags={sh.Flags:X4} SKIPPED packed={sh.PackedRefOffset:X}  {name}");
                skipped++;
                continue;
            }
            var fvf = m.Fvfs[sh.FvfIndex];
            var pos = fvf["position"];
            if (pos is null) { skipped++; continue; }

            // Index buffer: u16 values at TriOffset. TriLength/TriCount meaning is what we look at here.
            int idxCount = (int)sh.TriLength;
            var idx = new int[idxCount];
            int max = 0, restarts = 0;
            for (int i = 0; i < idxCount; i++)
            {
                idx[i] = r.U16((int)sh.TriOffset + i * 2);
                if (idx[i] == 0xFFFF) restarts++;
                else max = Math.Max(max, idx[i]);
            }
            var tris = Triangles(idx, sh.TriCount, out string mode);
            log.AppendLine($"{s,4} v={sh.VertexCount,6} triLen={sh.TriLength,6} triCount={sh.TriCount,6} flags={sh.Flags:X4} " +
                           $"maxIdx={max,6} restarts={restarts,4} mode={mode,-6} tris={tris.Count,6} first={string.Join(',', idx.Take(12))}  {name}" +
                           $"  fvf {sh.FvfIndex} '{fvf.Name}' size {fvf.VertexSize}: " +
                           string.Join(' ', fvf.Elements.Select(e => $"{e.Name}@{e.Start}:{e.Type}x{e.Count}" + (e.ArrayIndex > 0 ? $"[a{e.ArrayIndex}]" : ""))));

            // Non-float positions (S1 in the wheel 'Shadow' format): scale not known yet (about bbox x2), logged and skipped.
            if (pos.Type != RsxVertexType.F)
            {
                if (sh.BBoxOffset != 0)
                {
                var c = Enumerable.Range(0, 8).Select(k => Enumerable.Range(0, 3).Select(a => r.F32((int)sh.BBoxOffset + k * 12 + a * 4)).ToArray()).ToList();
                var lo = Enumerable.Range(0, 3).Select(a => Enumerable.Range(0, (int)sh.VertexCount).Min(v => Comp(r, pos, (int)(sh.VerticesOffset + fvf.ElementOffset(pos, v)), a)));
                var hi = Enumerable.Range(0, 3).Select(a => Enumerable.Range(0, (int)sh.VertexCount).Max(v => Comp(r, pos, (int)(sh.VerticesOffset + fvf.ElementOffset(pos, v)), a)));
                log.AppendLine(string.Create(ci, $"     {pos.Type} position min {string.Join(' ', lo)} max {string.Join(' ', hi)}; bbox min " +
                    $"{string.Join(' ', Enumerable.Range(0, 3).Select(a => c.Min(p => p[a])))} max {string.Join(' ', Enumerable.Range(0, 3).Select(a => c.Max(p => p[a])))}"));
                }
                skipped++;
                continue;
            }

            obj.AppendLine($"g s{s:D4}_{Root(name)}_{Short(name)}");
            if (sh.MaterialIndex >= 0) { obj.AppendLine($"usemtl m{sh.MaterialIndex:D3}"); usedMaterials.Add(sh.MaterialIndex); }
            var nrm = fvf["normal"];
            var uv = fvf["map1"] ?? fvf["map12"];
            if (nrm != null && nrm.Type != RsxVertexType.CMP) nrm = null;
            for (int v = 0; v < sh.VertexCount; v++)
            {
                int at = (int)(sh.VerticesOffset + fvf.ElementOffset(pos, v));
                obj.AppendLine(string.Create(ci, $"v {r.F32(at)} {r.F32(at + 4)} {r.F32(at + 8)}"));
            }
            // UV decoded as in docs/mdl3.md (S1/32767, UB/255, F as is, no scale); V flipped for the OBJ bottom-left origin.
            if (uv != null)
                for (int v = 0; v < sh.VertexCount; v++)
                {
                    int at = (int)(sh.VerticesOffset + fvf.ElementOffset(uv, v));
                    obj.AppendLine(string.Create(ci, $"vt {Comp(r, uv, at, 0)} {1 - Comp(r, uv, at, 1)}"));
                }
            // CMP normal: x bits 0-10, y 11-21, z 22-31, signed, /1023 /1023 /511 (verified in 2d).
            if (nrm != null)
                for (int v = 0; v < sh.VertexCount; v++)
                {
                    uint p = r.U32((int)(sh.VerticesOffset + fvf.ElementOffset(nrm, v)));
                    float x = ((int)(p << 21) >> 21) / 1023f, y = ((int)(p << 10) >> 21) / 1023f, z = ((int)p >> 22) / 511f;
                    obj.AppendLine(string.Create(ci, $"vn {x} {y} {z}"));
                }
            foreach (var (a, b, c) in tris)
                if (a < sh.VertexCount && b < sh.VertexCount && c < sh.VertexCount)
                    obj.AppendLine($"f {Corner(a)} {Corner(b)} {Corner(c)}");
            string Corner(int i) => (uv, nrm) switch
            {
                (not null, not null) => $"{i + vBase}/{i + vtBase}/{i + vnBase}",
                (not null, null) => $"{i + vBase}/{i + vtBase}",
                (null, not null) => $"{i + vBase}//{i + vnBase}",
                _ => $"{i + vBase}",
            };
            vBase += (int)sh.VertexCount;
            if (uv != null) vtBase += (int)sh.VertexCount;
            if (nrm != null) vnBase += (int)sh.VertexCount;
            exported++;
        }

        WriteMtl(m, usedMaterials, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!, mtlName));
        File.WriteAllText(args[1], obj.ToString());
        File.WriteAllText(Path.ChangeExtension(args[1], ".txt"), log.ToString());
        Console.WriteLine($"{exported} shapes exported ({packed} packed), {skipped} skipped (no data, non-float positions or failed) -> {args[1]}");
        return 0;
    }

    /// <summary>
    /// UV source of a packed shape: the input attribute with the type and count of the shape FVF's map1/map12
    /// (RSX F/SF/S1/UB = packed types 0/1/2/5), preferring id 05; else id 05 if present.
    /// </summary>
    static Mdl3PackedAttribute? PackedUv(Mdl3File m, Mdl3Shape sh, Mdl3PackedShape ps)
    {
        var inp = ps.Desc.Input.Attributes.Where(a => a.Id is not (1 or 2 or 3)).ToList();
        if (sh.FvfIndex >= 0 && sh.FvfIndex < m.Fvfs.Count && (m.Fvfs[sh.FvfIndex]["map1"] ?? m.Fvfs[sh.FvfIndex]["map12"]) is { } e)
        {
            int type = e.Type switch { RsxVertexType.F => 0, RsxVertexType.SF => 1, RsxVertexType.S1 => 2, RsxVertexType.UB => 5, _ => -1 };
            var hit = inp.Where(a => a.Type == type && a.Count == e.Count).OrderBy(a => a.Id == 5 ? 0 : 1).ThenBy(a => a.Id).FirstOrDefault();
            if (hit != null) return hit;
        }
        return inp.FirstOrDefault(a => a.Id == 5);
    }

    /// <summary>Top 20 files by share of plain vertices (the ones an OBJ export shows best).</summary>
    static int Rank(string tsv, string groupRegex)
    {
        var re = new System.Text.RegularExpressions.Regex(groupRegex);
        var rows = new List<(string path, long plain, int plainShapes, int packedShapes)>();
        foreach (var line in File.ReadLines(tsv).Skip(1))
        {
            var c = line.Split('\t');
            if (!re.IsMatch(c[1])) continue;
            Mdl3File m;
            try { m = Mdl3File.Load(c[0]); } catch { continue; }   // old v1/v2 files
            long plain = 0; int ps = 0, pk = 0;
            foreach (var sh in m.Shapes)
            {
                if (sh.VerticesOffset != 0) { plain += sh.VertexCount; ps++; }
                else pk++;
            }
            rows.Add((c[0], plain, ps, pk));
        }
        foreach (var x in rows.OrderByDescending(x => x.plainShapes - x.packedShapes * 4).Take(20))
            Console.WriteLine($"{x.plain,8} plain verts  {x.plainShapes,4} plain / {x.packedShapes,4} packed  {x.path}");
        return 0;
    }

    /// <summary>List when the buffer holds exactly 3 indices per triangle, otherwise strip with 0xFFFF restart.</summary>
    static List<(int, int, int)> Triangles(int[] idx, int triCount, out string mode)
    {
        var tris = new List<(int, int, int)>();
        if (idx.Length == triCount * 3 && !idx.Contains(0xFFFF))
        {
            mode = "list";
            for (int i = 0; i + 2 < idx.Length; i += 3) tris.Add((idx[i], idx[i + 1], idx[i + 2]));
            return tris;
        }
        mode = "strip";
        int run = 0;
        for (int i = 0; i < idx.Length; i++)
        {
            if (idx[i] == 0xFFFF) { run = 0; continue; }
            run++;
            if (run < 3) continue;
            int a = idx[i - 2], b = idx[i - 1], c = idx[i];
            if (a == b || b == c || a == c) continue;   // degenerate joins
            tris.Add((run & 1) == 1 ? (a, b, c) : (b, a, c));
        }
        return tris;
    }

    static float Comp(BigEndianReader r, Mdl3FvfElement e, int at, int c) => e.Type switch
    {
        RsxVertexType.F => r.F32(at + c * 4),
        RsxVertexType.SF => (float)r.F16(at + c * 2),
        RsxVertexType.S1 => r.S16(at + c * 2) / 32767f,
        RsxVertexType.S32K => r.S16(at + c * 2),
        RsxVertexType.UB => r.U8(at + c) / 255f,
        RsxVertexType.UB256 => r.U8(at + c),
        _ => 0,
    };

    /// <summary>One .mtl entry per used material; the diffuse (and alpha) maps found in the file are decoded to PNG next to it.</summary>
    static void WriteMtl(Mdl3File m, IEnumerable<int> used, string path)
    {
        string dir = Path.GetDirectoryName(path)!, stem = Path.GetFileNameWithoutExtension(path);
        var images = Txs3Image.ReadAll(m.Data, (int)m.Data.U32(0x48));
        var written = new Dictionary<int, string?>();
        string? Texture(Mdl3SamplerKey? key)
        {
            if (key == null || key.External || key.Index >= m.TextureInfos.Count) return null;
            var t = m.TextureInfos[key.Index];
            if (written.TryGetValue(t.ImageIndex, out var f)) return f;
            var img = images[t.ImageIndex];
            byte[]? rgba = img.Offset == 0 ? null : img.DecodeLevel0(m.Data.Data.AsSpan((int)img.Offset, (int)img.Size), t.Pitch);
            f = null;
            if (rgba != null)
            {
                f = $"{stem}_tex{t.ImageIndex:D3}.png";
                Png.Write(Path.Combine(dir, f), rgba, img.Width, img.Height);   // level 0, first face/slice
            }
            return written[t.ImageIndex] = f;
        }

        var sb = new StringBuilder();
        foreach (int i in used)
        {
            var mat = m.Materials[i];
            sb.AppendLine($"newmtl m{i:D3}");
            sb.AppendLine($"# {mat.Name} {(mat.DataIndex >= 0 && mat.DataIndex < m.MaterialData.Count ? m.MaterialData[mat.DataIndex].Name : "")}");
            sb.AppendLine("Kd 0.8 0.8 0.8");
            if (Texture(mat["diffuseMapSampler"]) is { } kd) sb.AppendLine($"map_Kd {kd}");
            if (Texture(mat["alphaMapSampler"]) is { } d) sb.AppendLine($"map_d {d}");
            sb.AppendLine();
        }
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>First node of the shape path (GTBE_BODY, GTBE_SHADOW_BOX, FIX1...).</summary>
    static string Root(string path) => path.Split('|', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

    static string Short(string path)
    {
        var last = path.Split('|')[^1];
        return new string(last.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
    }
}
