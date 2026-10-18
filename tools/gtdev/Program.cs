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
    Console.WriteLine("  mdl3-obj <file> <out.obj> [--all-models]   shapes as OBJ (readable names, one file per LOD) + MTL + PNG, stats in <out>.txt");
    Console.WriteLine("  mdl3-gltf <file> <out.gltf> [--all-models]   the same as glTF with the node hierarchy, one file per LOD");
    Console.WriteLine("  mdl3-tree <file> [out.txt]   bones, VM registers and model command streams with shape names");
    Console.WriteLine("  mdl3-normals <files.tsv> <groupRegex> <out.txt>   vertex normals vs triangle winding, per LOD level");
    Console.WriteLine("  mdl3-samplers <files.tsv> <groupRegex> <out.txt>   texture samplers of the materials: in file / external, names");
    Console.WriteLine("  pmsh-dump <file> [out.txt] | pmsh-probe <file> <mesh> | pmsh-survey / pmsh-check <files.tsv> <groupRegex> <out.txt>   GT6 PMSH research and check");
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
    "mdl3-gltf" => Mdl3Gltf.Run(args[1..]),
    "mdl3-tree" => Mdl3Tree.Run(args[1..]),
    "mdl3-normals" => Mdl3Normals.Run(args[1..]),
    "mdl3-samplers" => Mdl3Samplers.Run(args[1..]),
    "pmsh-dump" => PmshDump.Run(args[1..]),
    "pmsh-probe" => PmshProbe.Run(args[1..]),
    "pmsh-survey" => PmshSurvey.Run(args[1..]),
    "pmsh-check" => PmshCheck.Run(args[1..]),
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
