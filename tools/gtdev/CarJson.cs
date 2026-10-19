using System.Globalization;
using System.Text.Json;
using GTFormats.SpecDb;
using Microsoft.Data.Sqlite;

/// <summary>
/// ROADMAP 9c: one JSON file per GT6 car for Forza Studio (layout in docs/car_json.md).
/// car-json &lt;DB0106.dat | plain.sqlite&gt; &lt;out dir&gt; &lt;label&gt;... writes &lt;out dir&gt;\&lt;label&gt;.json: the stock GT6 values in SI
/// units ("gt6"), then the FM4 gamedb fields built from them ("fm4": single fields, the full throttle torque curve, the
/// paints), each with kind copy / estimate / default and the GT6 source. Rules: docs/car_data.md.
/// </summary>
static class CarJson
{
    const int Version = 1;
    const double Kgf = 9.80665;

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("car-json <DB0106.dat|plain.sqlite> <out dir> <label>..."); return 1; }
        using var spec = args[0].EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ? Gt6SpecDb.Open(args[0]) : null;
        using var own = spec == null ? Open(args[0]) : null;
        var db = spec?.Connection ?? own!;
        Directory.CreateDirectory(args[1]);
        int bad = 0;
        foreach (var label in args[2..])
        {
            try
            {
                string path = Path.Combine(args[1], label + ".json");
                File.WriteAllBytes(path, Export(db, label, Path.GetFileName(args[0])));
                Console.WriteLine($"{label} -> {path}");
            }
            catch (Exception e) { Console.WriteLine($"{label}: {e.Message}"); bad++; }
        }
        return bad == 0 ? 0 : 1;
    }

    static SqliteConnection Open(string path)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        db.Open();
        return db;
    }

    static Dictionary<string, object>? One(SqliteConnection db, string sql, params (string, object)[] p)
    {
        using var c = db.CreateCommand();
        c.CommandText = sql;
        foreach (var (k, v) in p) c.Parameters.AddWithValue(k, v);
        using var r = c.ExecuteReader();
        if (!r.Read()) return null;
        var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < r.FieldCount; i++) d[r.GetName(i)] = r.GetValue(i);
        return d;
    }

    static List<Dictionary<string, object>> All(SqliteConnection db, string sql, params (string, object)[] p)
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

    /// <summary>A number, also from text ("654x2" = rotary 2 x 654 cc); NaN when missing or "-".</summary>
    static double D(Dictionary<string, object>? row, string col)
    {
        if (row == null || !row.TryGetValue(col, out var o) || o is DBNull) return double.NaN;
        if (o is not string s) return Convert.ToDouble(o, CultureInfo.InvariantCulture);
        var p = s.Split('x');
        return p.Length == 2 && double.TryParse(p[0], CultureInfo.InvariantCulture, out var a) && double.TryParse(p[1], CultureInfo.InvariantCulture, out var b) ? a * b
            : double.TryParse(s, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
    }

    static string S(Dictionary<string, object>? row, string col) =>
        row != null && row.TryGetValue(col, out var o) && o is not DBNull ? Convert.ToString(o, CultureInfo.InvariantCulture)! : "";

    static double R(double v, int digits = 4) => double.IsNaN(v) ? v : Math.Round(v, digits);

    /// <summary>"L6" "V8" "Boxer4" "W12" "Rotor2" (also "Rotar2", "Roter3") -> FM4 List_EngineConfig.ConfigID, cylinders / rotors.</summary>
    static (int Config, int Count, bool Rotary)? Layout(string type)
    {
        string t = type.Trim();
        int i = t.Length;
        while (i > 0 && char.IsDigit(t[i - 1])) i--;
        if (i == t.Length || !int.TryParse(t[i..], out int n)) return null;
        string kind = t[..i].ToLowerInvariant();
        return kind switch
        {
            "l" => (3, n, false),
            "v" => (1, n, false),
            "w" => (2, n, false),
            "boxer" => (5, n, false),
            "rotor" or "rotar" or "roter" => (4, n, true),
            _ => null,
        };
    }

    sealed class Field
    {
        public required string Table, Row, Column, Kind, Gt6, Rule;
        public required object Value;
    }

    static byte[] Export(SqliteConnection db, string label, string dbName)
    {
        var car = One(db, "SELECT * FROM GENERIC_CAR WHERE CarLabel = $l", ("$l", label)) ?? throw new Exception("not in GENERIC_CAR");
        var name = One(db, "SELECT * FROM CAR_NAME_ALPHABET WHERE Label = $l", ("$l", label));
        var view = One(db, "SELECT * FROM CAR_VIEW WHERE Label = $l", ("$l", label));
        var wheel = One(db, "SELECT * FROM WHEEL WHERE Label = $l", ("$l", label));
        var dp = One(db, "SELECT * FROM DEFAULT_PARTS WHERE ID = $i", ("$i", car["Defaultparts"])) ?? throw new Exception("no DEFAULT_PARTS");
        Dictionary<string, object>? Part(string t) => dp.TryGetValue(t, out var id) && id is not DBNull ? One(db, $"SELECT * FROM {t} WHERE ID = $i", ("$i", id)) : null;
        var ch = Part("CHASSIS"); var en = Part("ENGINE"); var ge = Part("GEAR"); var su = Part("SUSPENSION"); var br = Part("BRAKE");
        var ls = Part("LSD"); var dt = Part("DRIVETRAIN"); var st = Part("STEER"); var tk = Part("TURBINEKIT");
        var paints = All(db, "SELECT * FROM VARIATION WHERE Label = $l ORDER BY VarOrder", ("$l", label));

        // ---- GT6 values in SI units
        double mass = D(ch, "mass"), front = D(ch, "percentageF") / 1000, cg = D(ch, "cgheight") / 1000;
        double width = D(ch, "width") / 1000, height = D(ch, "height") / 1000, cd = D(ch, "cd") / 1000;
        double redline = D(en, "RedLine") * 100, revLimit = D(en, "revlimit"), idle = D(en, "idlerpm"), disp = D(en, "displacement");
        string engineType = S(en, "enginetype"), aspiration = S(en, "aspiration"), drive = S(dt, "DriveTypeName");
        var curve = new List<(double Rpm, double Nm)>();
        for (int i = 0; i < 24; i++)
        {
            char c = (char)('A' + i);
            double t = D(en, "torque" + c), rpm = D(en, "rpm" + c) * 100;
            if (t > 0 && rpm > 0) curve.Add((rpm, t / 100 * Kgf));
        }
        var peak = curve.Count > 0 ? curve.MaxBy(p => p.Nm) : (Rpm: double.NaN, Nm: double.NaN);
        string[] gearCols = ["Gear1st", "Gear2nd", "Gear3rd", "Gear4th", "Gear5th", "Gear6th", "Gear7th", "Gear8th", "Gear9th", "Gear10th", "Gear11th"];
        var gears = gearCols.Select(g => D(ge, g) / 1000).TakeWhile(g => g > 0).ToList();
        double reverse = D(ge, "GearReverse") / 1000, final = D(ge, "finalgearDF") / 1000;
        bool forced = aspiration is "TURBO" or "SuperCharger";
        double boostBar = forced ? D(tk, "boost2") * 0.13 : 0;
        double maxScale = forced ? 1 + 0.5 * boostBar : 1;
        double WheelRate(string ax) => D(su, "springrateDF" + ax) / 100 * Math.Pow(D(su, "leverratioDF" + ax) / 1000, 2) * Kgf;
        double brakeF = D(br, "BraketorqueF"), brakeR = D(br, "BraketorqueR");
        (int Placement, int DriveType, string Note) layout = drive switch
        {
            "FF" => (1, 1, ""),
            "FR" => (1, 2, ""),
            "MR" => (2, 2, ""),
            "RR" => (3, 2, ""),
            "4WD" => D(ch, "EngineMount") == 3 ? (2, 3, "4WD + CHASSIS.EngineMount 3 = mid engine (R8, Gallardo, 205 T16...)") : (1, 3, "4WD, EngineMount 0 = front engine"),
            _ => (0, 0, ""),
        };
        int[,] powertrains = { { 0, -1, -1 }, { 1, 2, 3 }, { 4, 5, 6 } }; // FM4 Powertrains.Id: [drive FWD/RWD/AWD - 1, placement front/mid/rear - 1]
        int powertrain = layout.DriveType > 0 ? powertrains[layout.DriveType - 1, layout.Placement - 1] : -1;
        var engLayout = Layout(engineType);

        // ---- FM4 fields
        var fields = new List<Field>();
        void F(string table, string row, string column, object value, string kind, string gt6, string rule)
        {
            if (value is double d && (double.IsNaN(d) || double.IsInfinity(d))) return;
            fields.Add(new Field { Table = table, Row = row, Column = column, Value = value, Kind = kind, Gt6 = gt6, Rule = rule });
        }
        string N(double v) => R(v).ToString(CultureInfo.InvariantCulture);
        // car
        F("Data_Car", "car", "Year", (long)D(car, "Year"), "copy", $"GENERIC_CAR.Year {D(car, "Year")}", "same");
        F("Data_Car", "car", "CurbWeight", R(mass / 100), "copy", $"CHASSIS.mass {mass} kg", "kg / 100");
        F("Data_Car", "car", "WeightDistribution", R(front), "estimate", $"CHASSIS.percentageF {D(ch, "percentageF")}", "/ 1000 (GT6 rounds: Supra 0.60, FM4 0.53)");
        F("Data_Car", "car", "NumGears", (long)gears.Count, "copy", $"GEAR: {gears.Count} forward gears", "forward gears");
        F("Data_Car", "car", "Displacement", R(disp, 0), "copy", $"ENGINE.displacement {S(en, "displacement")}", "cc (rotary \"654x2\" = 1308)");
        if (layout.DriveType > 0)
        {
            string g = $"DRIVETRAIN.DriveTypeName {drive}";
            F("Data_Car", "car", "DriveTypeID", (long)layout.DriveType, drive == "4WD" ? "estimate" : "copy", g, "List_DriveType: FF 1 FWD, FR/MR/RR 2 RWD, 4WD 3 AWD");
            F("Data_Car", "car", "EnginePlacementID", (long)layout.Placement, drive == "4WD" ? "estimate" : "copy", g, layout.Note.Length > 0 ? layout.Note : "List_EnginePlacement: FF/FR 1 front, MR 2 mid, RR 3 rear");
            if (powertrain >= 0) F("Data_Car", "car", "PowertrainID", (long)powertrain, drive == "4WD" ? "estimate" : "copy", g, "Powertrains row with this DrivetrainId and EnginePlacementId");
        }
        if (engLayout is { } el)
        {
            F("Data_Car", "car", "EngineConfigID", (long)el.Config, "copy", $"ENGINE.enginetype {engineType}", "List_EngineConfig: L 3 I, V 1, W 2, Boxer 5 F, Rotor 4 Rotary");
            F("Data_Car", "car", "CylinderID", $"Number={el.Count};Rotors={(el.Rotary ? 1 : 0)}", "lookup", $"ENGINE.enginetype {engineType}", "List_Cylinders row with this Number and Rotors (two 3-cylinder rows: take the first)");
        }
        long asp = aspiration switch { "NA" => 1, "TURBO" => 2, "SuperCharger" => 5, _ => 0 };
        if (asp > 0) F("Data_Car", "car", "AspirationTypeId", asp, asp == 1 ? "copy" : "estimate", $"ENGINE.aspiration {aspiration}", "List_Aspiration: NA 1, TURBO 2 Turbo (GT6 does not say twin: Supra is 3), SuperCharger 5 DSC");
        F("Data_Car", "car", "FrontTireWidthMM", (long)D(wheel, "FrontWidth"), "copy", $"WHEEL.FrontWidth {D(wheel, "FrontWidth")}", "mm");
        F("Data_Car", "car", "FrontTireAspect", (long)D(wheel, "FrontAspect"), "copy", $"WHEEL.FrontAspect {D(wheel, "FrontAspect")}", "%");
        F("Data_Car", "car", "FrontWheelDiameterIN", R(D(wheel, "FrontDiameter") / 10, 1), "copy", $"WHEEL.FrontDiameter {D(wheel, "FrontDiameter")}", "inch x 10");
        F("Data_Car", "car", "RearTireWidthMM", (long)D(wheel, "RearWidth"), "copy", $"WHEEL.RearWidth {D(wheel, "RearWidth")}", "mm");
        F("Data_Car", "car", "RearTireAspect", (long)D(wheel, "RearAspect"), "copy", $"WHEEL.RearAspect {D(wheel, "RearAspect")}", "%");
        F("Data_Car", "car", "RearWheelDiameterIN", R(D(wheel, "RearDiameter") / 10, 1), "copy", $"WHEEL.RearDiameter {D(wheel, "RearDiameter")}", "inch x 10");
        double area = 0.85 * width * height;
        F("Data_Car", "car", "BodyAeroLongitudinalDrag", R(0.5 * 1.225 * cd * area * 44.704 * 44.704 / 4.44822, 2), "estimate",
            $"CHASSIS.cd {D(ch, "cd")}, width {D(ch, "width")}, height {D(ch, "height")}", "lbf at 100 mph = 0.5 x 1.225 x Cd x (0.85 x W x H) x 44.704^2 / 4.44822 (0.83-1.03 x FM4 on 9 cars)");
        F("Data_Car", "car", "SteerMaxAngle", 42L, "default", $"STEER.steerlimit {D(st, "steerlimit")}", "FM4 42 on all 9 compared cars");
        // body weight
        F("List_UpgradeCarBodyWeight", "stock", "Mass", R(mass), "copy", $"CHASSIS.mass {mass} kg", "kg");
        F("List_UpgradeCarBodyWeight", "stock", "InitialMass", R(mass), "copy", $"CHASSIS.mass {mass} kg", "kg");
        F("List_UpgradeCarBodyWeight", "stock", "CMBackFront", R(front), "estimate", $"CHASSIS.percentageF {D(ch, "percentageF")}", "/ 1000");
        F("List_UpgradeCarBodyWeight", "stock", "CMHeight", R(cg), "estimate", $"CHASSIS.cgheight {D(ch, "cgheight")} mm", "m (+-9% on 9 cars)");
        // transmission
        F("List_UpgradeDrivetrainTransmission", "stock", "NumGears", (long)gears.Count + 1, "copy", $"GEAR: {gears.Count} forward gears", "forward gears + 1 (reverse)");
        F("List_UpgradeDrivetrainTransmission", "stock", "GearRatio0", R(-reverse), "copy", $"GEAR.GearReverse {D(ge, "GearReverse")}", "- / 1000");
        for (int i = 0; i < gears.Count; i++)
            F("List_UpgradeDrivetrainTransmission", "stock", $"GearRatio{i + 1}", R(gears[i]), "copy", $"GEAR.{gearCols[i]} {D(ge, gearCols[i])}", "/ 1000");
        F("List_UpgradeDrivetrainTransmission", "stock", "FinalDriveRatio", R(final), "copy", $"GEAR.finalgearDF {D(ge, "finalgearDF")}", "/ 1000");
        // engine
        F("List_UpgradeEngineDisplacement", "stock", "Disp", R(disp, 0), "copy", $"ENGINE.displacement {S(en, "displacement")}", "cc");
        F("List_UpgradeEngineCamshaft", "stock", "RedlineRPM", R(redline, 0), "copy", $"ENGINE.RedLine {D(en, "RedLine")}", "x 100 rpm");
        F("List_UpgradeEngineCamshaft", "stock", "StallRPM", R(idle, 0), "copy", $"ENGINE.idlerpm {idle}", "rpm (FM4 800 on almost every car)");
        double maxRpm = Math.Ceiling(redline * 1.25 / 100) * 100;
        int points = (int)(maxRpm / 100) + 1;
        F("List_UpgradeEngineCamshaft", "stock", "TorqueCurveMaxRPM", R(maxRpm, 0), "estimate", $"ENGINE.RedLine {D(en, "RedLine")}", "red line x 1.25 rounded up to 100 rpm (Supra 6800 -> 8500)");
        F("List_UpgradeEngineCamshaft", "stock", "NumRPMEntriesArray", (long)points, "estimate", "", "TorqueCurveMaxRPM / 100 + 1 (one point every 100 rpm)");
        F("List_UpgradeEngineFlywheel", "stock", "MomentInertia", R(D(dt, "iflywheel") / 1000), "estimate", $"DRIVETRAIN.iflywheel {D(dt, "iflywheel")}", "/ 1000 kg m^2 (spread 2x on 9 cars)");
        F("Data_Engine", "engine", "MomentInertia", R(D(dt, "iflywheel") / 1000), "estimate", $"DRIVETRAIN.iflywheel {D(dt, "iflywheel")}", "/ 1000 kg m^2 (Supra 0.138 vs 0.13)");
        if (forced)
        {
            F("Data_Engine", "engine", "StockBoost-bar", R(boostBar, 2), "estimate", $"TURBINEKIT.boost2 {D(tk, "boost2")}", "x 0.13 bar (exact on 2 cars)");
            F("List_UpgradeEngineTurbo*", "stock", "MaxScale", R(maxScale, 3), "estimate", $"TURBINEKIT.boost2 {D(tk, "boost2")}", "1 + 0.5 x boost bar (Supra 0.78 bar -> 1.39); the stock turbo / supercharger row");
        }
        // chassis: FM4 presets and defaults, GT6 value in the note
        foreach (var (ax, row) in new[] { ("F", "front"), ("R", "rear") })
        {
            double wr = WheelRate(ax);
            F("List_SpringDamperPhysics", row, "DefSpringRate", R(wr * 1.4, 2), "estimate", $"SUSPENSION.springrateDF{ax} {D(su, "springrateDF" + ax)}, leverratioDF{ax} {D(su, "leverratioDF" + ax)}", $"wheel rate {N(wr)} N/mm x 1.4 (FM4 = 1.1-1.7 x wheel rate)");
            F("List_SpringDamperPhysics", row, "DefDampenBumpRate", R(D(su, "dampF1BMIN" + ax) / 600, 3), "estimate", $"SUSPENSION.dampF1BMIN{ax} {D(su, "dampF1BMIN" + ax)}", "/ 600 (spread 2x)");
            F("List_SpringDamperPhysics", row, "DefDampenReboundRate", R(D(su, "dampF1RMIN" + ax) / 600, 3), "estimate", $"SUSPENSION.dampF1RMIN{ax} {D(su, "dampF1RMIN" + ax)}", "/ 600 (spread 2x)");
            F("List_SpringDamperPhysics", row, "StaticCamber", -0.5, "default", $"SUSPENSION.camberDF{ax} {D(su, "camberDF" + ax)}", "FM4 -0.5 on the 9 compared cars");
            F("List_SpringDamperPhysics", row, "StaticToe", 0.0, "default", $"SUSPENSION.toeDF{ax} {D(su, "toeDF" + ax)}", "FM4 0 on the 9 compared cars");
            F("List_SpringDamperPhysics", row, "Caster", 5.0, "default", $"SUSPENSION.caster {D(su, "caster")}", "FM4 5 on the 9 compared cars");
            F("List_AntiSwayPhysics", row, "DefSwaybarStiffness", R(D(su, "stabilizerMIN" + ax) * 0.06, 2), "estimate", $"SUSPENSION.stabilizerMIN{ax} {D(su, "stabilizerMIN" + ax)}", "x 0.06 (FM4 uses presets: front 7.44 / 9.24 / 11.36, rear 4.96 / 6.16 / 7.57)");
        }
        F("List_UpgradeBrakes", "stock", "FrontBrakeSize", R(Math.Round(brakeF / 330), 0), "estimate", $"BRAKE.BraketorqueF {brakeF}", "/ 330, whole inches");
        F("List_UpgradeBrakes", "stock", "RearBrakeSize", R(Math.Round(brakeF / 330), 0), "estimate", $"BRAKE.BraketorqueR {brakeR}", "= the front size (Supra: 12 / 12 in FM4; the GT6 rear torque only sets the balance)");
        F("List_UpgradeBrakes", "stock", "BrakeBiasSlider", 0.5, "default", $"BRAKE front share {N(brakeF / (brakeF + brakeR))}", "FM4 0.5 on every compared car");

        // ---- FM4 torque curve: GT6 curve / turbo MaxScale, one point every 100 rpm, normalized on its peak
        var fmCurve = new List<double>();
        if (curve.Count > 1)
        {
            double At(double rpm)
            {
                if (rpm <= curve[0].Rpm) return curve[0].Nm;
                for (int i = 1; i < curve.Count; i++)
                    if (rpm <= curve[i].Rpm)
                        return curve[i - 1].Nm + (curve[i].Nm - curve[i - 1].Nm) * (rpm - curve[i - 1].Rpm) / (curve[i].Rpm - curve[i - 1].Rpm);
                var (a, b) = (curve[^2], curve[^1]);
                return Math.Max(0, b.Nm + (b.Nm - a.Nm) * (rpm - b.Rpm) / (b.Rpm - a.Rpm));
            }
            for (int i = 0; i < points - 1; i++) fmCurve.Add(At(i * 100) / maxScale);
        }
        double torqueScale = fmCurve.Count > 0 ? fmCurve.Max() : double.NaN;

        // ---- write
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            void Num(string k, double v) { if (double.IsNaN(v)) w.WriteNull(k); else w.WriteNumber(k, R(v)); }
            w.WriteStartObject();
            w.WriteString("format", "gt-car-data");
            w.WriteNumber("version", Version);
            w.WriteStartObject("source");
            w.WriteString("game", "GT6");
            w.WriteString("database", dbName);
            w.WriteString("label", label);
            w.WriteNumber("genericCarId", (long)D(car, "ID"));
            w.WriteNumber("modelCode", (long)D(car, "ModelCode"));
            w.WriteString("exported", DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            w.WriteEndObject();

            w.WriteStartObject("car");
            w.WriteString("name", S(name, "Name"));
            w.WriteString("shortName", S(name, "ShortName"));
            w.WriteString("maker", S(view, "Tuner"));
            w.WriteString("country", S(view, "CountryFlag"));
            w.WriteNumber("year", (long)D(car, "Year"));
            w.WriteString("drive", drive);
            w.WriteEndObject();

            w.WriteStartObject("gt6");
            w.WriteStartObject("body");
            Num("mass_kg", mass); Num("frontWeight", front); Num("cgHeight_m", cg);
            Num("length_m", D(ch, "length") / 1000); Num("width_m", width); Num("height_m", height);
            Num("wheelbase_m", D(ch, "wheelbase") / 1000); Num("trackFront_m", D(ch, "treadF") / 1000); Num("trackRear_m", D(ch, "treadR") / 1000);
            Num("cd", cd); Num("clFront", D(ch, "clDFF") / 100); Num("clRear", D(ch, "clDFR") / 100);
            w.WriteEndObject();
            w.WriteStartObject("engine");
            w.WriteString("type", engineType); w.WriteString("cam", S(en, "cam")); w.WriteString("aspiration", aspiration);
            Num("displacement_cc", disp); Num("idle_rpm", idle); Num("redline_rpm", redline); Num("revLimit_rpm", revLimit);
            Num("power_ps", D(en, "psvalue")); Num("powerRpm", D(en, "psrpm"));
            Num("peakTorque_Nm", peak.Nm); Num("peakTorqueRpm", peak.Rpm);
            w.WriteStartArray("torqueCurve");
            foreach (var (rpm, nm) in curve) { w.WriteStartObject(); w.WriteNumber("rpm", rpm); w.WriteNumber("Nm", R(nm, 1)); w.WriteEndObject(); }
            w.WriteEndArray();
            if (forced)
            {
                w.WriteStartObject("turbo");
                Num("boost1", D(tk, "boost1")); Num("boost2", D(tk, "boost2")); Num("peakRpm1", D(tk, "peakrpm1") * 100); Num("peakRpm2", D(tk, "peakrpm2") * 100);
                Num("response1", D(tk, "response1")); Num("response2", D(tk, "response2")); Num("wastegate", D(tk, "wastegate"));
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteStartObject("transmission");
            w.WriteStartArray("gears"); foreach (var g in gears) w.WriteNumberValue(R(g)); w.WriteEndArray();
            Num("reverse", reverse); Num("finalDrive", final);
            Num("flywheelInertia", D(dt, "iflywheel")); Num("wheelInertiaFront", D(dt, "iwheelF")); Num("wheelInertiaRear", D(dt, "iwheelR"));
            w.WriteEndObject();
            w.WriteStartObject("differential");
            foreach (var ax in new[] { "F", "R" })
            {
                w.WriteStartObject(ax == "F" ? "front" : "rear");
                Num("type", D(ls, "difftype" + ax)); Num("initial", D(ls, $"LSDparam{ax}DF")); Num("accel", D(ls, "LSDparam2" + ax)); Num("decel", D(ls, "LSDparam3" + ax));
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteStartObject("suspension");
            foreach (var ax in new[] { "F", "R" })
            {
                w.WriteStartObject(ax == "F" ? "front" : "rear");
                Num("spring_kgf_mm", D(su, "springrateDF" + ax) / 100); Num("leverRatio", D(su, "leverratioDF" + ax) / 1000); Num("wheelRate_N_mm", WheelRate(ax));
                Num("rideHeight_mm", D(su, "rideheightDF" + ax)); Num("camber", D(su, "camberDF" + ax)); Num("toe", D(su, "toeDF" + ax));
                Num("damperBump", D(su, "dampF1BMIN" + ax)); Num("damperRebound", D(su, "dampF1RMIN" + ax));
                Num("damperBump2", D(su, "dampF2BMIN" + ax)); Num("damperRebound2", D(su, "dampF2RMIN" + ax));
                Num("antiRoll", D(su, "stabilizerMIN" + ax)); Num("unsprungMass", D(su, "unsprungmass" + ax));
                w.WriteEndObject();
            }
            Num("caster", D(su, "caster"));
            w.WriteEndObject();
            w.WriteStartObject("brakes");
            Num("torqueFront", brakeF); Num("torqueRear", brakeR); Num("frontShare", brakeF / (brakeF + brakeR)); Num("tireMu", D(br, "tireMuForBrake"));
            w.WriteEndObject();
            w.WriteStartObject("steering"); Num("lock_deg", D(st, "steerlimit")); w.WriteEndObject();
            w.WriteStartObject("tyres");
            foreach (var ax in new[] { "Front", "Rear" })
            {
                w.WriteStartObject(ax.ToLowerInvariant());
                Num("width_mm", D(wheel, ax + "Width")); Num("aspect", D(wheel, ax + "Aspect")); Num("rim_in", D(wheel, ax + "Diameter") / 10);
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteEndObject();

            w.WriteStartObject("fm4");
            w.WriteStartArray("fields");
            foreach (var f in fields)
            {
                w.WriteStartObject();
                w.WriteString("table", f.Table); w.WriteString("row", f.Row); w.WriteString("column", f.Column);
                switch (f.Value)
                {
                    case long l: w.WriteNumber("value", l); break;
                    case double d: w.WriteNumber("value", d); break;
                    default: w.WriteString("value", f.Value.ToString()); break;
                }
                w.WriteString("kind", f.Kind); w.WriteString("gt6", f.Gt6); w.WriteString("rule", f.Rule);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartObject("torqueCurve");
            w.WriteString("kind", "estimate");
            w.WriteString("rule", $"GT6 curve / turbo MaxScale {R(maxScale, 3).ToString(CultureInfo.InvariantCulture)}, linear between the GT6 points, flat below the first, last slope above the last; v = Nm / TorqueScale");
            Num("TorqueScale", torqueScale);
            w.WriteNumber("NumTorqueValues", points);
            w.WriteNumber("maxRpm", maxRpm);
            w.WriteStartArray("v");
            foreach (var v in fmCurve) w.WriteNumberValue(Math.Round(v / torqueScale, 6));
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteStartArray("paints");
            int main = (int)D(car, "MainColor");
            for (int i = 0; i < paints.Count; i++)
            {
                var p = paints[i];
                long bgr = (long)D(p, "ColorChip");
                long rgb = (bgr & 0xFF) << 16 | (bgr & 0xFF00) | (bgr >> 16 & 0xFF);
                string pn = S(p, "NameEng");
                bool metal = pn.Contains("metal", StringComparison.OrdinalIgnoreCase) || pn.Contains("mica", StringComparison.OrdinalIgnoreCase) || pn.Contains("pearl", StringComparison.OrdinalIgnoreCase);
                w.WriteStartObject();
                w.WriteNumber("Sequence", i + 1);
                w.WriteString("name", pn);
                w.WriteNumber("RGB", rgb);
                w.WriteString("hex", $"#{rgb:X6}");
                w.WriteNumber("Metallic", metal ? 1 : 0);
                w.WriteString("metallicKind", "estimate");
                w.WriteBoolean("default", i == main);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return ms.ToArray();
    }
}
