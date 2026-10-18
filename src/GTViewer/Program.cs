namespace GTViewer;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length >= 3 && args[0] == "--shot") return Shot.Run(args);
        ApplicationConfiguration.Initialize();
        if (args.Length >= 5 && args[0] == "--ui-shot")
        {
            // --ui-shot <game folder> <tab 0-2> <search> <out.png>: the window off screen, no mouse, no screen capture
            var form = new MainForm(null) { NoAutoLoad = true, StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000), ShowInTaskbar = false };
            form.Shown += async (_, _) => { await form.ShotUi(args[1], int.Parse(args[2]), args[3], args[4]); form.Close(); };
            Application.Run(form);
            return 0;
        }
        if (args.Length >= 3 && args[0] == "--tex-shot")
        {
            // --tex-shot <file> <out.png> [image] [channel 0-5]: the texture browser off screen, image selected
            var form = new TextureForm { StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000), ShowInTaskbar = false };
            int image = args.Length > 3 ? int.Parse(args[3]) : 0, mode = args.Length > 4 ? int.Parse(args[4]) : 0;
            form.ThumbnailsDone += () => { form.Shot(args[2], image, mode); form.Close(); };
            form.Shown += (_, _) => { if (!form.OpenFile(args[1])) form.Close(); };
            Application.Run(form);
            return 0;
        }
        Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
        return 0;
    }
}
