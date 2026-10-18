using System.Drawing.Imaging;
using GTFormats;
using GTFormats.SpecDb;
using GTFormats.Txs3;

namespace GTViewer;

/// <summary>
/// "Open game folder": the cars of an extracted GT5_VOL / GT6_VOL folder by name (CarCatalog), with search, the
/// thumbnail of the selected car and buttons for its files. Stays open while browsing; double-click opens the body.
/// </summary>
public sealed class CarListForm : Form
{
    readonly Action<string, string> open;   // (file, title)
    readonly TextBox search = new() { Dock = DockStyle.Fill, PlaceholderText = "search name, code or label..." };
    readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
    readonly PictureBox thumb = new() { Dock = DockStyle.Top, Height = 200, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(41, 43, 48) };
    readonly Label info = new() { Dock = DockStyle.Top, Height = 70, Padding = new Padding(4) };
    readonly FlowLayoutPanel buttons = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
    readonly Label count = new() { AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(6, 0, 0, 0) };
    List<CarEntry> cars = [];

    public CarListForm(string root, Action<string, string> open)
    {
        this.open = open;
        Text = $"Cars - {root}";
        ClientSize = new Size(980, 640);
        StartPosition = FormStartPosition.CenterParent;
        list.Columns.Add("Code", 80);
        list.Columns.Add("Name", 420);
        list.Columns.Add("Year", 50);
        list.Columns.Add("Files", 150);

        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 30, ColumnCount = 2 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(search, 0, 0);
        top.Controls.Add(count, 1, 0);
        var right = new Panel { Dock = DockStyle.Right, Width = 300 };
        right.Controls.Add(buttons);
        right.Controls.Add(info);
        right.Controls.Add(thumb);
        Controls.Add(list);
        Controls.Add(right);
        Controls.Add(top);

        search.TextChanged += (_, _) => Fill();
        list.SelectedIndexChanged += (_, _) => ShowSelected();
        list.DoubleClick += (_, _) => { if (Selected is { } e && (e.HqBody ?? e.RaceBody) is { } f) Open(e, f, e.HqBody != null ? "hq body" : "race body"); };
        list.ColumnClick += (_, e) => { sortColumn = e.Column; Fill(); };
        Shown += async (_, _) =>
        {
            count.Text = "reading the car list...";
            UseWaitCursor = true;
            try { cars = await Task.Run(() => CarCatalog.Scan(root)); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Cars", MessageBoxButtons.OK, MessageBoxIcon.Warning); Close(); return; }
            finally { UseWaitCursor = false; }
            Fill();
            search.Focus();
        };
    }

    int sortColumn = 1;

    CarEntry? Selected => list.SelectedItems.Count > 0 ? list.SelectedItems[0].Tag as CarEntry : null;

    void Fill()
    {
        string q = search.Text.Trim();
        var shown = cars.Where(e => q.Length == 0 || e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || e.Code.Contains(q) || e.Label.Contains(q, StringComparison.OrdinalIgnoreCase));
        shown = sortColumn switch
        {
            0 => shown.OrderBy(e => e.Code),
            2 => shown.OrderBy(e => e.Year).ThenBy(e => e.Name),
            _ => shown.OrderBy(e => e.Name.Length == 0 ? 1 : 0).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
        };
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var e in shown)
        {
            string files = string.Join(" ", new[] { e.HqBody != null ? "hq" : null, e.RaceBody != null ? "race" : null, e.Interior != null ? "interior" : null,
                e.Wheels.Count > 0 ? $"wheel{(e.Wheels.Count > 1 ? "s " + e.Wheels.Count : "")}" : null }.Where(x => x != null));
            string name = e.Name.Length == 0 ? "(no name found)" : e.NameSource == "specdb" ? e.Name : e.Name + "  (guessed)";
            list.Items.Add(new ListViewItem([e.Code, name, e.Year > 0 ? e.Year.ToString() : "", files]) { Tag = e, ForeColor = e.NameSource == "specdb" ? SystemColors.WindowText : SystemColors.GrayText });
        }
        list.EndUpdate();
        count.Text = $"{list.Items.Count} / {cars.Count} cars";
    }

    void ShowSelected()
    {
        var e = Selected;
        buttons.Controls.Clear();
        thumb.Image?.Dispose();
        thumb.Image = null;
        if (e == null) { info.Text = ""; return; }
        info.Text = $"{(e.Name.Length > 0 ? e.Name : "(no name)")}\n{e.Game} code {e.Code}{(e.Label.Length > 0 ? $", label {e.Label}" : "")}\n" +
                    (e.NameSource == "specdb" ? "name from the game database" : e.NameSource.Length > 0 ? "name guessed from the model's texture paths" : "");
        if (e.Thumbnail != null) thumb.Image = LoadThumbnail(e.Thumbnail);
        void Add(string text, string? file)
        {
            if (file == null) return;
            var b = new Button { Text = text, Width = 270, Height = 30 };
            b.Click += (_, _) => Open(e, file, text);
            buttons.Controls.Add(b);
        }
        Add("Open body (hq)", e.HqBody);
        Add("Open body (race)", e.RaceBody);
        Add("Open interior", e.Interior);
        foreach (var w in e.Wheels) Add($"Open wheel {Path.GetFileName(Path.GetDirectoryName(w))}\\{Path.GetFileName(w)}", w);
    }

    void Open(CarEntry e, string file, string what) =>
        open(file, $"{(e.Name.Length > 0 ? e.Name : e.Code)} - {e.Game} {e.Code} {what}");

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
