using System.Drawing.Imaging;
using GTFormats.SpecDb;
using GTFormats.Txs3;

namespace GTViewer;

/// <summary>
/// "Open game folder": the models of an extracted GT5_VOL / GT6_VOL folder by name, docked under the 3D view.
/// Tabs Cars (CarCatalog), Wheels (each car's wheels, GT6 aftermarket wheels) and Wing parts (GT6 carparts); one search
/// box for all. The selected item shows its thumbnail and, for cars, the dealer sheet of the GT6 spec database (a GT5
/// car uses the GT6 row of the same model code when a GT6 folder sits next to it). Double-click opens the model.
/// </summary>
public sealed class CarListPanel : UserControl
{
    readonly Action<string, string> open;   // (file, title)
    readonly string root;
    readonly TextBox search = new() { Dock = DockStyle.Fill, PlaceholderText = "search name, code or label..." };
    readonly Label count = new() { AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(6, 0, 0, 0) };
    readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    readonly ListView carList = NewList(("Code", 75), ("Name", 330), ("Year", 45), ("Files", 170));
    readonly ListView wheelList = NewList(("Name", 300), ("Kind", 115), ("Code", 75), ("Size", 220));
    readonly ListView wingList = NewList(("Name", 260), ("Code", 80));
    readonly PictureBox thumb = new() { Dock = DockStyle.Top, Height = 170, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(41, 43, 48) };
    readonly TextBox info = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9), BackColor = SystemColors.Window };
    // the files of the selected item (body hq / race, interior, wheels...): selecting one opens it
    readonly ListView files = NewList(("Open", 135), ("File", 145));
    bool fillingFiles;
    List<CarEntry> cars = [];
    List<PartEntry> parts = [];
    Gt6SpecDb? specDb;
    bool specDbIsOwn;   // the game's own database (GT6), not the one of a GT6 folder next to a GT5 one
    int carSort = 1;

    public string Root => root;

    public CarListPanel(string root, Action<string, string> open)
    {
        this.root = root;
        this.open = open;
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 28, ColumnCount = 2 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(search, 0, 0);
        top.Controls.Add(count, 1, 0);
        AddTab("Cars", carList);
        AddTab("Wheels", wheelList);
        // right side: thumbnail and the list of files to open, then the car sheet in its own column
        var picture = new Panel { Dock = DockStyle.Left, Width = 330, Padding = new Padding(4, 0, 4, 0) };
        thumb.Dock = DockStyle.Top;
        thumb.Height = 130;
        files.ShowItemToolTips = true;
        picture.Controls.Add(files);
        picture.Controls.Add(thumb);
        files.SelectedIndexChanged += (_, _) =>
        {
            if (!fillingFiles && files.SelectedItems.Count > 0 && files.SelectedItems[0].Tag is (string file, Func<string> title)) open(file, title());
        };
        var right = new Panel { Dock = DockStyle.Right, Width = 700 };
        right.Controls.Add(info);
        right.Controls.Add(picture);
        Controls.Add(tabs);
        Controls.Add(right);
        Controls.Add(top);

        search.TextChanged += (_, _) => Fill();
        tabs.SelectedIndexChanged += (_, _) => { UpdateCount(); ShowSelected(); };
        foreach (var l in new[] { carList, wheelList, wingList })
        {
            l.SelectedIndexChanged += (_, _) => ShowSelected();
            l.DoubleClick += (_, _) => OpenDefault();
            l.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { OpenDefault(); e.Handled = true; } };
        }
        carList.ColumnClick += (_, e) => { carSort = e.Column; Fill(); };
        Disposed += (_, _) => { specDb?.Dispose(); thumb.Image?.Dispose(); };
    }

    static ListView NewList(params (string Name, int Width)[] columns)
    {
        var l = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
        foreach (var (n, w) in columns) l.Columns.Add(n, w);
        return l;
    }

    void AddTab(string text, ListView list)
    {
        var page = new TabPage(text);
        page.Controls.Add(list);
        tabs.TabPages.Add(page);
    }

    /// <summary>Reads the car list, the parts and the spec database in the background.</summary>
    public async Task LoadAsync()
    {
        count.Text = "reading the car list...";
        UseWaitCursor = true;
        try
        {
            (cars, parts, specDb, specDbIsOwn) = await Task.Run(() =>
            {
                var c = CarCatalog.Scan(root);
                Gt6SpecDb? db = null;
                bool own = false;
                try
                {
                    if (CarCatalog.FindGt6SpecDb(root) is { } f) { db = Gt6SpecDb.Open(f); own = c.Count > 0 && c[0].Game == "GT6"; }
                }
                catch { db = null; }
                return (c, CarCatalog.ScanParts(root, c, db), db, own);
            });
        }
        finally { UseWaitCursor = false; }
        if (parts.Any(p => p.Kind == "wing part")) AddTab("Wing parts", wingList);
        Fill();
        search.Focus();
    }

    /// <summary>Shows a tab with a search and selects its first item (used by --ui-shot).</summary>
    public void Select(int tab, string text)
    {
        tabs.SelectedIndex = Math.Min(tab, tabs.TabCount - 1);
        search.Text = text;
        if (Current.Items.Count > 0) Current.Items[0].Selected = true;
    }

    ListView Current =>tabs.SelectedIndex switch { 1 => wheelList, 2 => wingList, _ => carList };

    void Fill()
    {
        string q = search.Text.Trim();
        bool Match(params string[] fields) => q.Length == 0 || fields.Any(f => f.Contains(q, StringComparison.OrdinalIgnoreCase));

        var shown = cars.Where(e => Match(e.Name, e.Code, e.Label));
        shown = carSort switch
        {
            0 => shown.OrderBy(e => e.Code),
            2 => shown.OrderBy(e => e.Year).ThenBy(e => e.Name),
            _ => shown.OrderBy(e => e.Name.Length == 0 ? 1 : 0).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
        };
        carList.BeginUpdate();
        carList.Items.Clear();
        foreach (var e in shown)
        {
            string files = string.Join(" ", new[] { e.HqBody != null ? "hq" : null, e.RaceBody != null ? "race" : null, e.Interior != null ? "interior" : null,
                e.Wheels.Count > 0 ? $"wheel{(e.Wheels.Count > 1 ? "s " + e.Wheels.Count : "")}" : null }.Where(x => x != null));
            bool fromDb = e.NameSource.EndsWith("specdb");
            string name = e.Name.Length == 0 ? "(no name found)" : fromDb ? e.Name : e.Name + "  (guessed)";
            carList.Items.Add(new ListViewItem([e.Code, name, e.Year > 0 ? e.Year.ToString() : "", files]) { Tag = e, ForeColor = fromDb ? SystemColors.WindowText : SystemColors.GrayText });
        }
        carList.EndUpdate();

        FillParts(wheelList, parts.Where(p => p.Kind != "wing part" && Match(p.Name, p.Code, p.Kind))
            .OrderBy(p => p.Kind == "aftermarket wheel" ? 0 : 1).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            p => [p.Name, p.Kind, p.Code, p.Info]);
        FillParts(wingList, parts.Where(p => p.Kind == "wing part" && Match(p.Name, p.Code)), p => [p.Name, p.Code]);
        UpdateCount();
    }

    static void FillParts(ListView list, IEnumerable<PartEntry> items, Func<PartEntry, string[]> columns)
    {
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var p in items) list.Items.Add(new ListViewItem(columns(p)) { Tag = p });
        list.EndUpdate();
    }

    void UpdateCount()
    {
        int total = tabs.SelectedIndex switch { 1 => parts.Count(p => p.Kind != "wing part"), 2 => parts.Count(p => p.Kind == "wing part"), _ => cars.Count };
        count.Text = $"{Current.Items.Count} / {total} {tabs.SelectedTab?.Text.ToLowerInvariant()}";
    }

    object? Selected => Current.SelectedItems.Count > 0 ? Current.SelectedItems[0].Tag : null;

    void ShowSelected()
    {
        fillingFiles = true;
        files.BeginUpdate();
        files.Items.Clear();
        thumb.Image?.Dispose();
        thumb.Image = null;
        info.Text = "";
        switch (Selected)
        {
            case CarEntry e: ShowCar(e); break;
            case PartEntry p: ShowPart(p); break;
        }
        files.EndUpdate();
        fillingFiles = false;
    }

    void ShowCar(CarEntry e)
    {
        var text = new List<string> { e.Name.Length > 0 ? e.Name : "(no name)", $"{e.Game} code {e.Code}" };
        if (!e.NameSource.EndsWith("specdb") && e.NameSource.Length > 0) text.Add("name guessed from the model's texture paths");
        else if (e.NameSource == "GT5 specdb") text.Add("name from the GT5 database");
        List<(string Key, string Value)>? sheet = null;
        try { if (specDb != null && long.TryParse(e.Code, out long code)) sheet = specDb.CarSheet(code, e.Label.Length > 0 ? e.Label : null); }
        catch (Exception ex) { text.Add("spec database error: " + ex.Message); }
        text.Add("");
        if (sheet != null)
        {
            if (!specDbIsOwn) text.Add("Data from the GT6 database (same model code or label):");
            text.AddRange(sheet.Select(s => $"{s.Key,-9} {s.Value}"));
        }
        else text.Add(specDb == null ? "No GT6 spec database found (specdb\\GT6\\DB0106.dat)." : "This model code is not in the GT6 spec database.");
        info.Text = string.Join(Environment.NewLine, text);
        if (e.Thumbnail != null) thumb.Image = LoadThumbnail(e.Thumbnail);

        AddFile("body (hq)", e.HqBody, () => Title(e, "hq body"));
        AddFile("body (race)", e.RaceBody, () => Title(e, "race body"));
        AddFile("interior", e.Interior, () => Title(e, "interior"));
        foreach (var w in e.Wheels)
        {
            string what = e.Game == "GT5" ? $"wheel {Path.GetFileName(w)}" : $"{Path.GetFileName(Path.GetDirectoryName(w))} wheel";
            AddFile(what, w, () => Title(e, what));
        }
    }

    void ShowPart(PartEntry p)
    {
        var text = new List<string> { p.Name, p.Kind + (p.Car != null ? $", {p.Car.Game} car {p.Car.Code}" : $", carparts {p.Code}") };
        if (p.Info.Length > 0) text.Add(p.Info);
        text.Add("");
        text.Add(p.File);
        if (p.RaceFile != null) text.Add(p.RaceFile);
        info.Text = string.Join(Environment.NewLine, text);
        if (p.Thumbnail != null) thumb.Image = LoadThumbnail(p.Thumbnail);
        AddFile(p.RaceFile != null ? $"{p.Kind} (hq)" : p.Kind, p.File, () => $"{p.Name} - {p.Code} {p.Kind}");
        AddFile($"{p.Kind} (race)", p.RaceFile, () => $"{p.Name} - {p.Code} {p.Kind} race");
        if (p.Car is { } c) AddFile("the car body", c.HqBody ?? c.RaceBody, () => Title(c, c.HqBody != null ? "hq body" : "race body"));
    }

    /// <summary>One row of the file list: what it is, and the file path under the game folder.</summary>
    void AddFile(string text, string? file, Func<string> title)
    {
        if (file == null) return;
        string rel = file.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? file[root.Length..].TrimStart('\\', '/') : file;
        files.Items.Add(new ListViewItem([text, rel]) { Tag = (file, title), ToolTipText = file });
    }

    static string Title(CarEntry e, string what) => $"{(e.Name.Length > 0 ? e.Name : e.Code)} - {e.Game} {e.Code} {what}";

    void OpenDefault()
    {
        switch (Selected)
        {
            case CarEntry e when (e.HqBody ?? e.RaceBody) is { } f: open(f, Title(e, e.HqBody != null ? "hq body" : "race body")); break;
            case PartEntry p: open(p.File, $"{p.Name} - {p.Code} {p.Kind}"); break;
        }
    }

    /// <summary>Thumbnails are stand-alone TXS3 files: the first image, level 0.</summary>
    static Bitmap? LoadThumbnail(string path)
    {
        try
        {
            var rgba = Txs3Image.DecodeFile(File.ReadAllBytes(path), out int w, out int h);
            if (rgba == null) return null;
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var bgra = new byte[rgba.Length];
            for (int i = 0; i + 3 < rgba.Length; i += 4) { bgra[i] = rgba[i + 2]; bgra[i + 1] = rgba[i + 1]; bgra[i + 2] = rgba[i]; bgra[i + 3] = rgba[i + 3]; }
            for (int y = 0; y < h; y++) System.Runtime.InteropServices.Marshal.Copy(bgra, y * w * 4, data.Scan0 + y * data.Stride, w * 4);
            bmp.UnlockBits(data);
            return bmp;
        }
        catch { return null; }
    }
}
