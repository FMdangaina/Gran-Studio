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
    public bool NormalsComputed;   // not stored in the file (GT6 SmoothMesh data): area-weighted face normals
    public string? NormalSource;   // GT6 PMSH: where the normals came from (raw element, stream 0x80, packed layout, computed)

    /// <summary>Area-weighted vertex normals from the triangles (unit length; vertices without triangles get +Y).</summary>
    public static Vector3[] FaceNormals(Vector3[] p, int[] idx)
    {
        var n = new Vector3[p.Length];
        for (int i = 0; i + 2 < idx.Length; i += 3)
        {
            int a = idx[i], b = idx[i + 1], c = idx[i + 2];
            if ((uint)a >= p.Length || (uint)b >= p.Length || (uint)c >= p.Length) continue;
            var f = Vector3.Cross(p[b] - p[a], p[c] - p[a]);
            n[a] += f; n[b] += f; n[c] += f;
        }
        for (int v = 0; v < n.Length; v++) n[v] = n[v].LengthSquared() > 1e-20f ? Vector3.Normalize(n[v]) : Vector3.UnitY;
        return n;
    }

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
        if (sh.FvfIndex < 0 && m.Pmsh is { } pmsh && Mdl3Pmsh.MeshIndex(r, sh) is int mi && mi >= 0 && mi < pmsh.Meshes.Count)
            return DecodePmsh(m, shape, pmsh, pmsh.Meshes[mi], out reason);
        if (sh.FvfIndex < 0) { reason = "no vertex format"; return null; }
        long vertsAt = sh.VerticesOffset, trisAt = sh.TriOffset;
        if (vertsAt == 0 && sh.VertexCount > 0 && m.BodyS?.Shapes.TryGetValue(shape, out var sd) == true)
        {
            // GT6 body: plain shape data in its body_s bundle, record +04 vertices, +08 triangles, +0C box
            r = sd!.Bundle.R;
            vertsAt = r.U32(sd.Record + 4);
            trisAt = r.U32(sd.Record + 8);
        }
        if (vertsAt == 0 || trisAt == 0) { reason = "no vertex data in the file"; return null; }
        var fvf = m.Fvfs[sh.FvfIndex];
        var pos = fvf["position"];
        if (pos is null) { reason = "no position"; return null; }
        if (pos.Type != RsxVertexType.F) { reason = $"{pos.Type} positions"; return null; }

        int n = (int)sh.VertexCount;
        var mesh = new Mdl3Mesh { Positions = new Vector3[n] };
        for (int v = 0; v < n; v++)
        {
            int at = (int)(vertsAt + fvf.ElementOffset(pos, v));
            mesh.Positions[v] = new Vector3(r.F32(at), r.F32(at + 4), r.F32(at + 8));
        }
        if (fvf["normal"] is { Type: RsxVertexType.CMP } nrm)
        {
            mesh.Normals = new Vector3[n];
            for (int v = 0; v < n; v++) mesh.Normals[v] = Cmp(r.U32((int)(vertsAt + fvf.ElementOffset(nrm, v))));
        }
        if ((fvf["map1"] ?? fvf["map12"]) is { } uv)
        {
            mesh.Uvs = new Vector2[n];
            for (int v = 0; v < n; v++)
            {
                int at = (int)(vertsAt + fvf.ElementOffset(uv, v));
                mesh.Uvs[v] = new Vector2(Comp(r, uv, at, 0), Comp(r, uv, at, 1));
            }
        }
        var idx = new List<int>((int)sh.TriLength);
        for (int i = 0; i + 2 < sh.TriLength; i += 3)
        {
            int a = r.U16((int)trisAt + i * 2), b = r.U16((int)trisAt + i * 2 + 2), c = r.U16((int)trisAt + i * 2 + 4);
            if (a < n && b < n && c < n) { idx.Add(a); idx.Add(b); idx.Add(c); }
        }
        mesh.Indices = [.. idx];
        return mesh;
    }

    /// <summary>
    /// GT6 PMSH mesh: data blocks in the MDL3 (interiors, wheels) or, for car bodies, in the shape's body_s bundle
    /// (same records, offsets relative to the bundle). Several data blocks are merged (split or level: open).
    /// </summary>
    static Mdl3Mesh? DecodePmsh(Mdl3File m, int shape, Mdl3Pmsh pmsh, Mdl3PmshMesh mesh, out string reason)
    {
        reason = "";
        if (mesh.Declaration < 0) { reason = "PMSH mesh without declaration"; return null; }
        BigEndianReader r = m.Data;
        var blocks = mesh.Data;
        if (blocks.Count == 0 || blocks[0].PackedVertices == 0)
        {
            if (m.BodyS?.Shapes.TryGetValue(shape, out var sd) != true) { reason = "PMSH data not in the file and not in body_s"; return null; }
            r = sd!.Bundle.R;
            blocks = m.BodyS.Data(shape);
        }
        var parts = blocks.Select(d => pmsh.Decode(r, mesh, d)).ToList();
        if (parts.Count == 1) return parts[0];
        var pos = new List<Vector3>(); var nrm = new List<Vector3>(); var uvs = new List<Vector2>(); var idx = new List<int>();
        bool hasN = parts.All(p => p.Normals != null), hasUv = parts.All(p => p.Uvs != null);
        foreach (var p in parts)
        {
            int b = pos.Count;
            pos.AddRange(p.Positions);
            if (hasN) nrm.AddRange(p.Normals!);
            if (hasUv) uvs.AddRange(p.Uvs!);
            idx.AddRange(p.Indices.Select(i => i < 0 ? i : b + i));
        }
        return new Mdl3Mesh { Packed = true, Positions = [.. pos], Normals = hasN ? [.. nrm] : null, Uvs = hasUv ? [.. uvs] : null, Indices = [.. idx] };
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
