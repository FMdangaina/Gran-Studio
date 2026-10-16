// gtdev: console harness for surveys and checks over the extracted GT5/GT6 volumes.
// Usage: gtdev <command> [args]

if (args.Length == 0)
{
    Console.WriteLine("gtdev <command> [args]");
    Console.WriteLine("Commands: (none yet)");
    return 1;
}

Console.Error.WriteLine($"Unknown command: {args[0]}");
return 1;
