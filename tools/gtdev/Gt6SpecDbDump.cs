using GTFormats.SpecDb;
using Microsoft.Data.Sqlite;

/// <summary>
/// gt6-specdb &lt;DB0106.dat&gt; &lt;out.sqlite&gt; [report.txt]: decrypts a GT6 SQLite database (Salsa20, spec database
/// key; menudb.dat with --menu) into a plain SQLite file, then lists every table with its columns, row count and the
/// first rows.
/// </summary>
static class Gt6SpecDbDump
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("gt6-specdb <DB0106.dat|menudb.dat> <out.sqlite> [report.txt] [--menu] [--rows N]"); return 1; }
        bool menu = args.Contains("--menu");
        int rows = args.SkipWhile(a => a != "--rows").Skip(1).Select(int.Parse).FirstOrDefault(3);
        var data = File.ReadAllBytes(args[0]);
        Salsa20.Crypt(data, menu ? Salsa20.Gt6MenuDbKey : Salsa20.Gt6SpecDbKey);
        string magic = System.Text.Encoding.ASCII.GetString(data, 0, 15);
        Console.WriteLine($"header after decrypt: '{magic}'");
        if (magic != "SQLite format 3") { Console.Error.WriteLine("not a SQLite file: wrong key?"); return 2; }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
        File.WriteAllBytes(args[1], data);

        using var w = args.Length > 2 && !args[2].StartsWith("--") ? new StreamWriter(args[2]) : new StreamWriter(Console.OpenStandardOutput());
        using var db = new SqliteConnection($"Data Source={args[1]};Mode=ReadOnly");
        db.Open();
        var tables = new List<string>();
        using (var c = db.CreateCommand())
        {
            c.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
            using var r = c.ExecuteReader();
            while (r.Read()) tables.Add(r.GetString(0));
        }
        w.WriteLine($"{args[0]}: {tables.Count} tables");
        foreach (var t in tables)
        {
            using var c = db.CreateCommand();
            c.CommandText = $"SELECT COUNT(*) FROM \"{t}\"";
            long n = (long)c.ExecuteScalar()!;
            c.CommandText = $"SELECT * FROM \"{t}\" LIMIT {rows}";
            using var r = c.ExecuteReader();
            var cols = Enumerable.Range(0, r.FieldCount).Select(i => r.GetName(i)).ToList();
            w.WriteLine();
            w.WriteLine($"== {t}: {n} rows, {cols.Count} columns");
            w.WriteLine("   " + string.Join(", ", cols));
            while (r.Read())
                w.WriteLine("   > " + string.Join(" | ", Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "null" : r.GetValue(i) is byte[] b ? $"blob[{b.Length}]" : r.GetValue(i).ToString())));
        }
        return 0;
    }

    /// <summary>sql &lt;plain.sqlite | DB0106.dat&gt; &lt;query&gt;: one query, tab-separated rows with a header.</summary>
    public static int Query(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("sql <plain.sqlite | DB0106.dat> <query>"); return 1; }
        using var spec = args[0].EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ? Gt6SpecDb.Open(args[0]) : null;
        using var own = spec == null ? new SqliteConnection($"Data Source={args[0]};Mode=ReadOnly") : null;
        own?.Open();
        using var c = (spec?.Connection ?? own!).CreateCommand();
        c.CommandText = string.Join(" ", args[1..]);
        using var r = c.ExecuteReader();
        Console.WriteLine(string.Join("\t", Enumerable.Range(0, r.FieldCount).Select(r.GetName)));
        while (r.Read())
            Console.WriteLine(string.Join("\t", Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "null" : Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture))));
        return 0;
    }
}
