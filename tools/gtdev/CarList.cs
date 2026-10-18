using GTFormats.SpecDb;

/// <summary>car-list &lt;GT5_VOL or GT6_VOL&gt; &lt;out.tsv&gt;: the viewer's car list (CarCatalog) as a table, with coverage counts.</summary>
static class CarList
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("car-list <GT5_VOL or GT6_VOL> <out.tsv> | --thumb <txs3 file> <out.png>"); return 1; }
        if (args[0] == "--thumb")
        {
            var rgba = GTFormats.Txs3.Txs3Image.DecodeFile(File.ReadAllBytes(args[1]), out int w, out int h);
            if (rgba == null) { Console.WriteLine("does not decode"); return 1; }
            Png.Write(args[2], rgba, w, h);
            Console.WriteLine($"{w}x{h} -> {args[2]}");
            return 0;
        }
        if (args[0] == "--sheet")
        {
            // car-list --sheet <root> <code|parts>: the viewer's car sheet of one code, or the parts list counts
            using var db = CarCatalog.FindGt6SpecDb(args[1]) is { } f ? Gt6SpecDb.Open(f) : null;
            if (args[2] == "parts")
            {
                var parts = CarCatalog.ScanParts(args[1], CarCatalog.Scan(args[1]), db);
                foreach (var g in parts.GroupBy(p => p.Kind)) Console.WriteLine($"{g.Key}: {g.Count()}, {g.Count(p => p.Thumbnail != null)} with thumbnail, e.g. {string.Join(" | ", g.Take(3).Select(p => $"{p.Code} {p.Name} {p.Info}"))}");
                return 0;
            }
            if (args[2] == "coverage")
            {
                var cars = CarCatalog.Scan(args[1]);
                int sheets = cars.Count(e => db?.CarSheet(long.Parse(e.Code), e.Label.Length > 0 ? e.Label : null) != null);
                Console.WriteLine($"{cars.Count} cars, {sheets} with a GT6 database sheet");
                return 0;
            }
            foreach (var (k, v) in db?.CarSheet(long.Parse(args[2])) ?? [("-", "not in the GT6 database")]) Console.WriteLine($"{k,-9} {v}");
            return 0;
        }
        var list = CarCatalog.Scan(args[0], Console.WriteLine);
        using (var w = new StreamWriter(args[1]))
        {
            w.WriteLine("game\tcode\tname\tyear\tlabel\tsource\thq\trace\tinterior\twheels\tthumbnail");
            foreach (var e in list)
                w.WriteLine($"{e.Game}\t{e.Code}\t{e.Name}\t{e.Year}\t{e.Label}\t{e.NameSource}\t{e.HqBody != null}\t{e.RaceBody != null}\t{e.Interior != null}\t{e.Wheels.Count}\t{Path.GetFileName(e.Thumbnail)}");
        }
        Console.WriteLine($"{list.Count} cars: {list.Count(e => e.NameSource.EndsWith("specdb"))} named from a specdb ({string.Join(", ", list.Where(e => e.NameSource.EndsWith("specdb")).GroupBy(e => e.NameSource).Select(g => $"{g.Key} {g.Count()}"))}), {list.Count(e => e.NameSource == "model paths")} from model paths, " +
            $"{list.Count(e => e.Name.Length == 0)} without name, {list.Count(e => e.Thumbnail != null)} with thumbnail -> {args[1]}");
        return 0;
    }
}
