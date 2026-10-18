using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using GTFormats.Mdl3;

namespace GTViewer;

/// <summary>
/// Texture browser (ROADMAP 11): every TXS3 image of a model (GT6 bodies: pixels from body_s) or of a stand-alone TXS3
/// file (thumbnails, decken) as thumbnails grouped by the sampler that uses them; the selected one big (wheel zoom at the
/// cursor, drag to pan, double-click = fit), channels RGBA / RGB / alpha / R / G / B, faces and slices of cubes and 3D
/// textures; names, format, users and where the pixels are; save one or all as PNG (level 0, RGBA).
/// </summary>
public sealed class TextureForm : Form
{
    readonly ImageList thumbs = new() { ImageSize = new Size(96, 96), ColorDepth = ColorDepth.Depth32Bit };
    readonly ListView list;
    readonly ImagePanel picture = new() { Dock = DockStyle.Fill };
    readonly TextBox info = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9) };
    readonly ToolStripComboBox channels = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    readonly ToolStripComboBox slices = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    readonly ToolStripTextBox filter = new() { Width = 180, ToolTipText = "Filter by name, material or sampler" };
    readonly ToolStripButton upright = new("Upright") { CheckOnClick = true, Checked = true, ToolTipText = "GT stores the rows bottom up (v = 0 at the bottom): show and save them upright" };
    readonly ToolStripStatusLabel status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    Mdl3TextureSet? set;
    string source = "";
    CancellationTokenSource? job;

    public TextureForm(Mdl3TextureSet? textures = null, string? title = null)
    {
        Text = "GTViewer - Textures";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        ClientSize = new Size(1300, 860);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        list = new ListView { Dock = DockStyle.Fill, View = View.LargeIcon, LargeImageList = thumbs, MultiSelect = false, HideSelection = false, ShowGroups = true };

        var menu = new MenuStrip();
        var fileMenu = new ToolStripMenuItem("&File");
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("&Open model or texture file...", null, (_, _) => OpenDialog()) { ShortcutKeys = Keys.Control | Keys.O });
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("&Save selected as PNG / TGA...", null, (_, _) => SaveSelected()) { ShortcutKeys = Keys.Control | Keys.S });
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("Save &all as PNG...", null, (_, _) => SaveAll(".png")) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.S });
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("Save all as &TGA...", null, (_, _) => SaveAll(".tga")));
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("&Close", null, (_, _) => Close()));
        menu.Items.Add(fileMenu);

        channels.Items.AddRange(["RGBA", "RGB", "Alpha", "Red", "Green", "Blue"]);
        channels.SelectedIndex = 0;
        channels.SelectedIndexChanged += (_, _) => ShowSelected(keepView: true);
        slices.SelectedIndexChanged += (_, _) => ShowSelected(keepView: true);
        filter.TextChanged += (_, _) => FillList();
        var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        tools.Items.Add(new ToolStripLabel("Channels"));
        tools.Items.Add(channels);
        tools.Items.Add(new ToolStripLabel("Face / slice"));
        tools.Items.Add(slices);
        tools.Items.Add(upright);
        upright.CheckedChanged += (_, _) => { ShowSelected(keepView: true); StartThumbnails(); };
        tools.Items.Add(new ToolStripButton("Fit", null, (_, _) => picture.Fit()));
        tools.Items.Add(new ToolStripButton("1:1", null, (_, _) => picture.SetZoom(1)));
        tools.Items.Add(new ToolStripSeparator());
        tools.Items.Add(new ToolStripLabel("Filter"));
        tools.Items.Add(filter);

        var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2 };
        right.Panel1.Controls.Add(picture);
        right.Panel2.Controls.Add(info);
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(list);
        split.Panel2.Controls.Add(right);
        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(status);
        Controls.Add(split);
        Controls.Add(tools);
        Controls.Add(statusStrip);
        Controls.Add(menu);
        MainMenuStrip = menu;
        Load += (_, _) => { split.SplitterDistance = 470; right.SplitterDistance = Math.Max(200, right.Height - 230); };

        list.SelectedIndexChanged += (_, _) => { FillSlices(); ShowSelected(keepView: false); };
        picture.DoubleClick += (_, _) => picture.Fit();
        picture.MouseMove += (_, e) => ShowPixel(e.Location);
        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] f && f.Length > 0) OpenFile(f[0]); };
        FormClosed += (_, _) => { job?.Cancel(); picture.Image = null; thumbs.Dispose(); };

        if (textures != null) SetTextures(textures, title ?? "");
    }

    /// <summary>Opens a model (MDL3) or a stand-alone TXS3 file; GT6 body_s goes with its body.</summary>
    public bool OpenFile(string path)
    {
        try
        {
            if (path.EndsWith("body_s", StringComparison.OrdinalIgnoreCase)) path = path[..^2];
            var bytes = File.ReadAllBytes(path);
            SetTextures(Mdl3TextureSet.IsTxs3(bytes) ? new Mdl3TextureSet(bytes) : new Mdl3TextureSet(Mdl3File.Load(path)), path);
            return true;
        }
        catch (Exception e)
        {
            MessageBox.Show(this, $"Cannot read {path}:\n{e.Message}", "Textures", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    void OpenDialog()
    {
        using var dlg = new OpenFileDialog { Title = "Open a model (MDL3) or a texture file (TXS3)", Filter = "All files|*.*", InitialDirectory = Directory.Exists(@"F:\GT_DEV") ? @"F:\GT_DEV" : "" };
        if (source.Length > 0 && Directory.Exists(Path.GetDirectoryName(source))) dlg.InitialDirectory = Path.GetDirectoryName(source);
        if (dlg.ShowDialog(this) == DialogResult.OK) OpenFile(dlg.FileName);
    }

    public void SetTextures(Mdl3TextureSet textures, string title)
    {
        job?.Cancel();
        set = textures;
        source = title;
        Text = $"GTViewer - Textures - {title}";
        thumbs.Images.Clear();
        picture.Image = null;
        set.FindBodyS();   // where the GT6 body pixels are, for the info panel
        foreach (var e in set.Entries) thumbs.Images.Add(e.Image.ToString(), Placeholder());
        FillList();
        if (list.Items.Count > 0) list.Items[0].Selected = true;
        else ShowSelected(false);
        status.Text = $"{set.Entries.Count} images, {set.External.Count} external sampler(s) (provided by the game, listed in the info panel)";
        StartThumbnails();
    }

    /// <summary>Thumbnails in the background, one decoder at a time (Mdl3TextureSet.Decode is locked).</summary>
    void StartThumbnails()
    {
        if (set == null) return;
        job?.Cancel();
        var cts = job = new CancellationTokenSource();
        var s = set;
        bool flip = upright.Checked;
        Task.Run(() =>
        {
            foreach (var e in s.Entries)
            {
                if (cts.IsCancellationRequested) return;
                var rgba = s.Decode(e.Image);
                var thumb = Thumbnail(rgba, e.Record.Width, e.Record.Height, flip);
                try { BeginInvoke(() => { if (!cts.IsCancellationRequested) thumbs.Images[e.Image] = thumb; list.Invalidate(); }); }
                catch (InvalidOperationException) { return; }   // window closed
            }
            try { BeginInvoke(() => { if (!cts.IsCancellationRequested) ThumbnailsDone?.Invoke(); }); } catch (InvalidOperationException) { }
        });
    }

    /// <summary>Raised on the UI thread when every thumbnail is decoded (for --tex-shot).</summary>
    public event Action? ThumbnailsDone;

    void FillList()
    {
        if (set == null) return;
        string f = filter.Text.Trim();
        list.BeginUpdate();
        list.Items.Clear();
        list.Groups.Clear();
        var groups = new Dictionary<string, ListViewGroup>();
        foreach (var e in set.Entries)
        {
            string text = string.Join(" ", e.Names) + " " + string.Join(" ", e.Users.Select(u => u.MaterialName + " " + u.Sampler)) + " " + e.Image;
            if (f.Length > 0 && !text.Contains(f, StringComparison.OrdinalIgnoreCase)) continue;
            // group by the sampler that uses it: the diffuse map wins, else the most used one
            string key = e.Users.Count == 0 ? (set.File == null ? "Images" : "Not used by a material")
                : e.Users.Any(u => u.Sampler == "diffuseMapSampler") ? "diffuseMapSampler"
                : e.Users.GroupBy(u => u.Sampler).OrderByDescending(g => g.Count()).First().Key;
            if (!groups.TryGetValue(key, out var g))
            {
                g = new ListViewGroup(key, key);
                groups[key] = g;
            }
            list.Items.Add(new ListViewItem($"{e.ShortName}\n{e.Record.Width}x{e.Record.Height}", e.Image) { Tag = e, Group = g, ToolTipText = string.Join("\n", e.Names) });
        }
        foreach (var g in groups.OrderBy(x => x.Key == "Not used by a material" ? 1 : 0).ThenBy(x => x.Key)) list.Groups.Add(g.Value);
        list.EndUpdate();
    }

    Mdl3TextureEntry? Selected => list.SelectedItems.Count > 0 ? list.SelectedItems[0].Tag as Mdl3TextureEntry : null;

    void FillSlices()
    {
        slices.Items.Clear();
        var e = Selected;
        int n = e == null ? 1 : e.Record.Faces * e.Record.Depth;
        slices.Items.Add(n > 1 ? $"all {n} (stacked)" : "-");
        if (e != null && n > 1)
            for (int i = 0; i < n; i++)
                slices.Items.Add(e.Record.Cube ? $"face {i}" : $"slice {i}");
        slices.SelectedIndex = 0;
        slices.Enabled = n > 1;
    }

    void ShowSelected(bool keepView)
    {
        var e = Selected;
        if (set == null || e == null)
        {
            picture.Image = null;
            info.Text = set == null ? "" : ExternalText();
            return;
        }
        var rgba = set.Decode(e.Image);
        int w = e.Record.Width, h = e.Record.Height, n = e.Record.Faces * e.Record.Depth;
        if (rgba != null)
        {
            int slice = Math.Max(0, slices.SelectedIndex - 1);
            if (slices.SelectedIndex > 0) { rgba = rgba.AsSpan(slice * w * h * 4, w * h * 4).ToArray(); n = 1; }
            picture.SetImage(ToBitmap(Shown(rgba, w, h, n), w, h * n, channels.SelectedIndex), keepView);
        }
        else picture.Image = null;
        info.Text = InfoText(e, rgba != null);
    }

    string InfoText(Mdl3TextureEntry e, bool decoded)
    {
        var r = e.Record;
        List<string> lines =
        [
            $"image {e.Image}: {e.ShortName}",
            $"{r.Width} x {r.Height}{(r.Depth > 1 ? $" x {r.Depth}" : "")}, {e.FormatText}, {r.Mipmaps} mip level(s), {r.Size:N0} bytes, location {(r.Location == 1 ? "local" : r.Location == 2 ? "main" : r.Location.ToString())}",
            decoded ? "" : "PIXELS NOT DECODED (not found or format not handled)",
            "names:",
            .. (e.Names.Count == 0 ? new[] { "  (none)" } : e.Names.Select(n => "  " + n)),
            "used by (material : sampler):",
            .. (e.Users.Count == 0 ? new[] { "  (no material)" } : e.Users.Select(u => $"  {u.MaterialName} (#{u.Material}) : {u.Sampler}")),
            $"pixels in: {(e.Where.Count == 0 ? "(not found)" : string.Join(", ", e.Where.Take(6)) + (e.Where.Count > 6 ? $" ... ({e.Where.Count} copies)" : ""))}",
        ];
        if (set!.External.Count > 0) lines.AddRange(["", ExternalText()]);
        return string.Join("\r\n", lines.Where(l => l != null));
    }

    string ExternalText() => set!.External.Count == 0 ? "No external samplers." :
        "External samplers (textures the game provides at run time: race numbers, meters, paint colour shift...):\r\n" +
        string.Join("\r\n", set.External.GroupBy(x => (x.Sampler, x.Index)).OrderBy(g => g.Key.Sampler)
            .Select(g => $"  {g.Key.Sampler} #{g.Key.Index}: {string.Join(", ", g.Select(x => x.MaterialName).Distinct().Take(4))}{(g.Select(x => x.MaterialName).Distinct().Count() > 4 ? " ..." : "")}"));

    void ShowPixel(Point at)
    {
        if (picture.ToImage(at) is not { } p || picture.Image is not Bitmap bmp || Selected is not { } e) return;
        var c = bmp.GetPixel(p.X, p.Y);
        status.Text = $"image {e.Image} {e.ShortName}  x {p.X} y {p.Y}  shown {channels.Text}: {c.R} {c.G} {c.B} {c.A}  zoom {picture.Zoom * 100:0}%";
    }

    /// <summary>The pixels as shown: with Upright, every face / slice flipped on its own (their order is kept).</summary>
    byte[] Shown(byte[] rgba, int w, int h, int slices)
    {
        if (!upright.Checked) return rgba;
        var o = new byte[rgba.Length];
        int row = w * 4;
        for (int s = 0; s < slices; s++)
            for (int y = 0; y < h; y++)
                Array.Copy(rgba, (s * h + y) * row, o, (s * h + h - 1 - y) * row, row);
        return o;
    }

    static string SafeName(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    void SaveSelected()
    {
        if (set == null || Selected is not { } e) return;
        var rgba = set.Decode(e.Image);
        if (rgba == null) { MessageBox.Show(this, "This image could not be decoded.", "Textures"); return; }
        using var dlg = new SaveFileDialog { Title = "Save the texture (level 0, RGBA)", Filter = "PNG|*.png|TGA (32-bit, with alpha)|*.tga", FileName = SafeName(Path.GetFileNameWithoutExtension(e.ShortName)) + ".png" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string file = dlg.FileName;
        if (dlg.FilterIndex == 2 && !file.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)) file = Path.ChangeExtension(file, ".tga");
        Save(file, e, rgba);
        status.Text = $"Saved {file}{(upright.Checked ? " (upright)" : " (as stored, bottom up)")}";
    }

    /// <summary>One image (all faces / slices stacked) as shown (Upright or not) to PNG or TGA by the extension.</summary>
    void Save(string file, Mdl3TextureEntry e, byte[] rgba)
    {
        int w = e.Record.Width, h = e.Record.Height, n = e.Record.Faces * e.Record.Depth;
        var px = Shown(rgba, w, h, n);
        if (file.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)) { WriteTga(file, px, w, h * n); return; }
        using var bmp = ToBitmap(px, w, h * n, 0);
        bmp.Save(file, ImageFormat.Png);
    }

    /// <summary>Uncompressed 32-bit TGA (BGRA, 8 alpha bits, rows top to bottom: descriptor 0x28).</summary>
    public static void WriteTga(string file, byte[] rgba, int w, int h)
    {
        var o = new byte[18 + w * h * 4];
        o[2] = 2;                                          // uncompressed true colour
        o[12] = (byte)w; o[13] = (byte)(w >> 8);
        o[14] = (byte)h; o[15] = (byte)(h >> 8);
        o[16] = 32;
        o[17] = 0x28;                                      // 8 alpha bits, origin top left
        for (int i = 0; i < w * h; i++)
        {
            o[18 + i * 4] = rgba[i * 4 + 2]; o[19 + i * 4] = rgba[i * 4 + 1]; o[20 + i * 4] = rgba[i * 4]; o[21 + i * 4] = rgba[i * 4 + 3];
        }
        File.WriteAllBytes(file, o);
    }

    void SaveAll(string ext)
    {
        if (set == null) return;
        using var dlg = new FolderBrowserDialog { Description = $"Folder for the {ext[1..].ToUpperInvariant()} files (one per image: NNN_name{ext})", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        Cursor = Cursors.WaitCursor;
        int saved = 0, failed = 0;
        foreach (var e in set.Entries)
        {
            var rgba = set.Decode(e.Image);
            if (rgba == null) { failed++; continue; }
            Save(Path.Combine(dlg.SelectedPath, $"{e.Image:D3}_{SafeName(Path.GetFileNameWithoutExtension(e.ShortName))}{ext}"), e, rgba);
            saved++;
        }
        Cursor = Cursors.Default;
        status.Text = $"{saved} {ext[1..].ToUpperInvariant()} saved to {dlg.SelectedPath}{(upright.Checked ? " (upright)" : " (as stored)")}{(failed > 0 ? $", {failed} not decoded" : "")}";
    }

    /// <summary>RGBA8 to a 32-bit ARGB bitmap: 0 RGBA, 1 RGB (opaque), 2 alpha as grey, 3-5 one channel as grey.</summary>
    public static Bitmap ToBitmap(byte[] rgba, int w, int h, int mode, bool flipV = false)
    {
        var bgra = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            int s = flipV ? ((h - 1 - i / w) * w + i % w) * 4 : i * 4;   // GT stores rows bottom up (v = 0 at the bottom)
            byte r = rgba[s], g = rgba[s + 1], b = rgba[s + 2], a = rgba[s + 3];
            (byte R, byte G, byte B, byte A) o = mode switch
            {
                1 => (r, g, b, 255), 2 => (a, a, a, 255), 3 => (r, r, r, 255), 4 => (g, g, g, 255), 5 => (b, b, b, 255),
                _ => (r, g, b, a),
            };
            bgra[i * 4] = o.B; bgra[i * 4 + 1] = o.G; bgra[i * 4 + 2] = o.R; bgra[i * 4 + 3] = o.A;
        }
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < h; y++) Marshal.Copy(bgra, y * w * 4, data.Scan0 + y * data.Stride, w * 4);
        bmp.UnlockBits(data);
        return bmp;
    }

    static Bitmap Placeholder()
    {
        var bmp = new Bitmap(96, 96);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(60, 62, 68));
        return bmp;
    }

    /// <summary>96x96 thumbnail of the first face / slice over a checkerboard, aspect kept; grey with an X when not decoded.</summary>
    static Bitmap Thumbnail(byte[]? rgba, int w, int h, bool flipV)
    {
        var bmp = new Bitmap(96, 96);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(60, 62, 68));
        if (rgba == null || w == 0 || h == 0)
        {
            using var pen = new Pen(Color.IndianRed, 2);
            g.DrawLine(pen, 20, 20, 76, 76); g.DrawLine(pen, 76, 20, 20, 76);
            return bmp;
        }
        float s = Math.Min(96f / w, 96f / h);
        var dst = new RectangleF((96 - w * s) / 2, (96 - h * s) / 2, w * s, h * s);
        ImagePanel.Checker(g, Rectangle.Round(dst), 8);
        using var src = ToBitmap(rgba.AsSpan(0, w * h * 4).ToArray(), w, h, 0, flipV);
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.DrawImage(src, dst);
        return bmp;
    }

    /// <summary>--tex-shot: the window with a file, an image selected (by index), as a PNG; no screen capture.</summary>
    public void Shot(string png, int image, int mode)
    {
        channels.SelectedIndex = mode;
        foreach (ListViewItem it in list.Items) if (it.Tag is Mdl3TextureEntry e && e.Image == image) { it.Selected = true; it.EnsureVisible(); }
        Application.DoEvents();
        using var bmp = new Bitmap(Width, Height);
        DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
        bmp.Save(png, ImageFormat.Png);
        File.WriteAllText(Path.ChangeExtension(png, ".txt"), info.Text);
        // the selected image saved both ways, to compare the TGA with the PNG
        if (set != null && Selected is { } sel && set.Decode(sel.Image) is { } px)
        {
            Save(Path.ChangeExtension(png, ".image.png"), sel, px);
            Save(Path.ChangeExtension(png, ".image.tga"), sel, px);
        }
    }
}

/// <summary>Picture with zoom (wheel, at the cursor), pan (drag) and a checkerboard under transparent pixels.</summary>
sealed class ImagePanel : Panel
{
    Bitmap? image;
    PointF offset;
    Point last;
    bool dragging;
    public float Zoom { get; private set; } = 1;

    public ImagePanel()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(41, 43, 48);
        MouseDown += (_, e) => { dragging = true; last = e.Location; Focus(); };
        MouseUp += (_, _) => dragging = false;
        MouseMove += (_, e) => { if (dragging) { offset.X += e.X - last.X; offset.Y += e.Y - last.Y; last = e.Location; Invalidate(); } };
        MouseWheel += (_, e) =>
        {
            float z = Math.Clamp(Zoom * (e.Delta > 0 ? 1.25f : 0.8f), 0.02f, 64f);
            // keep the pixel under the cursor in place
            offset.X = e.X - (e.X - offset.X) * z / Zoom;
            offset.Y = e.Y - (e.Y - offset.Y) * z / Zoom;
            Zoom = z;
            Invalidate();
        };
        Resize += (_, _) => Invalidate();
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Bitmap? Image
    {
        get => image;
        set { image?.Dispose(); image = value; Invalidate(); }
    }

    public void SetImage(Bitmap bmp, bool keepView)
    {
        bool same = keepView && image != null && image.Size == bmp.Size;
        Image = bmp;
        if (!same) Fit();
    }

    public void Fit()
    {
        if (image == null) return;
        Zoom = Math.Min(8f, Math.Min((Width - 20f) / image.Width, (Height - 20f) / image.Height));   // small ones up to 8x
        if (Zoom <= 0) Zoom = 1;
        offset = new PointF((Width - image.Width * Zoom) / 2, (Height - image.Height * Zoom) / 2);
        Invalidate();
    }

    public void SetZoom(float z)
    {
        if (image == null) return;
        Zoom = z;
        offset = new PointF((Width - image.Width * Zoom) / 2, (Height - image.Height * Zoom) / 2);
        Invalidate();
    }

    /// <summary>Image pixel under a panel point, null outside.</summary>
    public Point? ToImage(Point p)
    {
        if (image == null) return null;
        int x = (int)Math.Floor((p.X - offset.X) / Zoom), y = (int)Math.Floor((p.Y - offset.Y) / Zoom);
        return x >= 0 && y >= 0 && x < image.Width && y < image.Height ? new Point(x, y) : null;
    }

    public static void Checker(Graphics g, Rectangle r, int cell)
    {
        using var light = new SolidBrush(Color.FromArgb(204, 204, 204));
        using var dark = new SolidBrush(Color.FromArgb(153, 153, 153));
        g.FillRectangle(light, r);
        var clip = g.Clip;
        g.SetClip(r);
        for (int y = r.Top; y < r.Bottom; y += cell)
            for (int x = r.Left + ((y - r.Top) / cell % 2) * cell; x < r.Right; x += cell * 2)
                g.FillRectangle(dark, x, y, cell, cell);
        g.Clip = clip;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (image == null) return;
        var dst = new RectangleF(offset.X, offset.Y, image.Width * Zoom, image.Height * Zoom);
        var visible = Rectangle.Intersect(Rectangle.Round(dst), ClientRectangle);
        if (visible.Width > 0 && visible.Height > 0) Checker(e.Graphics, visible, 12);
        e.Graphics.InterpolationMode = Zoom >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImage(image, dst);
    }
}
