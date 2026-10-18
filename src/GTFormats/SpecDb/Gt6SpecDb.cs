using Microsoft.Data.Sqlite;

namespace GTFormats.SpecDb;

/// <summary>
/// The GT6 spec database: `specdb\GT6\DB0106.dat` (the highest DB0xxx is the newest), a SQLite file encrypted with
/// Salsa20 (<see cref="Salsa20.Gt6SpecDbKey"/>). It is decrypted once into a cache folder (never next to the game
/// data) and opened read-only. 65 tables; the car is GENERIC_CAR (CarLabel, ModelCode = maker * 10000 + car = the
/// model folders car\MMMM\CCCC, Year), its name CAR_NAME_ALPHABET (Label = CarLabel), its colours VARIATION (Label,
/// VarOrder, NameEng), its parts DEFAULT_PARTS -> ENGINE, CHASSIS, GEAR, DRIVETRAIN, SUSPENSION, BRAKE, tyres.
/// </summary>
public sealed class Gt6SpecDb : IDisposable
{
    public SqliteConnection Connection { get; }
    public string SourcePath { get; }

    Gt6SpecDb(string source, string plain)
    {
        SourcePath = source;
        Connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = plain, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        Connection.Open();
    }

    public void Dispose() => Connection.Dispose();

    /// <summary>The newest spec database of a GT6 folder (GT6_VOL), or null.</summary>
    public static string? Find(string root)
    {
        string dir = Path.Combine(root, "specdb", "GT6");
        return Directory.Exists(dir) ? Directory.GetFiles(dir, "DB*.dat").OrderByDescending(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).FirstOrDefault() : null;
    }

    /// <summary>Opens an encrypted DB0xxx.dat; the plain copy goes to <paramref name="cacheDir"/> (default %LOCALAPPDATA%\GTViewer\specdb).</summary>
    public static Gt6SpecDb Open(string encrypted, string? cacheDir = null)
    {
        cacheDir ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GTViewer", "specdb");
        var info = new FileInfo(encrypted);
        string plain = Path.Combine(cacheDir, $"{Path.GetFileNameWithoutExtension(encrypted)}_{info.Length}_{info.LastWriteTimeUtc.Ticks:x}.sqlite");
        if (!File.Exists(plain))
        {
            var data = File.ReadAllBytes(encrypted);
            Salsa20.Crypt(data, Salsa20.Gt6SpecDbKey);
            if (System.Text.Encoding.ASCII.GetString(data, 0, 15) != "SQLite format 3") throw new InvalidDataException($"{encrypted}: not a GT6 spec database (decrypted header is not SQLite)");
            Directory.CreateDirectory(cacheDir);
            File.WriteAllBytes(plain + ".tmp", data);
            File.Move(plain + ".tmp", plain, true);
        }
        return new Gt6SpecDb(encrypted, plain);
    }

    /// <summary>Model code (8 digits, maker + car folder) -> (name, label, year).</summary>
    public Dictionary<string, (string Name, string Label, int Year)> CarNames()
    {
        var result = new Dictionary<string, (string, string, int)>();
        using var c = Connection.CreateCommand();
        c.CommandText = "SELECT g.ModelCode, g.CarLabel, g.Year, n.Name FROM GENERIC_CAR g LEFT JOIN CAR_NAME_ALPHABET n ON n.Label = g.CarLabel";
        using var r = c.ExecuteReader();
        while (r.Read())
        {
            string code = r.GetInt64(0).ToString("D8");
            string label = r.GetString(1);
            result.TryAdd(code, (r.IsDBNull(3) ? label : r.GetString(3), label, r.IsDBNull(2) ? 0 : r.GetInt32(2)));
        }
        return result;
    }

    static readonly string[] Letters = [.. "ABCDEFGHIJKLMNOPQRSTUVWX".Select(ch => ch.ToString())];
    static readonly string Curve = string.Join(", ", Letters.Select(l => $"e.torque{l}, e.rpm{l}"));

    /// <summary>
    /// The dealer sheet of a car (model code = maker * 10000 + car) as readable lines, or null when the code is not in
    /// the database. Sources: GENERIC_CAR (year, price, PP, weight, flags; Defaultparts = DEFAULT_PARTS.ID), CAR_VIEW
    /// (the game's own spec sheet: drive, displacement, power PS / torque kgf*m x 100 with their rpm, size, mass),
    /// CAR_NAME_ALPHABET, ENGINE (layout, cam, aspiration, red line x 100, rev limit, torque curve) and CHASSIS (size,
    /// front weight per mille), WHEEL (tyre sizes: diameter inch x 10, width mm, aspect %), VARIATION (colours).
    /// <paramref name="label"/> (a GT5 database label) finds the car when GT6 keeps it under another model code
    /// (GT5 00010007 rx7_spiritr_a_02_std and 00010105 rx7_spiritr_a_02, GT6 has only 10105).
    /// </summary>
    public List<(string Key, string Value)>? CarSheet(long modelCode, string? label = null)
    {
        using var c = Connection.CreateCommand();
        c.CommandText = $"""
            SELECT g.CarLabel, g.ModelCode, g.Year, g.Price, g.PP, g.Weight, g.PremiumCar, g.OpenCar, g.Dirt, g.DLC,
                   n.Name, n.Grade, v.DriveTypeName, v.displacement, v.psvalue, v.psrpm, v.torquevalue, v.torquerpm,
                   v.dlength, v.dwidth, v.dheight, co.Label AS country, e.enginetype, e.cam, e.aspiration, e.RedLine,
                   e.revlimit, ch.percentageF, w.FrontWidth, w.FrontAspect, w.FrontDiameter, w.RearWidth, w.RearAspect,
                   w.RearDiameter, ch.length, ch.width, ch.height, {Curve}
            FROM GENERIC_CAR g
            LEFT JOIN CAR_NAME_ALPHABET n ON n.Label = g.CarLabel
            LEFT JOIN CAR_VIEW v ON v.Label = g.CarLabel
            LEFT JOIN COUNTRY co ON co.code = v.CountryFlag
            LEFT JOIN DEFAULT_PARTS d ON d.ID = g.Defaultparts
            LEFT JOIN ENGINE e ON e.ID = d.ENGINE
            LEFT JOIN CHASSIS ch ON ch.ID = d.CHASSIS
            LEFT JOIN WHEEL w ON w.Label = g.CarLabel
            WHERE g.ModelCode = $m OR g.CarLabel = $l
            ORDER BY g.ModelCode = $m DESC
            LIMIT 1
            """;
        c.Parameters.AddWithValue("$m", modelCode);
        // GT5 standard-model twins carry "_std" (rx7_spiritr_a_02_std = GT6 rx7_spiritr_a_02)
        c.Parameters.AddWithValue("$l", label == null ? "" : label.EndsWith("_std") ? label[..^4] : label);
        using var r = c.ExecuteReader();
        if (!r.Read()) return null;
        string S(string col) { var o = r[col]; return o is DBNull ? "" : Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture)!.Trim(); }
        long N(string col) => long.TryParse(S(col), out long x) ? x : 0;
        string Rpm(string col) => N(col) > 0 ? $" at {N(col)} rpm" : "";

        var lines = new List<(string, string)>();
        string name = S("Name"), grade = S("Grade");
        label = S("CarLabel");
        long found = N("ModelCode");
        string maker = name.Length > grade.Length && grade.Length > 0 && name.EndsWith(grade) ? name[..^grade.Length].Trim() : "";
        if (maker.Length > 0) lines.Add(("Maker", maker));
        string country = S("country");
        if (country.Length > 1) lines.Add(("Country", country.Length <= 3 ? country.ToUpperInvariant() : char.ToUpperInvariant(country[0]) + country[1..]));
        if (N("Year") > 0) lines.Add(("Year", N("Year").ToString()));
        var kind = new List<string> { N("PremiumCar") != 0 ? "Premium" : "Standard" };
        if (N("OpenCar") != 0) kind.Add("open top");
        if (N("DLC") != 0) kind.Add("DLC");
        lines.Add(("Model", string.Join(", ", kind)));
        if (N("Price") > 0) lines.Add(("Price", $"{N("Price"):N0} Cr."));
        if (N("PP") > 0) lines.Add(("PP", N("PP").ToString()));

        string engine = string.Join(" ", new[] { N("displacement") > 0 ? $"{N("displacement")} cc" : "", S("enginetype"), S("cam"), S("aspiration") }.Where(x => x.Length > 0 && x != "-"));
        if (engine.Length > 0) lines.Add(("Engine", engine));
        // the engine's torque curve: torqueA..X in kgf*m x 100 at rpmA..X x 100 rpm (0 = unused point); the dealer
        // sheet leaves power / torque at 0 on some cars (e.g. torque of the Scion FR-S), then the curve gives them
        var curve = Letters.Select(l => (Kgfm: N("torque" + l) / 100.0, Rpm: N("rpm" + l) * 100)).Where(p => p.Kgfm > 0 && p.Rpm > 0).ToList();
        var peakT = curve.Count > 0 ? curve.MaxBy(p => p.Kgfm) : default;
        var peakP = curve.Count > 0 ? curve.MaxBy(p => p.Kgfm * p.Rpm) : default;
        string Ps(double ps, string at) => $"{ps:0} PS ({ps * 0.98632:0} hp, {ps * 0.73550:0} kW){at}";
        string Nm(double kgfm, string at) => $"{kgfm * 9.80665:0} Nm ({kgfm:0.0} kgf*m){at}";
        if (N("psvalue") > 0) lines.Add(("Power", Ps(N("psvalue"), Rpm("psrpm"))));
        else if (curve.Count > 0) lines.Add(("Power", Ps(peakP.Kgfm * peakP.Rpm / 716.2, $" at {peakP.Rpm} rpm (from the torque curve)")));
        if (N("torquevalue") > 0) lines.Add(("Torque", Nm(N("torquevalue") / 100.0, Rpm("torquerpm"))));
        else if (curve.Count > 0) lines.Add(("Torque", Nm(peakT.Kgfm, $" at {peakT.Rpm} rpm (from the torque curve)")));
        if (N("RedLine") > 0 || N("revlimit") > 0) lines.Add(("Red line", $"{N("RedLine") * 100} rpm, rev limit {N("revlimit")} rpm"));
        if (S("DriveTypeName").Length > 0) lines.Add(("Drive", S("DriveTypeName")));
        if (N("Weight") > 0) lines.Add(("Weight", $"{N("Weight")} kg" + (N("percentageF") > 0 ? $", front {N("percentageF") / 10.0:0.#}%" : "")));
        if (N("dlength") > 0) lines.Add(("Size", $"{N("dlength")} x {N("dwidth")} x {N("dheight")} mm (L x W x H)"));
        else if (N("length") > 0) lines.Add(("Size", $"{N("length")} x {N("width")} x {N("height")} mm (L x W x H)"));
        string Tyre(string side) => N(side + "Width") > 0 ? $"{N(side + "Width")}/{N(side + "Aspect")} R{N(side + "Diameter") / 10.0:0.#}" : "";
        string f = Tyre("Front"), rr = Tyre("Rear");
        if (f.Length > 0) lines.Add(("Tyres", f == rr || rr.Length == 0 ? f : $"{f} front, {rr} rear"));
        r.Close();

        var colours = ColorNames(label);
        if (colours.Count > 0) lines.Add(("Colours", colours.All(x => x.Length == 0) ? $"{colours.Count} (no names)" : $"{colours.Count}: {string.Join(", ", colours)}"));
        lines.Add(("Label", label));
        if (found != modelCode) lines.Add(("GT6 model", $"{found:D8} (same label, another model code)"));
        return lines;
    }

    /// <summary>GT6 aftermarket wheels (WHEEL_EX): ID (= carparts\hq\heNNN) -> name, diameter (inch x 10), width (mm), spokes.</summary>
    public Dictionary<int, (string Name, int Diameter, int Width, int Spokes)> AftermarketWheels()
    {
        var result = new Dictionary<int, (string, int, int, int)>();
        using var c = Connection.CreateCommand();
        c.CommandText = "SELECT ID, NameEng, Diameter, Width, NumSpoke FROM WHEEL_EX";
        using var r = c.ExecuteReader();
        while (r.Read()) result[r.GetInt32(0)] = (r.IsDBNull(1) ? "" : r.GetString(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4));
        return result;
    }

    /// <summary>The colour names of a car in VarOrder (the paint variations of body_s, item 5a).</summary>
    public List<string> ColorNames(string label)
    {
        var list = new List<string>();
        using var c = Connection.CreateCommand();
        c.CommandText = "SELECT NameEng FROM VARIATION WHERE Label = $l ORDER BY VarOrder";
        c.Parameters.AddWithValue("$l", label);
        using var r = c.ExecuteReader();
        while (r.Read()) list.Add(r.IsDBNull(0) ? "" : r.GetString(0));
        return list;
    }
}
