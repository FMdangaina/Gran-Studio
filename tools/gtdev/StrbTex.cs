using GTFormats.Mdl3;

/// <summary>
/// strb-tex &lt;file&gt; &lt;outDir&gt;: the TXS3 textures inside a file that is not a model (GT5/GT6 `meter` STRB): every "TXS3"
/// found, cut up to the next one, decoded as a stand-alone TXS3 to outDir\NN_image.png. Research for ROADMAP 12c.
/// </summary>
static class StrbTex
{
    public static int Run(string[] args)
    {
        var data = File.ReadAllBytes(args[0]);
        Directory.CreateDirectory(args[1]);
        var starts = new List<int>();
        for (int i = 0; i + 4 <= data.Length; i += 4)   // TXS3 blocks are aligned
            if (data[i] == 'T' && data[i + 1] == 'X' && data[i + 2] == 'S' && data[i + 3] == '3') starts.Add(i);
        for (int k = 0; k < starts.Count; k++)
        {
            int end = k + 1 < starts.Count ? starts[k + 1] : data.Length;
            var set = new Mdl3TextureSet(data[starts[k]..end]);
            for (int i = 0; i < set.Entries.Count; i++)
            {
                var e = set.Entries[i];
                var rgba = set.Decode(i);
                Console.WriteLine($"{k,2} @{starts[k]:X6} image {i}: {e.Record.Width}x{e.Record.Height} {e.FormatText} {(rgba == null ? "NOT DECODED" : "")}");
                if (rgba != null) Png.Write(Path.Combine(args[1], $"{k:D2}_{i}.png"), rgba, e.Record.Width, rgba.Length / 4 / e.Record.Width);
            }
        }
        return 0;
    }
}
