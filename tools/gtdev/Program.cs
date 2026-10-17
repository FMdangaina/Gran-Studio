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
    Console.WriteLine("  mdl3-obj <file> <out.obj>   export the plain shapes as OBJ + MTL + PNG textures, shape stats in <out>.txt");
    Console.WriteLine("  mdl3-packed <files.tsv> <groupRegex> <out.txt>   decode and check every GT5 packed shape");
    Console.WriteLine("  mdl3-mat <files.tsv> <groupRegex> <out.txt>   column stats of the material, SHDS and TXS3 blocks");
    Console.WriteLine("  mdl3-coverage <files.tsv> <groupRegex> <out.txt> | <file> <out.txt>   byte coverage + table checks");
    Console.WriteLine("  txs3-survey <files.tsv> <groupRegex> <out.txt>   TXS3 image formats, mipmaps, computed vs record size");
    Console.WriteLine("  txs3-png <file> <outDir> | <files.tsv> <groupRegex> <out.txt>   decode TXS3 images to PNG / decode check");
    Console.WriteLine("  body-s <files.tsv> <groupRegex> <out.txt> | <body> <out.txt>   GT6 body_s map: streams, bundles, image pixels");
    return 1;
}

return args[0] switch
{
    "mdl3-survey" => Mdl3Survey.Run(args[1..]),
    "mdl3-records" => Mdl3Records.Run(args[1..]),
    "mdl3-models" => Mdl3Models.Run(args[1..]),
    "mdl3-fvf" => Mdl3FvfCheck.Run(args[1..]),
    "mdl3-obj" => Mdl3Obj.Run(args[1..]),
    "mdl3-packed" => Mdl3PackedCheck.Run(args[1..]),
    "mdl3-mat" => Mdl3MatCheck.Run(args[1..]),
    "mdl3-uv" => Mdl3UvCheck.Run(args[1..]),
    "mdl3-matcheck" => Mdl3MatLayout.Run(args[1..]),
    "mdl3-coverage" => Mdl3Coverage.Run(args[1..]),
    "txs3-survey" => Txs3Survey.Run(args[1..]),
    "txs3-png" => Txs3Png.Run(args[1..]),
    "body-s" => BodySCheck.Run(args[1..]),
    _ => Unknown(args[0]),
};

static int Unknown(string cmd)
{
    Console.Error.WriteLine($"Unknown command: {cmd}");
    return 1;
}
