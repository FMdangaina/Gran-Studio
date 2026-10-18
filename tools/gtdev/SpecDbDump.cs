using System.Buffers.Binary;
using GTFormats.SpecDb;

/// <summary>
/// specdb-dump &lt;specdb folder&gt; &lt;table&gt; [rows] [stringDb]: rows of a GT5 GTDB table as id, label and hex, with
/// every 4-byte column also read as a string index (UnistrDB.sdb, or the given string database).
/// </summary>
static class SpecDbDump
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("specdb-dump <specdb folder> <table> [rows] [stringDb]"); return 1; }
        var t = new GtDbTable(args[0], args[1]);
        int rows = args.Length > 2 ? int.Parse(args[2]) : 10;
        var strings = new GtStringDb(Path.Combine(args[0], args.Length > 3 ? args[3] : "UnistrDB.sdb"));
        Console.WriteLine($"{args[1]}: {t.RowCount} rows of {t.RowSize} bytes, {(t.BigEndian ? "BE" : "LE")}, {t.Labels.Count} labels");
        for (int i = 0; i < Math.Min(rows, t.RowCount); i++)
        {
            var r = t.Row(i);
            int id = t.RowId(i);
            var words = Enumerable.Range(0, r.Length / 4).Select(k => t.BigEndian ? BinaryPrimitives.ReadInt32BigEndian(r.AsSpan(k * 4)) : BinaryPrimitives.ReadInt32LittleEndian(r.AsSpan(k * 4)));
            Console.WriteLine($"{id,6} {t.Labels.GetValueOrDefault(id, "?"),-28} {Convert.ToHexString(r)}");
            Console.WriteLine($"       strings: {string.Join(" | ", words.Select(w => strings[w]))}");
        }
        return 0;
    }
}
