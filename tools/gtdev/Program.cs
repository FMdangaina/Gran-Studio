// gtdev: console harness for surveys and checks over the extracted GT5/GT6 volumes.
// Usage: gtdev <command> [args]

if (args.Length == 0)
{
    Console.WriteLine("gtdev <command> [args]");
    Console.WriteLine("Commands:");
    Console.WriteLine("  mdl3-survey <out.txt> <root>...   header field/table check over every MDL3 (also writes files.tsv)");
    Console.WriteLine("  mdl3-records <files.tsv> <groupRegex> <table> <stride hex> <out.txt>   column stats of a table");
    Console.WriteLine("  mdl3-models <files.tsv> <groupRegex> <out.txt> [dumpPath]   check model command streams / dump one file");
    Console.WriteLine("  mdl3-fvf <files.tsv> <groupRegex> <out.txt>   check vertex formats, decode plain positions vs bbox");
    Console.WriteLine("  mdl3-obj <file> <out.obj>   export the plain shapes as OBJ (research view) + index stats in <out>.txt");
    return 1;
}

return args[0] switch
{
    "mdl3-survey" => Mdl3Survey.Run(args[1..]),
    "mdl3-records" => Mdl3Records.Run(args[1..]),
    "mdl3-models" => Mdl3Models.Run(args[1..]),
    "mdl3-fvf" => Mdl3FvfCheck.Run(args[1..]),
    "mdl3-obj" => Mdl3Obj.Run(args[1..]),
    _ => Unknown(args[0]),
};

static int Unknown(string cmd)
{
    Console.Error.WriteLine($"Unknown command: {cmd}");
    return 1;
}
