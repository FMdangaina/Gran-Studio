using System.Globalization;
using GTFormats.SpecDb;
using Microsoft.Data.Sqlite;

/// <summary>
/// ROADMAP 9c research: everything the two games' databases hold about one car, to map GT fields to FM4 fields.
/// car-data gt6 &lt;DB0106.dat | plain.sqlite&gt; &lt;label&gt; &lt;out.txt&gt;: GENERIC_CAR, CAR_VIEW, MODEL_INFO, the DEFAULT_PARTS row
/// and every default part row (column name = table name; ids 4294967295 = none), the tyre size / compound / force rows,
/// the paints, then every upgrade part of the car (part tables with UseCar = the car's GENERIC_CAR.ID) by category.
/// car-data fm4 &lt;gamedb.slt&gt; &lt;car id&gt; &lt;out.txt&gt;: Data_Car, then every row of every table that refers to the car
/// (an Ordinal / CarID column = id, or an Id in id*1000..id*1000+999), the engine (Data_Engine + every row with
/// EngineID = it or Id in EngineID*1000..+999) and the rows the found ones point to by *ID columns of other tables.
/// Opened read only; only non-empty columns are printed.
/// </summary>
static class CarData
{
    const long None = 4294967295;

    public static int Run(string[] args)
    {
        if (args.Length < 4) { Console.Error.WriteLine("car-data gt6 <DB0106.dat|plain.sqlite> <label> <out.txt> | car-data fm4 <gamedb.slt> <car id> <out.txt>"); return 1; }
        if (args[0] == "cmp") return Compare(args[1], args[2], args[3], args[4..]);
        if (args[0] == "sql")
        {
            using var spec = args[1].EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ? Gt6SpecDb.Open(args[1]) : null;
            using var own = spec == null ? OpenReadOnly(args[1]) : null;
            using var o = new StreamWriter(args[3]);
            Dump(o, "row", Rows(spec?.Connection ?? own!, args[2]));
            return 0;
        }
        using var w = new StreamWriter(args[3]);
        if (args[0] == "gt6") Gt6(args[1], args[2], w);
        else Fm4(args[1], long.Parse(args[2]), w);
        Console.WriteLine($"-> {args[3]}");
        return 0;
    }

    static string V(object o) => o is DBNull ? "" : Convert.ToString(o, CultureInfo.InvariantCulture)!;

    static List<Dictionary<string, object>> Rows(SqliteConnection db, string sql, params (string, object)[] p)
    {
        using var c = db.CreateCommand();
        c.CommandText = sql;
        foreach (var (k, v) in p) c.Parameters.AddWithValue(k, v);
        using var r = c.ExecuteReader();
        var list = new List<Dictionary<string, object>>();
        while (r.Read())
        {
            var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < r.FieldCount; i++) d[r.GetName(i)] = r.GetValue(i);
            list.Add(d);
        }
        return list;
    }

    static List<(string Table, List<string> Columns)> Tables(SqliteConnection db)
    {
        var list = new List<(string, List<string>)>();
        foreach (var t in Rows(db, "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name"))
        {
            string name = V(t["name"]);
            list.Add((name, [.. Rows(db, $"PRAGMA table_info(\"{name}\")").Select(c => V(c["name"]))]));
        }
        return list;
    }

    static void Dump(StreamWriter w, string title, IEnumerable<Dictionary<string, object>> rows)
    {
        foreach (var r in rows)
        {
            w.WriteLine($"== {title}");
            foreach (var (k, v) in r)
            {
                string s = V(v);
                if (s.Length == 0 || s == None.ToString()) continue;
                w.WriteLine($"   {k,-28} {s}");
            }
        }
    }

    static SqliteConnection OpenReadOnly(string path)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        db.Open();
        return db;
    }

    static void Gt6(string path, string label, StreamWriter w)
    {
        using var spec = path.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ? Gt6SpecDb.Open(path) : null;
        using var own = spec == null ? OpenReadOnly(path) : null;
        var db = spec?.Connection ?? own!;
        var tables = Tables(db).ToDictionary(t => t.Table, t => t.Columns, StringComparer.OrdinalIgnoreCase);
        var car = Rows(db, "SELECT * FROM GENERIC_CAR WHERE CarLabel = $l", ("$l", label)).Single();
        w.WriteLine($"GT6 {label}: GENERIC_CAR.ID {V(car["ID"])}, Defaultparts {V(car["Defaultparts"])}");
        Dump(w, "GENERIC_CAR", [car]);
        foreach (var t in new[] { "CAR_NAME_ALPHABET", "CAR_VIEW", "CAR_CUSTOM_INFO", "RACINGMODIFY" })
            if (tables.ContainsKey(t)) Dump(w, t, Rows(db, $"SELECT * FROM {t} WHERE Label = $l", ("$l", label)));
        Dump(w, "MODEL_INFO", Rows(db, "SELECT * FROM MODEL_INFO WHERE ModelCode = $m", ("$m", car["ModelCode"])));
        Dump(w, "WHEEL", Rows(db, "SELECT * FROM WHEEL WHERE Label = $l OR ModelCode = $m", ("$l", label), ("$m", car["ModelCode"])));
        Dump(w, "VARIATION", Rows(db, "SELECT * FROM VARIATION WHERE Label = $l ORDER BY VarOrder", ("$l", label)));
        var parts = Rows(db, "SELECT * FROM DEFAULT_PARTS WHERE ID = $i", ("$i", car["Defaultparts"])).Single();
        Dump(w, "DEFAULT_PARTS", [parts]);
        // the default parts: DEFAULT_PARTS column = part table, value = its ID
        foreach (var (col, val) in parts)
        {
            if (!tables.ContainsKey(col) || col == "DEFAULT_PARTS" || V(val) is "" || V(val) == None.ToString()) continue;
            var rows = Rows(db, $"SELECT * FROM \"{col}\" WHERE ID = $i", ("$i", val));
            Dump(w, $"default {col} (ID {V(val)})", rows);
            if (col is "FRONTTIRE" or "REARTIRE")
                foreach (var r in rows)
                {
                    Dump(w, $"  {col} tiresize", Rows(db, "SELECT * FROM TIRESIZE WHERE ID = $i", ("$i", r["tiresize"])));
                    foreach (var k in new[] { "tirecompound0", "tirecompound1", "tirecompound2" })
                        Dump(w, $"  {col} {k}", Rows(db, "SELECT * FROM TIRECOMPOUND WHERE ID = $i", ("$i", r[k])));
                    foreach (var k in new[] { "tireforcevol0" })
                        Dump(w, $"  {col} {k}", Rows(db, "SELECT * FROM TIREFORCEVOL WHERE ID = $i", ("$i", r[k])));
                }
        }
        // upgrade parts of the car: UseCar = GENERIC_CAR.ID (checked on the default parts)
        w.WriteLine();
        w.WriteLine("######## upgrade parts (UseCar = the car)");
        foreach (var (t, cols) in tables.OrderBy(x => x.Key))
        {
            if (!cols.Contains("UseCar", StringComparer.OrdinalIgnoreCase)) continue;
            var rows = Rows(db, $"SELECT * FROM \"{t}\" WHERE UseCar = $u ORDER BY category", ("$u", car["ID"]));
            if (rows.Count == 0) continue;
            w.WriteLine($"-- {t}: {rows.Count} rows, categories {string.Join(",", rows.Select(r => V(r.GetValueOrDefault("category") ?? "")))}");
            Dump(w, t, rows);
        }
    }

    /// <summary>
    /// car-data cmp &lt;gt6 db&gt; &lt;fm4 gamedb&gt; &lt;out.txt&gt; label=id...: the same cars in both games side by side, one block
    /// per quantity: GT6 value (raw and converted), FM4 value, FM4 / GT6. A ratio that stays the same over the cars is
    /// a unit conversion; one that changes means the games model it differently.
    /// </summary>
    static int Compare(string gt6Path, string fm4Path, string outPath, string[] pairs)
    {
        using var spec = gt6Path.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ? Gt6SpecDb.Open(gt6Path) : null;
        using var own = spec == null ? OpenReadOnly(gt6Path) : null;
        var g6 = spec?.Connection ?? own!;
        using var f4 = OpenReadOnly(fm4Path);
        var rows = new List<(string Quantity, string Car, double Gt6, double Fm4, string Note)>();
        // numbers, also as text ("654x2" = rotary 2 x 654 cc, "-" = unknown)
        double D(object? o)
        {
            if (o == null || o is DBNull) return double.NaN;
            if (o is not string s) return Convert.ToDouble(o, CultureInfo.InvariantCulture);
            var p = s.Split('x');
            return p.Length == 2 && double.TryParse(p[0], CultureInfo.InvariantCulture, out var a) && double.TryParse(p[1], CultureInfo.InvariantCulture, out var b) ? a * b
                : double.TryParse(s, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
        }
        Dictionary<string, object>? One(SqliteConnection db, string sql, params (string, object)[] p) => Rows(db, sql, p).FirstOrDefault();

        foreach (var pair in pairs)
        {
            string label = pair.Split('=')[0]; long id = long.Parse(pair.Split('=')[1]);
            var car = One(g6, "SELECT * FROM GENERIC_CAR WHERE CarLabel = $l", ("$l", label))!;
            var dp = One(g6, "SELECT * FROM DEFAULT_PARTS WHERE ID = $i", ("$i", car["Defaultparts"]))!;
            Dictionary<string, object> Part(string t) => One(g6, $"SELECT * FROM {t} WHERE ID = $i", ("$i", dp[t])) ?? [];
            var ch = Part("CHASSIS"); var en = Part("ENGINE"); var ge = Part("GEAR"); var su = Part("SUSPENSION");
            var br = Part("BRAKE"); var ls = Part("LSD"); var dt = Part("DRIVETRAIN"); var st = Part("STEER"); var tk = Part("TURBINEKIT");
            var fc = One(f4, "SELECT * FROM Data_Car WHERE Id = $i", ("$i", id))!;
            var body = One(f4, "SELECT * FROM List_UpgradeCarBodyWeight WHERE CarBodyId = $i AND IsStock = 1", ("$i", id * 1000)) ?? [];
            var cb = One(f4, "SELECT * FROM Data_CarBody WHERE Id = $i", ("$i", id * 1000)) ?? [];
            var sd = One(f4, "SELECT * FROM List_UpgradeSpringDamper WHERE Ordinal = $i AND IsStock = 1", ("$i", id)) ?? [];
            var sf = One(f4, "SELECT * FROM List_SpringDamperPhysics WHERE SpringDamperPhysicsID = $i", ("$i", sd.GetValueOrDefault("FrontSpringDamperPhysicsID") ?? 0)) ?? [];
            var sr = One(f4, "SELECT * FROM List_SpringDamperPhysics WHERE SpringDamperPhysicsID = $i", ("$i", sd.GetValueOrDefault("RearSpringDamperPhysicsID") ?? 0)) ?? [];
            var af = One(f4, "SELECT p.* FROM List_UpgradeAntiSwayFront u JOIN List_AntiSwayPhysics p ON p.AntiSwayPhysicsID = u.AntiSwayPhysicsID WHERE u.Ordinal = $i AND u.IsStock = 1", ("$i", id)) ?? [];
            var ar = One(f4, "SELECT p.* FROM List_UpgradeAntiSwayRear u JOIN List_AntiSwayPhysics p ON p.AntiSwayPhysicsID = u.AntiSwayPhysicsID WHERE u.Ordinal = $i AND u.IsStock = 1", ("$i", id)) ?? [];
            // the drivetrain parts hang on the stock List_UpgradeDrivetrain.DrivetrainID (Supra 214), not on the car id
            long dtId = One(f4, "SELECT DrivetrainID FROM List_UpgradeDrivetrain WHERE Ordinal = $i AND IsStock = 1", ("$i", id)) is { } dtr ? Convert.ToInt64(dtr["DrivetrainID"]) : -1;
            var tr = One(f4, "SELECT * FROM List_UpgradeDrivetrainTransmission WHERE DrivetrainID = $i AND IsStock = 1", ("$i", dtId)) ?? [];
            var df = One(f4, "SELECT * FROM List_UpgradeDrivetrainDifferential WHERE DrivetrainID = $i AND IsStock = 1", ("$i", dtId)) ?? [];
            var fb = One(f4, "SELECT * FROM List_UpgradeBrakes WHERE Ordinal = $i AND IsStock = 1", ("$i", id)) ?? [];
            var ue = One(f4, "SELECT * FROM List_UpgradeEngine WHERE Ordinal = $i AND IsStock = 1", ("$i", id)) ?? [];
            long eng = ue.TryGetValue("EngineID", out var eo) ? Convert.ToInt64(eo) : 0;
            var cam = One(f4, "SELECT * FROM List_UpgradeEngineCamshaft WHERE EngineID = $e AND IsStock = 1", ("$e", eng)) ?? [];
            var fly = One(f4, "SELECT * FROM List_UpgradeEngineFlywheel WHERE EngineID = $e AND IsStock = 1", ("$e", eng)) ?? [];
            // FM4 peak torque: normalized curve x TorqueScale x the stock turbo / supercharger MaxScale
            double fmTorque = double.NaN, fmTorqueRpm = double.NaN;
            if (cam.TryGetValue("TorqueCurveFullThrottleID", out var tcid))
            {
                var tc = One(f4, "SELECT * FROM List_TorqueCurve WHERE TorqueCurveID = $i", ("$i", tcid));
                if (tc != null)
                {
                    int n = (int)D(tc["NumTorqueValues"]); double maxRpm = D(cam["TorqueCurveMaxRPM"]), scale = D(tc["TorqueScale"]);
                    double boost = 1;
                    foreach (var t in new[] { "List_UpgradeEngineTurboSingle", "List_UpgradeEngineTurboTwin", "List_UpgradeEngineTurboQuad", "List_UpgradeEngineCSC", "List_UpgradeEngineDSC" })
                        if (One(f4, $"SELECT name FROM sqlite_master WHERE name = '{t}'") != null && One(f4, $"SELECT * FROM {t} WHERE EngineID = $e AND IsStock = 1", ("$e", eng)) is { } b)
                            boost = b.TryGetValue("MaxScale", out var ms) ? D(ms) : b.TryGetValue("RedlineRPMScale", out var rs) ? D(rs) : 1;
                    for (int i = 0; i < n - 1; i++)
                    {
                        double v = D(tc[$"v{i}"]) * scale * boost;
                        if (!(v <= fmTorque)) { fmTorque = v; fmTorqueRpm = i * maxRpm / (n - 1); }
                    }
                }
            }
            var gtCurve = Enumerable.Range(0, 24).Select(i => (T: D(en.GetValueOrDefault("torque" + (char)('A' + i))) / 100 * 9.80665, R: D(en.GetValueOrDefault("rpm" + (char)('A' + i))) * 100)).Where(p => p.T > 0).ToList();
            var gtPeak = gtCurve.Count > 0 ? gtCurve.MaxBy(p => p.T) : (T: double.NaN, R: double.NaN);
            string c = $"{label} / {fc["MediaName"]}";
            void Add(string q, double gt, double fm, string note = "") => rows.Add((q, c, gt, fm, note));
            Add("mass kg | CurbWeight x100", D(ch.GetValueOrDefault("mass")), D(fc["CurbWeight"]) * 100);
            Add("front weight | percentageF/1000 vs WeightDistribution", D(ch.GetValueOrDefault("percentageF")) / 1000, D(fc["WeightDistribution"]));
            Add("wheelbase m", D(ch.GetValueOrDefault("wheelbase")) / 1000, D(cb.GetValueOrDefault("ModelWheelbase")));
            Add("front track m (GT6 tread, FM4 outer)", D(ch.GetValueOrDefault("treadF")) / 1000, D(cb.GetValueOrDefault("ModelFrontTrackOuter")));
            Add("cg height m", D(ch.GetValueOrDefault("cgheight")) / 1000, D(body.GetValueOrDefault("CMHeight")));
            double area = D(ch.GetValueOrDefault("width")) / 1000 * D(ch.GetValueOrDefault("height")) / 1000 * 0.85;
            double cd = D(ch.GetValueOrDefault("cd")) / 1000;
            Add("drag: Cd / BodyAeroLongitudinalDrag", cd, D(fc["BodyAeroLongitudinalDrag"]));
            Add("drag lbf at 100 mph (0.5 rho Cd A v^2, A = 0.85 W H)", 0.5 * 1.225 * cd * area * 44.704 * 44.704 / 4.44822, D(fc["BodyAeroLongitudinalDrag"]));
            Add("peak torque Nm", gtPeak.T, fmTorque);
            Add("peak torque rpm", gtPeak.R, fmTorqueRpm);
            Add("red line rpm", D(en.GetValueOrDefault("RedLine")) * 100, D(cam.GetValueOrDefault("RedlineRPM")));
            Add("idle rpm", D(en.GetValueOrDefault("idlerpm")), D(cam.GetValueOrDefault("StallRPM")));
            Add("flywheel inertia (iflywheel vs MomentInertia)", D(dt.GetValueOrDefault("iflywheel")), D(fly.GetValueOrDefault("MomentInertia")));
            for (int gIdx = 1; gIdx <= 6; gIdx++)
                Add($"gear {gIdx}", D(ge.GetValueOrDefault(new[] { "", "Gear1st", "Gear2nd", "Gear3rd", "Gear4th", "Gear5th", "Gear6th" }[gIdx])) / 1000, D(tr.GetValueOrDefault($"GearRatio{gIdx}")));
            Add("final drive", D(ge.GetValueOrDefault("finalgearDF")) / 1000, D(tr.GetValueOrDefault("FinalDriveRatio")));
            Add("reverse (GearReverse vs -GearRatio0)", D(ge.GetValueOrDefault("GearReverse")) / 1000, -D(tr.GetValueOrDefault("GearRatio0")));
            double lever(string k) => D(su.GetValueOrDefault(k)) / 1000;
            Add("spring F: kgf/mm (springrate/100) vs DefSpringRate", D(su.GetValueOrDefault("springrateDFF")) / 100, D(sf.GetValueOrDefault("DefSpringRate")));
            Add("spring R", D(su.GetValueOrDefault("springrateDFR")) / 100, D(sr.GetValueOrDefault("DefSpringRate")));
            Add("wheel rate F N/mm (k x lever^2 x 9.81)", D(su.GetValueOrDefault("springrateDFF")) / 100 * Math.Pow(lever("leverratioDFF"), 2) * 9.80665, D(sf.GetValueOrDefault("DefSpringRate")));
            Add("wheel rate R N/mm", D(su.GetValueOrDefault("springrateDFR")) / 100 * Math.Pow(lever("leverratioDFR"), 2) * 9.80665, D(sr.GetValueOrDefault("DefSpringRate")));
            Add("anti-roll F (stabilizer vs DefSwaybarStiffness)", D(su.GetValueOrDefault("stabilizerMINF")), D(af.GetValueOrDefault("DefSwaybarStiffness")));
            Add("anti-roll R", D(su.GetValueOrDefault("stabilizerMINR")), D(ar.GetValueOrDefault("DefSwaybarStiffness")));
            Add("damper bump F (dampF1BMINF vs DefDampenBumpRate)", D(su.GetValueOrDefault("dampF1BMINF")), D(sf.GetValueOrDefault("DefDampenBumpRate")));
            Add("damper rebound F (dampF1RMINF vs DefDampenReboundRate)", D(su.GetValueOrDefault("dampF1RMINF")), D(sf.GetValueOrDefault("DefDampenReboundRate")));
            Add("damper bump R", D(su.GetValueOrDefault("dampF1BMINR")), D(sr.GetValueOrDefault("DefDampenBumpRate")));
            Add("ride height F (rideheightDFF mm vs DefRideHeight m)", D(su.GetValueOrDefault("rideheightDFF")) / 1000, D(sf.GetValueOrDefault("DefRideHeight")));
            Add("camber F (camberDFF vs StaticCamber)", D(su.GetValueOrDefault("camberDFF")), D(sf.GetValueOrDefault("StaticCamber")));
            Add("toe R (toeDFR vs StaticToe rear)", D(su.GetValueOrDefault("toeDFR")), D(sr.GetValueOrDefault("StaticToe")));
            Add("caster (caster vs Caster)", D(su.GetValueOrDefault("caster")), D(sf.GetValueOrDefault("Caster")));
            Add("brake F (BraketorqueF vs FrontBrakeSize in)", D(br.GetValueOrDefault("BraketorqueF")), D(fb.GetValueOrDefault("FrontBrakeSize")));
            Add("brake balance F/(F+R) vs BrakeBiasSlider", D(br.GetValueOrDefault("BraketorqueF")) / (D(br.GetValueOrDefault("BraketorqueF")) + D(br.GetValueOrDefault("BraketorqueR"))), D(fb.GetValueOrDefault("BrakeBiasSlider")));
            Add("LSD rear accel (LSDparam2R vs RearLimitedSlipTorqueAccel)", D(ls.GetValueOrDefault("LSDparam2R")), D(df.GetValueOrDefault("RearLimitedSlipTorqueAccel")));
            Add("LSD rear decel (LSDparam3R vs RearLimitedSlipTorqueDecel)", D(ls.GetValueOrDefault("LSDparam3R")), D(df.GetValueOrDefault("RearLimitedSlipTorqueDecel")));
            Add("LSD rear initial (LSDparamRDF)", D(ls.GetValueOrDefault("LSDparamRDF")), double.NaN);
            Add("steering lock deg (steerlimit vs SteerMaxAngle)", D(st.GetValueOrDefault("steerlimit")), D(fc["SteerMaxAngle"]));
            Add("turbo boost (boost2 vs StockBoost-bar)", D(tk.GetValueOrDefault("boost2")), D(One(f4, "SELECT * FROM Data_Engine WHERE EngineID = $e", ("$e", eng))?.GetValueOrDefault("StockBoost-bar")));
            Add("displacement cc", D(en.GetValueOrDefault("displacement")), D(fc["Displacement"]));
        }
        using var w = new StreamWriter(outPath);
        var ci = CultureInfo.InvariantCulture;
        foreach (var g in rows.GroupBy(r => r.Quantity))
        {
            var ratios = g.Select(r => r.Fm4 / r.Gt6).Where(x => !double.IsNaN(x) && !double.IsInfinity(x)).ToList();
            w.WriteLine($"== {g.Key}   FM4/GT6: mean {(ratios.Count > 0 ? ratios.Average().ToString("0.###", ci) : "-")}, spread {(ratios.Count > 1 ? (ratios.Max() / ratios.Min()).ToString("0.##", ci) : "-")}");
            foreach (var r in g) w.WriteLine(string.Create(ci, $"   {r.Car,-55} GT6 {r.Gt6,10:0.####}   FM4 {r.Fm4,10:0.####}   ratio {r.Fm4 / r.Gt6,8:0.###}"));
        }
        Console.WriteLine($"{pairs.Length} cars -> {outPath}");
        return 0;
    }

    static void Fm4(string path, long carId, StreamWriter w)
    {
        using var db = OpenReadOnly(path);
        var tables = Tables(db);
        var car = Rows(db, "SELECT * FROM Data_Car WHERE Id = $i", ("$i", carId)).Single();
        long engine = Convert.ToInt64(car.FirstOrDefault(x => x.Key.Equals("EngineID", StringComparison.OrdinalIgnoreCase)).Value ?? 0);
        // the stock engine is in List_UpgradeEngine (Level 0 = car*1000)
        foreach (var u in Rows(db, "SELECT * FROM List_UpgradeEngine WHERE Id = $i", ("$i", carId * 1000)))
            if (u.TryGetValue("EngineID", out var e)) engine = Convert.ToInt64(e);
        w.WriteLine($"FM4 Data_Car {carId}: {V(car.GetValueOrDefault("MediaName") ?? "")}, engine {engine}");
        Dump(w, "Data_Car", [car]);
        var seen = new HashSet<(string, string)>();
        var found = new List<(string Table, Dictionary<string, object> Row)>();
        foreach (var (t, cols) in tables)
        {
            if (t == "Data_Car") continue;
            var where = new List<string>();
            foreach (var c in cols)
            {
                string lc = c.ToLowerInvariant();
                if (lc is "ordinal" or "carid" or "car_id") where.Add($"\"{c}\" = {carId}");
                if (lc is "id") where.Add($"(\"{c}\" BETWEEN {carId * 1000} AND {carId * 1000 + 999})");
                if (engine > 0 && lc is "engineid") where.Add($"\"{c}\" = {engine}");
                if (engine > 0 && lc is "id" && t.Contains("Engine", StringComparison.OrdinalIgnoreCase)) where.Add($"(\"{c}\" = {engine} OR \"{c}\" BETWEEN {engine * 1000} AND {engine * 1000 + 999})");
            }
            if (where.Count == 0) continue;
            foreach (var r in Rows(db, $"SELECT * FROM \"{t}\" WHERE {string.Join(" OR ", where)}"))
            {
                found.Add((t, r));
                seen.Add((t, V(r.FirstOrDefault().Value)));
            }
        }
        foreach (var g in found.GroupBy(f => f.Table))
        {
            w.WriteLine();
            w.WriteLine($"-- {g.Key}: {g.Count()} rows");
            Dump(w, g.Key, g.Select(x => x.Row));
        }
        // one hop: *ID columns of the car and the found rows -> the rows with that Id in tables named after the column
        w.WriteLine();
        w.WriteLine("######## referenced rows (column XxxID -> a table whose name ends in Xxx, by Id)");
        var byName = tables.ToDictionary(t => t.Table, t => t.Columns, StringComparer.OrdinalIgnoreCase);
        foreach (var (src, row) in found.Prepend(("Data_Car", car)))
            foreach (var (col, val) in row)
            {
                if (!col.EndsWith("ID", StringComparison.OrdinalIgnoreCase) || col.Equals("Id", StringComparison.OrdinalIgnoreCase) || val is not long id || id <= 0) continue;
                string stem = col[..^2];
                foreach (var t in byName.Keys.Where(t => t.EndsWith(stem, StringComparison.OrdinalIgnoreCase) && byName[t].Any(c => c.Equals("Id", StringComparison.OrdinalIgnoreCase))))
                {
                    if (!seen.Add((t, id.ToString()))) continue;
                    var rows = Rows(db, $"SELECT * FROM \"{t}\" WHERE Id = $i", ("$i", id));
                    if (rows.Count > 0) Dump(w, $"{t} {id} (from {src}.{col})", rows);
                }
            }
    }
}
