// gtdev: console harness for surveys and checks over the extracted GT5/GT6 volumes.
// Usage: gtdev <command> [args]

if (args.Length == 0)
{
    Console.WriteLine("gtdev <command> [args]");
    Console.WriteLine("Commands:");
    Console.WriteLine("  mdl3-survey <out.txt> <root>...   header field/table check over every MDL3");
    return 1;
}

return args[0] switch
{
    "mdl3-survey" => Mdl3Survey.Run(args[1..]),
    _ => Unknown(args[0]),
};

static int Unknown(string cmd)
{
    Console.Error.WriteLine($"Unknown command: {cmd}");
    return 1;
}
