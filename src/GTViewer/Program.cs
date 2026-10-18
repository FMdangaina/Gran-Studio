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
        Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
        return 0;
    }
}
