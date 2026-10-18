using System.Numerics;

namespace GTFormats.Mdl3;

/// <summary>
/// Decoded triangles of one shape (plain or GT5 packed): positions, optional normals and UVs, triangle list.
/// Plain: F x3 positions, CMP normals, map1/map12 UVs (no scale), u16 triangle list (+14 = index count).
/// Packed: all segments merged, one LOD level (see <see cref="Decode"/>). UVs are as stored (V not flipped).
/// </summary>
public sealed class Mdl3Mesh
{
    public Vector3[] Positions = [];
    public Vector3[]? Normals;
    public Vector2[]? Uvs;
    public int[] Indices = [];
    public bool Packed;

    /// <summary>LOD levels of a shape: the most levels any packed segment has; 1 for plain shapes.</summary>
    public static int LodCount(Mdl3File m, int shape)
    {
        var sh = m.Shapes[shape];
        if (!Mdl3PackedShape.IsPacked(m.Data, sh)) return 1;
        return Mdl3PackedShape.Read(m.Data, sh).Segments.Max(s => s.LodIndexInfos.Length);
    }

    /// <summary>
    /// Decode a shape. Returns null with <paramref name="reason"/> when it has no decodable data here
    /// (GT6 data in body_s or PMSH, non-float plain positions). Packed segments with LODs use level
    /// min(lod, last); segments without LODs are the same at every level.
    /// </summary>
    public static Mdl3Mesh? Decode(Mdl3File m, int shape, int lod, out string reason)
    {
        var r = m.Data;
        var sh = m.Shapes[shape];
        reason = "";
        if (Mdl3PackedShape.IsPacked(r, sh)) return DecodePacked(m, sh, lod);
        if (sh.VerticesOffset == 0 || sh.TriOffset == 0 || sh.FvfIndex < 0) { reason = "no vertex data in the file"; return null; }
        var fvf = m.Fvfs[sh.FvfIndex];
        var pos = fvf["position"];
        if (pos is null) { reason = "no position"; return null; }
        if (pos.Type != RsxVertexType.F) { reason = $"{pos.Type} positions"; return null; }

        int n = (int)sh.VertexCount;
        var mesh = new Mdl3Mesh { Positions = new Vector3[n] };
        for (int v = 0; v < n; v++)
        {
            int at = (int)(sh.VerticesOffset + fvf.ElementOffset(pos, v));
            mesh.Positions[v] = new Vector3(r.F32(at), r.F32(at + 4), r.F32(at + 8));
        }
        if (fvf["normal"] is { Type: RsxVertexType.CMP } nrm)
        {
            mesh.Normals = new Vector3[n];
            for (int v = 0; v < n; v++) mesh.Normals[v] = Cmp(r.U32((int)(sh.VerticesOffset + fvf.ElementOffset(nrm, v))));
        }
        if ((fvf["map1"] ?? fvf["map12"]) is { } uv)
        {
            mesh.Uvs = new Vector2[n];
            for (int v = 0; v < n; v++)
            {
                int at = (int)(sh.VerticesOffset + fvf.ElementOffset(uv, v));
                mesh.Uvs[v] = new Vector2(Comp(r, uv, at, 0), Comp(r, uv, at, 1));
            }
        }
        var idx = new List<int>((int)sh.TriLength);
        for (int i = 0; i + 2 < sh.TriLength; i += 3)
        {
            int a = r.U16((int)sh.TriOffset + i * 2), b = r.U16((int)sh.TriOffset + i * 2 + 2), c = r.U16((int)sh.TriOffset + i * 2 + 4);
            if (a < n && b < n && c < n) { idx.Add(a); idx.Add(b); idx.Add(c); }
        }
        mesh.Indices = [.. idx];
        return mesh;
    }

    static Mdl3Mesh DecodePacked(Mdl3File m, Mdl3Shape sh, int lod)
    {
        var r = m.Data;
        var frame = Mdl3PackedShape.CarFrame(r);
        var ps = Mdl3PackedShape.Read(r, sh);
        var uvAttr = PackedUv(m, sh, ps);
        var nAttr = ps.Desc.Input[2] is { Type: 4 } na ? na : null;
        var pos = new List<Vector3>();
        var nrm = nAttr != null ? new List<Vector3>() : null;
        var uvs = uvAttr != null ? new List<Vector2>() : null;
        var idx = new List<int>();
        foreach (var full in ps.Segments)
        {
            int levels = full.LodIndexInfos.Length;
            var seg = levels <= 1 ? full : full.Lod(r, Math.Min(lod, levels - 1));
            int b = pos.Count;
            pos.AddRange(ps.Positions(r, seg, frame));
            for (int v = 0; v < seg.VertexCount; v++)
            {
                if (nrm != null) nrm.Add(ps.Attribute(r, seg, nAttr!, v));
                if (uvs != null) { var t = ps.Attribute(r, seg, uvAttr!, v); uvs.Add(new Vector2(t.X, t.Y)); }
            }
            foreach (int i in Mdl3PackedShape.Triangles(r, seg, out _, out _, out _)) idx.Add(b + i);
        }
        return new Mdl3Mesh { Packed = true, Positions = [.. pos], Normals = nrm?.ToArray(), Uvs = uvs?.ToArray(), Indices = [.. idx] };
    }

    /// <summary>
    /// UV source of a packed shape: the input attribute with the type and count of the shape FVF's map1/map12
    /// (RSX F/SF/S1/UB = packed types 0/1/2/5), preferring id 05; else id 05 if present.
    /// </summary>
    public static Mdl3PackedAttribute? PackedUv(Mdl3File m, Mdl3Shape sh, Mdl3PackedShape ps)
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

    /// <summary>CMP normal: x bits 0-10, y 11-21, z 22-31, signed, /1023 /1023 /511 (verified in 2d).</summary>
    public static Vector3 Cmp(uint p) =>
        new(((int)(p << 21) >> 21) / 1023f, ((int)(p << 10) >> 21) / 1023f, ((int)p >> 22) / 511f);

    public static float Comp(BigEndianReader r, Mdl3FvfElement e, int at, int c) => e.Type switch
    {
        RsxVertexType.F => r.F32(at + c * 4),
        RsxVertexType.SF => (float)r.F16(at + c * 2),
        RsxVertexType.S1 => r.S16(at + c * 2) / 32767f,
        RsxVertexType.S32K => r.S16(at + c * 2),
        RsxVertexType.UB => r.U8(at + c) / 255f,
        RsxVertexType.UB256 => r.U8(at + c),
        _ => 0,
    };
}
