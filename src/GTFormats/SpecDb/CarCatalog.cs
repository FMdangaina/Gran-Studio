using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace GTFormats.SpecDb;

/// <summary>One car of a game folder: its model code and files, with the name from the game data when found.</summary>
public sealed class CarEntry
{
    public string Game = "";            // GT5 / GT6
    public string Code = "";            // model folder code, 8 digits (GT6: maker folder + car folder)
    public string Name = "";            // readable name
    public string NameSource = "";      // specdb, or "model paths" (guessed from the texture paths in the model)
    public string Label = "";           // database label (rx7_spiritr_a_02), also the thumbnail name
    public int Year;
    public string? HqBody, RaceBody, Interior;
    public List<string> Wheels = [];
    public string? Thumbnail;
}

/// <summary>A model that is not a car body: a car's wheel, a GT6 aftermarket wheel or custom wing part (carparts).</summary>
public sealed class PartEntry
{
    public string Kind = "";            // car wheel, aftermarket wheel, wing part
    public string Code = "";            // car code, or the carparts file name (he002)
    public string Name = "";
    public string Info = "";            // size and spokes of an aftermarket wheel
    public string File = "";            // the model (hq)
    public string? RaceFile;            // carparts\race copy (low detail)
    public string? Thumbnail;
    public CarEntry? Car;               // for a car's own wheel
}

/// <summary>
/// The car list of an extracted game folder (GT5_VOL or GT6_VOL), as in Forza Studio: every model code with its
/// files, the name, year and database label from the GT5 spec database (`specdb\GT5_JP3009`: VARIATION.ModelCode =
/// the model folder, CAR_NAME_british, GENERIC_CAR year). GT6 uses its own SQLite database (<see cref="Gt6SpecDb"/>);
/// without it, the GT5 database when it sits next to it (963 of the 1227 GT6 codes are in it), and for the others
/// the name is guessed from the Maya project paths in the model.
/// </summary>
public static class CarCatalog
{
    public static List<CarEntry> Scan(string root, Action<string>? progress = null)
    {
        root = Path.GetFullPath(root);
        if (Path.GetFileName(root).Equals("car", StringComparison.OrdinalIgnoreCase)) root = Path.GetDirectoryName(root)!;
        string car = Path.Combine(root, "car");
        if (!Directory.Exists(car)) throw new DirectoryNotFoundException($"No 'car' folder in {root}: pick the extracted GT5_VOL or GT6_VOL folder.");
        bool gt5 = Directory.Exists(Path.Combine(car, "hq"));
        var names = gt5 ? [] : ReadGt6Names(root);
        string nameSource = "GT6 specdb";
        if (names.Count == 0)
        {
            var db = FindSpecDb(root, gt5);
            names = db != null ? ReadNames(db) : [];
            nameSource = gt5 ? "specdb" : "GT5 specdb";
        }
        var list = new List<CarEntry>();
        if (gt5)
        {
            var wheels = Directory.Exists(Path.Combine(root, "wheel", "hq"))
                ? Directory.GetFiles(Path.Combine(root, "wheel", "hq")).GroupBy(f => Path.GetFileName(f).Split('.')[0]).ToDictionary(g => g.Key, g => g.Order().ToList())
                : [];
            foreach (var f in Directory.GetFiles(Path.Combine(car, "hq")).Order())
            {
                string code = Path.GetFileName(f);
                if (!Regex.IsMatch(code, @"^\d{8}$")) continue;
                var e = new CarEntry { Game = "GT5", Code = code, HqBody = f };
                e.RaceBody = Existing(Path.Combine(car, "race", code));
                e.Interior = Existing(Path.Combine(car, "interior", code));
                e.Wheels = wheels.GetValueOrDefault(code, []);
                list.Add(e);
            }
        }
        else
            foreach (var maker in Directory.GetDirectories(car).Where(d => Regex.IsMatch(Path.GetFileName(d), @"^\d{4}$")).Order())
                foreach (var dir in Directory.GetDirectories(maker).Where(d => Regex.IsMatch(Path.GetFileName(d), @"^\d{4}$")).Order())
                {
                    var e = new CarEntry { Game = "GT6", Code = Path.GetFileName(maker) + Path.GetFileName(dir) };
                    e.HqBody = Existing(Path.Combine(dir, "hq", "body"));
                    e.RaceBody = Existing(Path.Combine(dir, "race", "body"));
                    e.Interior = Existing(Path.Combine(dir, "interior"));
                    foreach (var w in new[] { Path.Combine(dir, "hq", "wheel"), Path.Combine(dir, "race", "wheel") })
                        if (File.Exists(w)) e.Wheels.Add(w);
                    if (e.HqBody != null || e.RaceBody != null) list.Add(e);
                }

        // thumbnails: GT5 car\thumbnail_M, GT6 car\thumbnail_S, named <label>_<colour>
        string thumbs = Path.Combine(car, gt5 ? "thumbnail_M" : "thumbnail_S");
        var thumbFiles = Directory.Exists(thumbs) ? Directory.GetFiles(thumbs).Select(Path.GetFileName).OfType<string>().Order().ToList() : [];
        var thumbLabels = thumbFiles.Select(t => Regex.Replace(t, @"_\d\d$", "")).Distinct().ToList();
        int n = 0;
        foreach (var e in list)
        {
            if (names.TryGetValue(e.Code, out var info))
            {
                (e.Name, e.Label, e.Year) = info;
                e.NameSource = nameSource;
            }
            else
            {
                e.Name = GuessName(e.HqBody ?? e.RaceBody!) ?? "";
                e.NameSource = e.Name.Length > 0 ? "model paths" : "";
                e.Label = GuessLabel(e.Name, thumbLabels) ?? "";
            }
            if (e.Label.Length > 0)
                e.Thumbnail = thumbFiles.FirstOrDefault(t => t.StartsWith(e.Label + "_") && Regex.IsMatch(t[(e.Label.Length + 1)..], @"^\d\d$")) is { } t ? Path.Combine(thumbs, t)
                    : thumbFiles.Contains(e.Label) ? Path.Combine(thumbs, e.Label) : null;
            if (++n % 100 == 0) progress?.Invoke($"{n}/{list.Count}");
        }
        return list;
    }

    static string? Existing(string p) => File.Exists(p) ? p : null;

    /// <summary>The GT6 spec database for a game folder: its own (GT6_VOL), else the one of a GT6 folder next to it.</summary>
    public static string? FindGt6SpecDb(string root)
    {
        root = Path.GetFullPath(root);
        if (Gt6SpecDb.Find(root) is { } own) return own;
        var parent = Path.GetDirectoryName(root);
        return parent == null ? null : Directory.GetDirectories(parent).Select(Gt6SpecDb.Find).FirstOrDefault(f => f != null);
    }

    /// <summary>
    /// The wheels and other parts of a game folder, apart from the cars: every car's own wheel files (named after the
    /// car), and in GT6 the `carparts` models: aftermarket wheels `heNNN` (NNN = WHEEL_EX ID, name and size from the
    /// database, thumbnail `wheel_thumb\NNN_00`) and the custom wing parts `we` endplate, `wm` mount, `wp` plane, `ws` stay.
    /// </summary>
    public static List<PartEntry> ScanParts(string root, List<CarEntry> cars, Gt6SpecDb? db)
    {
        root = Path.GetFullPath(root);
        if (Path.GetFileName(root).Equals("car", StringComparison.OrdinalIgnoreCase)) root = Path.GetDirectoryName(root)!;
        var list = new List<PartEntry>();
        foreach (var c in cars)
            foreach (var w in c.Wheels)
            {
                // GT5 wheel\hq\<code>.NN, GT6 car\MMMM\CCCC\<hq|race>\wheel
                string variant = c.Game == "GT5" ? Path.GetFileName(w).Contains('.') ? "variant " + Path.GetFileName(w).Split('.')[1] : "base" : Path.GetFileName(Path.GetDirectoryName(w))!;
                list.Add(new PartEntry { Kind = "car wheel", Code = c.Code, Name = $"{(c.Name.Length > 0 ? c.Name : c.Code)} ({variant})", File = w, Thumbnail = c.Thumbnail, Car = c });
            }
        string parts = Path.Combine(root, "carparts", "hq");
        if (!Directory.Exists(parts)) return list;
        var wheels = db?.AftermarketWheels() ?? [];
        string thumbs = Path.Combine(root, "carparts", "wheel_thumb");
        var wing = new Dictionary<string, string> { ["we"] = "wing endplate", ["wm"] = "wing mount", ["wp"] = "wing plane", ["ws"] = "wing stay" };
        foreach (var f in Directory.GetFiles(parts).Order())
        {
            string id = Path.GetFileName(f);
            var m = Regex.Match(id, @"^(he|we|wm|wp|ws)(\d{3})$");
            if (!m.Success) continue;
            int n = int.Parse(m.Groups[2].Value);
            var e = new PartEntry { Code = id, File = f, RaceFile = Existing(Path.Combine(root, "carparts", "race", id)) };
            if (m.Groups[1].Value == "he")
            {
                e.Kind = "aftermarket wheel";
                if (wheels.TryGetValue(n, out var w))
                {
                    e.Name = w.Name;
                    e.Info = $"{w.Diameter / 10.0:0.#} inch, {w.Width} mm wide, {w.Spokes} spokes";
                }
                else e.Name = $"wheel {n}";
                e.Thumbnail = Existing(Path.Combine(thumbs, $"{n:D3}_00"));
            }
            else
            {
                e.Kind = "wing part";
                e.Name = $"{wing[m.Groups[1].Value]} {n}";
            }
            list.Add(e);
        }
        return list;
    }

    /// <summary>GT6 names from its own spec database (specdb\GT6\DB0106.dat), empty when missing or unreadable.</summary>
    static Dictionary<string, (string Name, string Label, int Year)> ReadGt6Names(string root)
    {
        try
        {
            if (Gt6SpecDb.Find(root) is not { } f) return [];
            using var db = Gt6SpecDb.Open(f);
            return db.CarNames();
        }
        catch { return []; }
    }

    /// <summary>The GT5 spec database: in a GT5 folder, or in a GT5 folder next to a GT6 one.</summary>
    static string? FindSpecDb(string root, bool gt5)
    {
        string own = Path.Combine(root, "specdb", "GT5_JP3009");
        if (gt5 && Directory.Exists(own)) return own;
        var parent = Path.GetDirectoryName(root);
        if (parent == null) return null;
        return Directory.GetDirectories(parent).Select(d => Path.Combine(d, "specdb", "GT5_JP3009")).FirstOrDefault(Directory.Exists);
    }

    /// <summary>Model code -> (name, label, year) from VARIATION, CAR_NAME_british and GENERIC_CAR (GT5_JP3009 layouts).</summary>
    static Dictionary<string, (string Name, string Label, int Year)> ReadNames(string folder)
    {
        var result = new Dictionary<string, (string, string, int)>();
        try
        {
            var uni = new GtStringDb(Path.Combine(folder, "UnistrDB.sdb"));
            var british = new GtStringDb(Path.Combine(folder, "british_StrDB.sdb"));
            var variation = new GtDbTable(folder, "VARIATION");
            var carName = new GtDbTable(folder, "CAR_NAME_british");
            var generic = new GtDbTable(folder, "GENERIC_CAR");
            int I(GtDbTable t, byte[] r, int at) => t.BigEndian ? BinaryPrimitives.ReadInt32BigEndian(r.AsSpan(at)) : BinaryPrimitives.ReadInt32LittleEndian(r.AsSpan(at));
            var names = new Dictionary<int, string>();
            for (int i = 0; i < carName.RowCount; i++) names[carName.RowId(i)] = british[I(carName, carName.Row(i), 0)];
            var years = new Dictionary<int, int>();
            for (int i = 0; i < generic.RowCount; i++)
            {
                var r = generic.Row(i);
                years[generic.RowId(i)] = generic.BigEndian ? BinaryPrimitives.ReadInt16BigEndian(r.AsSpan(32)) : BinaryPrimitives.ReadInt16LittleEndian(r.AsSpan(32));
            }
            for (int i = 0; i < variation.RowCount; i++)
            {
                int id = variation.RowId(i);
                string code = uni[I(variation, variation.Row(i), 0)];
                if (!Regex.IsMatch(code, @"^\d{8}$") || result.ContainsKey(code)) continue;
                string label = variation.Labels.GetValueOrDefault(id, "");
                result[code] = (names.GetValueOrDefault(id, label), label, years.GetValueOrDefault(id));
            }
        }
        catch { }
        return result;
    }

    /// <summary>
    /// GT6 cars missing from the GT5 database: the Maya project folder in the texture paths of the model
    /// (".../Projects/Mazda/Mazda_1989_ROADSTER/..." or ".../gt6/Mazda_2010_Demio/sourceimages/...").
    /// </summary>
    public static string? GuessName(string modelPath)
    {
        try
        {
            var text = Encoding.Latin1.GetString(File.ReadAllBytes(modelPath));
            var m = Regex.Matches(text, @"/(?:Projects/[^/\x00]+|gt6|gt5|GTcar)/([A-Za-z][A-Za-z0-9\-]*_(?:19|20)\d\d_[A-Za-z0-9_\-]+)/")
                .Select(x => x.Groups[1].Value).GroupBy(x => x).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
            if (m == null)
            {
                // else the project folder of the textures: .../gt/gt5/NISSAN_Leaf_/sourceimages, .../gt/gt6/std/rx8_tmv_01/...
                var folder = Regex.Matches(text, @"/gt/gt[56]/(?:std/|prm/|premium/)?([^/\x00]{3,60})/sourceimages/")
                    .Select(x => x.Groups[1].Value).Where(x => !Regex.IsMatch(x, @"^(public|common|texture|personal|GT_CALIPER|GT_BRAKE\w*|caliper|brake\w*)$", RegexOptions.IgnoreCase))
                    .GroupBy(x => x).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
                return folder == null ? null : "[" + folder.Trim('_') + "]";   // in brackets: a folder name, not a car name
            }
            var parts = m.Split('_');
            // Maker_Year_Model -> "Maker Model 'YY"
            return $"{parts[0]} {string.Join(' ', parts.Skip(2))} '{parts[1][2..]}";
        }
        catch { return null; }
    }

    /// <summary>A thumbnail label for a guessed name: the model words and the two-digit year all in the label.</summary>
    static string? GuessLabel(string name, List<string> labels)
    {
        if (name.StartsWith('[')) { string f = name.Trim('[', ']').ToLowerInvariant(); return labels.Contains(f) ? f : null; }
        var m = Regex.Match(name, @"^(\S+) (.+) '(\d\d)$");
        if (!m.Success) return null;
        var words = m.Groups[2].Value.ToLowerInvariant().Split(' ', '-').Where(w => w.Length > 1).ToList();
        string yy = m.Groups[3].Value;
        return labels.Where(l => l.EndsWith("_" + yy) && words.All(w => l.Contains(w))).OrderBy(l => l.Length).FirstOrDefault();
    }
}
