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
