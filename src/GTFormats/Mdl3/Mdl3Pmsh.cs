namespace GTFormats.Mdl3;

/// <summary>Bit layout of one packed element (0x30, PDTools PMSHElementBitLayout): total bits, bits and bit ends per component, scale, offset.</summary>
public sealed class Mdl3PmshBitLayout
{
    public int TotalBits;
    public int[] Bits = new int[4], BitEnds = new int[4];
    public short Unk0C, Unk0E;
    public float[] Scale = new float[4], Offset = new float[4];

    public static Mdl3PmshBitLayout Read(BigEndianReader r, int at)
    {
        var l = new Mdl3PmshBitLayout { TotalBits = (int)r.U32(at), Unk0C = r.S16(at + 0x0C), Unk0E = r.S16(at + 0x0E) };
        for (int i = 0; i < 4; i++)
        {
            l.Bits[i] = r.U8(at + 4 + i);
            l.BitEnds[i] = r.U8(at + 8 + i);
            l.Scale[i] = r.F32(at + 0x10 + i * 4);
            l.Offset[i] = r.F32(at + 0x20 + i * 4);
        }
        return l;
    }
}

/// <summary>Element of a PMSH vertex declaration: u8 name id, u8 output offset, u8 component count, u8 RSX type; name from the name list.</summary>
public sealed record Mdl3PmshElement(string Name, int NameId, int OutputOffset, int Count, RsxVertexType Type, bool Packed);

/// <summary>PMSH vertex declaration (0x10): u8 packed count, u8 raw count, u8 packed (output) stride, u8 raw stride, u32, elements, names.</summary>
public sealed class Mdl3PmshDeclaration
{
    public int PackedStride, RawStride;
    public uint Unk04;
    public List<Mdl3PmshElement> Packed = [], Raw = [];

    public static Mdl3PmshDeclaration Read(BigEndianReader r, int at)
    {
        var d = new Mdl3PmshDeclaration { PackedStride = r.U8(at + 2), RawStride = r.U8(at + 3), Unk04 = r.U32(at + 4) };
        int np = r.U8(at), nr = r.U8(at + 1), el = (int)r.U32(at + 8), names = (int)r.U32(at + 0x0C);
        for (int i = 0; i < np + nr; i++)
        {
            int e = el + i * 4;
            var x = new Mdl3PmshElement(r.CString((int)r.U32(names + i * 4)), r.U8(e), r.U8(e + 1), r.U8(e + 2), (RsxVertexType)r.U8(e + 3), i < np);
            (i < np ? d.Packed : d.Raw).Add(x);
        }
        return d;
    }
}

/// <summary>Smooth mesh data (0x30, PDTools PMSHSmoothMeshData); offsets absolute in the MDL3, 0 in GT6 car bodies (data in body_s).</summary>
public sealed class Mdl3PmshData
{
    public int Offset;
    public uint PackedVertices, Entries08, Unk04, RawVertices, Streams10, Indices, Commands, Unk1C;
    public int Flags, VertexCount, Unk24, OutputVertexCount, Quadwords28, IndexQuadwords, CommandQuadwords, IndexBits, Unk2F;

    public static Mdl3PmshData Read(BigEndianReader r, int at) => new()
    {
        Offset = at,
        PackedVertices = r.U32(at), Unk04 = r.U32(at + 4), Entries08 = r.U32(at + 8), RawVertices = r.U32(at + 0x0C),
        Streams10 = r.U32(at + 0x10), Indices = r.U32(at + 0x14), Commands = r.U32(at + 0x18), Unk1C = r.U32(at + 0x1C),
        Flags = r.U16(at + 0x20), VertexCount = r.U16(at + 0x22), Unk24 = r.U16(at + 0x24), OutputVertexCount = r.U16(at + 0x26),
        Quadwords28 = r.U16(at + 0x28), IndexQuadwords = r.U16(at + 0x2A), CommandQuadwords = r.U16(at + 0x2C),
        IndexBits = r.U8(at + 0x2E), Unk2F = r.U8(at + 0x2F),
    };
}

/// <summary>PMSH mesh (0x30): i16 declaration, i16 bit layout array, i16, u8 data count, u8 k, +08 records, +10 data, +14 bboxes.</summary>
public sealed class Mdl3PmshMesh
{
    public int Offset, Declaration, LayoutArray, Unk04, DataCount, K;
    public float Unk0C, Unk18, Unk1C;
    public short[] Unk20 = new short[6];
    public List<Mdl3PmshData> Data = [];
    public List<System.Numerics.Vector3[]> BBoxes = [];   // one 8-corner box per data block

    public static Mdl3PmshMesh Read(BigEndianReader r, int at)
    {
        var m = new Mdl3PmshMesh
        {
            Offset = at, Declaration = r.S16(at), LayoutArray = r.S16(at + 2), Unk04 = r.S16(at + 4), DataCount = r.U8(at + 6), K = r.U8(at + 7),
            Unk0C = r.F32(at + 0x0C), Unk18 = r.F32(at + 0x18), Unk1C = r.F32(at + 0x1C),
        };
        for (int i = 0; i < 6; i++) m.Unk20[i] = r.S16(at + 0x20 + i * 2);
        int data = (int)r.U32(at + 0x10), bboxes = (int)r.U32(at + 0x14);
        if (data != 0)
            for (int i = 0; i < m.DataCount; i++) m.Data.Add(Mdl3PmshData.Read(r, data + i * 0x30));
        // box: two groups of 4 corners, each stored as x[4], y[4], z[4] (oriented box, not axis-aligned)
        if (bboxes != 0)
            for (int i = 0; i < m.DataCount; i++)
            {
                int b = bboxes + i * 0x60;
                m.BBoxes.Add([.. Enumerable.Range(0, 8).Select(k => new System.Numerics.Vector3(
                    r.F32(b + (k / 4) * 0x30 + (k % 4) * 4), r.F32(b + (k / 4) * 0x30 + 0x10 + (k % 4) * 4), r.F32(b + (k / 4) * 0x30 + 0x20 + (k % 4) * 4)))]);
            }
        return m;
    }
}

/// <summary>GT6 (v14) packed mesh header PMSH at header 0xD0. Layout from PDTools (MIT) and `gtdev mdl3-coverage`.</summary>
public sealed class Mdl3Pmsh
{
    public List<Mdl3PmshMesh> Meshes = [];
    public List<List<Mdl3PmshBitLayout>> Layouts = [];
    public List<Mdl3PmshDeclaration> Declarations = [];
    public List<(int Field0, int Count)> Entries1C = [];   // +12 count, +1C: u8, u8 count, u16 data size, u32 data offset

    public static Mdl3Pmsh? Read(BigEndianReader r)
    {
        int h = (int)r.U32(0xD0);
        if (h == 0) return null;
        var p = new Mdl3Pmsh();
        for (int i = 0; i < r.U16(h + 0x12); i++)
        {
            int a = (int)r.U32(h + 0x1C) + i * 8;
            p.Entries1C.Add((r.U8(a), r.U8(a + 1)));
        }
        int meshes = r.U16(h + 0x10), layouts = r.U16(h + 0x14), decls = r.U16(h + 0x16);
        int meshAt = (int)r.U32(h + 0x18), layoutAt = (int)r.U32(h + 0x20), declAt = (int)r.U32(h + 0x24);
        for (int i = 0; i < layouts; i++)
        {
            int a = layoutAt + i * 8, n = (int)r.U32(a), at = (int)r.U32(a + 4);
            p.Layouts.Add([.. Enumerable.Range(0, n).Select(k => Mdl3PmshBitLayout.Read(r, at + k * 0x30))]);
        }
        for (int i = 0; i < decls; i++) p.Declarations.Add(Mdl3PmshDeclaration.Read(r, declAt + i * 0x10));
        for (int i = 0; i < meshes; i++) p.Meshes.Add(Mdl3PmshMesh.Read(r, meshAt + i * 0x30));
        return p;
    }

    /// <summary>
    /// Bit layout used by each packed element (null = no layout, see HasNoLayout); the normal is then in the 4-byte
    /// stream of flag 0x80, or not stored. Checked by `gtdev pmsh-check`.
    /// </summary>
    public static Mdl3PmshBitLayout?[] PackedLayouts(Mdl3PmshDeclaration decl, List<Mdl3PmshBitLayout> layouts, int flags)
    {
        var map = new Mdl3PmshBitLayout?[decl.Packed.Count];
        int next = 0;
        for (int i = 0; i < decl.Packed.Count; i++)
        {
            var e = decl.Packed[i];
            if (HasNoLayout(e, flags, decl)) continue;
            map[i] = next < layouts.Count ? layouts[next] : null;
            next++;
        }
        return map;
    }

    /// <summary>Layouts the rule above expects for a declaration (to compare with the layout array).</summary>
    public static int ExpectedLayoutCount(Mdl3PmshDeclaration decl, int flags) =>
        decl.Packed.Count(e => !HasNoLayout(e, flags, decl));

    /// <summary>
    /// The CMP normal never has a bit layout; colorSet1 and colorSet2 together take one (counted on colorSet2);
    /// colorSet1 alone has one only with flag 0x40; map34 shares the layout of a 2-component map12 (4 components,
    /// counted on map12).
    /// </summary>
    static bool HasNoLayout(Mdl3PmshElement e, int flags, Mdl3PmshDeclaration decl) =>
        e.Type == RsxVertexType.CMP ||
        (e.Name == "colorSet1" && ((flags & 0x40) == 0 || decl.Packed.Any(x => x.Name == "colorSet2"))) ||
        (e.Name == "map34" && decl.Packed.Any(x => x.Name == "map12" && x.Count == 2));

    /// <summary>
    /// Decode one data block held in the MDL3 (interiors, wheels, tires): packed elements are bit arrays, one per
    /// layout, V values each (MSB first), value = (raw / (2^(bits-1) - 1) + 2) * scale + offset (PDTools);
    /// raw elements are V records of the raw stride; normals come from a raw CMP element, the 4-byte stream (+10 after
    /// the +28 quadwords and the 1-byte stream) or a layout. Triangles from the command stream (Mdl3PmshCommands).
    /// </summary>
    public Mdl3Mesh Decode(BigEndianReader r, Mdl3PmshMesh mesh, Mdl3PmshData d)
    {
        var decl = Declarations[mesh.Declaration];
        var layouts = mesh.LayoutArray >= 0 ? Layouts[mesh.LayoutArray] : [];
        int V = d.VertexCount;
        var result = new Mdl3Mesh { Packed = true, Positions = new System.Numerics.Vector3[V] };
        var packed = new Dictionary<string, System.Numerics.Vector4[]>();
        var map = PackedLayouts(decl, layouts, d.Flags);
        long at = d.PackedVertices;
        uint[]? positionW = null;   // 32-bit 4th component of the position layout = the CMP normal word
        for (int i = 0; i < decl.Packed.Count; i++)
        {
            var l = map[i];
            if (l == null) continue;
            var values = new System.Numerics.Vector4[V];
            bool keepW = decl.Packed[i].Name == "position" && l.Bits[3] == 32;
            if (keepW) positionW = new uint[V];
            for (int v = 0; v < V; v++)
            {
                long bit = (long)v * l.TotalBits;
                var f = new float[4];
                for (int c = 0; c < 4; c++)
                {
                    if (l.Bits[c] == 0) continue;
                    uint raw = 0;
                    for (int b = 0; b < l.Bits[c]; b++, bit++)
                        raw = (raw << 1) | (uint)((r.Data[at + (bit >> 3)] >> (7 - (int)(bit & 7))) & 1);
                    if (c == 3 && keepW) positionW![v] = raw;
                    f[c] = (raw / (float)((1L << (l.Bits[c] - 1)) - 1) + 2f) * l.Scale[c] + l.Offset[c];
                }
                values[v] = new System.Numerics.Vector4(f[0], f[1], f[2], f[3]);
            }
            packed[decl.Packed[i].Name] = values;
            at += ((long)V * l.TotalBits + 7) / 8;
        }
        if (packed.TryGetValue("position", out var pos))
            for (int v = 0; v < V; v++) result.Positions[v] = new System.Numerics.Vector3(pos[v].X, pos[v].Y, pos[v].Z);

        System.Numerics.Vector3 RawVec(Mdl3PmshElement e, int v)
        {
            int a = (int)d.RawVertices + v * decl.RawStride + e.OutputOffset;
            if (e.Type == RsxVertexType.CMP) return Mdl3Mesh.Cmp(r.U32(a));
            var el = new Mdl3FvfElement(e.Name, 0, e.Count, e.Type, 0);
            return new System.Numerics.Vector3(Mdl3Mesh.Comp(r, el, a, 0), e.Count > 1 ? Mdl3Mesh.Comp(r, el, a, 1) : 0, e.Count > 2 ? Mdl3Mesh.Comp(r, el, a, 2) : 0);
        }

        // normals
        if (decl.Raw.FirstOrDefault(e => e.Name == "normal") is { } rn && d.RawVertices != 0)
        { result.Normals = [.. Enumerable.Range(0, V).Select(v => RawVec(rn, v))]; result.NormalSource = $"raw {rn.Type}"; }
        else if (positionW != null && decl.Packed.Any(e => e.Name == "normal" && e.Type == RsxVertexType.CMP))
        { result.Normals = [.. positionW.Select(Mdl3Mesh.Cmp)]; result.NormalSource = "position w"; }
        else if ((d.Flags & 0x80) != 0 && d.Streams10 != 0)
        {
            int s = (int)d.Streams10 + d.Quadwords28 * 16 + ((d.Flags & 0x40) != 0 ? (V + 15) & ~15 : 0);
            result.Normals = [.. Enumerable.Range(0, V).Select(v => Mdl3Mesh.Cmp(r.U32(s + v * 4)))];
            result.NormalSource = "stream 0x80";
        }
        else if (packed.TryGetValue("normal", out var pn))
        { result.Normals = [.. pn.Select(x => new System.Numerics.Vector3(x.X, x.Y, x.Z))]; result.NormalSource = "packed layout"; }

        // UVs: map12 / map1, raw or packed
        foreach (var name in new[] { "map12", "map1" })
        {
            if (decl.Raw.FirstOrDefault(e => e.Name == name) is { } ru && d.RawVertices != 0)
            {
                result.Uvs = [.. Enumerable.Range(0, V).Select(v => { var q = RawVec(ru, v); return new System.Numerics.Vector2(q.X, q.Y); })];
                break;
            }
            if (packed.TryGetValue(name, out var pu)) { result.Uvs = [.. pu.Select(x => new System.Numerics.Vector2(x.X, x.Y))]; break; }
        }
        if (decl.Raw.FirstOrDefault(e => e.Name == "colorSet1") is { Type: RsxVertexType.UB, Count: 4 } rc && d.RawVertices != 0)
            result.Colors = [.. Enumerable.Range(0, V).Select(v =>
            {
                int a = (int)d.RawVertices + v * decl.RawStride + rc.OutputOffset;
                return new System.Numerics.Vector4(r.U8(a), r.U8(a + 1), r.U8(a + 2), r.U8(a + 3)) / 255f;
            })];
        result.Indices = Mdl3PmshCommands.Triangles(r.Data, d, out _, out _, Entries1C);
        if (result.Normals == null) { result.Normals = Mdl3Mesh.FaceNormals(result.Positions, result.Indices); result.NormalsComputed = true; result.NormalSource = "computed"; }
        return result;
    }

    /// <summary>PMSH mesh index of a shape (shape packed reference +34), or -1.</summary>
    public static int MeshIndex(BigEndianReader r, Mdl3Shape s) =>
        s.PackedRefOffset == 0 ? -1 : r.S32((int)s.PackedRefOffset + 0x34);
}
