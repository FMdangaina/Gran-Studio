using System.Text.RegularExpressions;

namespace GTFormats.Mdl3;

/// <summary>
/// Readable names from the Maya node paths of the shapes (shapeKeys): drops the technical nodes
/// (FIX, GT_LOD, LOD_L/H, transform1, *_BMFPP_NOEXPAND levels, SHARE_GROUP, GTM_FULL_SUS_*) and the default Maya
/// names (pPlane154, polySurface35, subdTessShape48...), strips the GT_/GTM_/GTB_/GTBE_ prefixes and the Shape suffixes.
/// Only renames: every kept word comes from the file.
/// </summary>
public static partial class ShapeNaming
{
    [GeneratedRegex(@"^(FIX|GT_BATCH_FIX|GT_LOD|LOD_[LHM]\d*|transform\d*|SHARE_GROUP|GT_INTERIOR_SHARE|GTM_FULL_SUS_\w+|[LH]\d*)$")]
    private static partial Regex Technical();

    [GeneratedRegex(@"^_*(p?(Plane|Cube|Cylinder|Sphere|Torus|Cone|Pipe|Disc|Prism)|poly(Surface|ToSubd)|subdTess|group|mesh|polySurfaceShape|nurbsToPoly|Mesh)\d*(_?\d+)*(Shape\d*)*_*$", RegexOptions.IgnoreCase)]
    private static partial Regex MayaDefault();

    [GeneratedRegex(@"^(SUSPENSION_\w+|DAYTIME|NIGHT|\w+_(ON|OFF)|CUSTOM_\w+|OPTION_\d+)$")]
    private static partial Regex Key();

    [GeneratedRegex(@"(Shape\d*)+_*$")]
    private static partial Regex ShapeSuffix();

    /// <summary>Clean node: suffixes and prefixes removed; empty when the node says nothing.</summary>
    public static string Clean(string node)
    {
        string n = node.Replace("_BMFPP_NOEXPAND", "");
        if (Technical().IsMatch(n) || MayaDefault().IsMatch(n)) return "";
        n = ShapeSuffix().Replace(n, "");
        if (n.Contains(':')) n = n[(n.LastIndexOf(':') + 1)..];   // Maya namespaces (Lint:pCube77)
        foreach (var p in new[] { "GTBE_", "GTM_", "GTB_", "GT_" })
            if (n.StartsWith(p)) { n = n[p.Length..]; break; }
        n = n.Trim('_');
        if (MayaDefault().IsMatch(n)) return "";
        return new string([.. n.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_')]);
    }

    /// <summary>Meaningful nodes of a path, first = the model (BODY, SHADOW_BOX...); repeats of the parent dropped.</summary>
    public static List<string> Nodes(string path)
    {
        var list = new List<string>();
        foreach (var raw in path.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            string c = Clean(raw);
            if (c.Length == 0) continue;
            if (list.Count > 1)   // child repeating its parent (p_mirror_cp2 | p_mirror_cp): keep the longer one
            {
                string a = list[^1].TrimEnd("0123456789_".ToCharArray()), b = c.TrimEnd("0123456789_".ToCharArray());
                if (a.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a, StringComparison.OrdinalIgnoreCase))
                {
                    if (c.Length > list[^1].Length) list[^1] = c;
                    continue;
                }
            }
            list.Add(c);
        }
        return list;
    }

    /// <summary>
    /// Short object name: model + the last two meaningful nodes + shape index (Blender keeps 63 characters).
    /// When only the model is left, the Maya node name is kept as is (pPlane154).
    /// </summary>
    public static string Short(Mdl3File m, int shape)
    {
        string path = m.ShapeNames.TryGetValue(shape, out var n) ? n[0] : $"shape{shape}";
        var nodes = Nodes(path);
        if (nodes.Count <= 1)
        {
            var raw = path.Split('|', StringSplitOptions.RemoveEmptyEntries);
            string last = raw.Length >= 2 ? raw[^2] : raw.LastOrDefault() ?? "";
            nodes.Add(new string([.. last.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_')]));
        }
        // model, the key nodes (wheel corner, day/night, on/off, custom options), then the last two nodes
        var parts = nodes.Count <= 3 ? nodes
            : [nodes[0], .. nodes.Skip(1).SkipLast(2).Where(x => Key().IsMatch(x)), .. nodes.TakeLast(2)];
        while (parts.Count > 2 && string.Join('.', parts).Length > 56) parts.RemoveAt(parts.Count - 2);   // the leaf stays
        string name = string.Join('.', parts);
        if (name.Length > 56) name = name[..56];
        return $"{name}.s{shape}";
    }
}
