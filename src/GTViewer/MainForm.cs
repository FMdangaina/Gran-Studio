using System.Numerics;
using GTFormats.Mdl3;
using GTViewer.Rendering;

namespace GTViewer;

/// <summary>
/// Main window, in the style of Forza Studio: the model tree with check boxes (show / hide) on the left, the switches
/// (day/night, lights, custom parts) and the LOD level under it, the 3D view on the right, the selected shape in the
/// status bar. Mouse in the view: left drag orbit, right or middle drag pan, wheel zoom; F frames the visible parts.
/// </summary>
public sealed class MainForm : Form
{
    readonly Renderer renderer;
    readonly OrbitCamera camera = new();
    readonly TreeView tree = new() { Dock = DockStyle.Fill, CheckBoxes = true, HideSelection = false, Font = new Font("Segoe UI", 9) };
    readonly Panel view = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(41, 43, 48) };
    readonly FlowLayoutPanel switches = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
    readonly ToolStripStatusLabel status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly ToolStripMenuItem wireItem = new("&Wireframe") { CheckOnClick = true, ShortcutKeys = Keys.Control | Keys.W };
    readonly ToolStripMenuItem texItem = new("&Textures") { CheckOnClick = true, Checked = true, ShortcutKeys = Keys.Control | Keys.T };
    readonly ToolStripMenuItem listItem = new("Car &list") { CheckOnClick = true, ShortcutKeys = Keys.Control | Keys.L };
    readonly SplitContainer outer = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2 };
    readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    readonly TabPage viewTab = new("3D view");
    readonly TabPage texturesTab = new("Textures");
    readonly TexturePanel texturePanel = new() { Dock = DockStyle.Fill };
    ViewerDocument? doc;
    string? docTitle;   // the car list title of the open file (car name - game code part)
    bool filling;
    Point lastMouse;
    MouseButtons dragging;

    public MainForm(string? file)
    {
        Text = "GTViewer";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);   // the exe icon (granstudio.ico) on the window too
        ClientSize = new Size(1400, 940);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        AllowDrop = true;

        var menu = new MenuStrip();
        var fileMenu = new ToolStripMenuItem("&File");
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("Open &game folder (car list by name)...", null, (_, _) => OpenGameFolder()) { ShortcutKeys = Keys.Control | Keys.G });
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("&Open file...", null, (_, _) => OpenDialog()) { ShortcutKeys = Keys.Control | Keys.O });
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("&Export visible to OBJ...", null, (_, _) => ExportObj()) { ShortcutKeys = Keys.Control | Keys.E });
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("Export the whole &car to OBJ (hq, race, cockpit, wheels, all LOD levels)...", null, (_, _) => ExportObj(true)) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.E });
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("Export visible to &FBX (hierarchy, all LOD levels) + TGA textures...", null, (_, _) => ExportFbx()) { ShortcutKeys = Keys.Control | Keys.F });
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("Export the whole car to FBX (hq, race, cockpit, wheels) + TGA textures...", null, (_, _) => ExportFbx(true)) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.F });
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("Export all &textures of this file (TGA)...", null, (_, _) => ExportTextures()));
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (_, _) => Close()));
        var viewMenu = new ToolStripMenuItem("&View");
        viewMenu.DropDownItems.Add(wireItem);
        viewMenu.DropDownItems.Add(texItem);
        var aoItem = new ToolStripMenuItem("Vertex &occlusion (GT6 shading)") { CheckOnClick = true, Checked = true };
        aoItem.CheckedChanged += (_, _) => { renderer!.VertexOcclusion = aoItem.Checked; view.Invalidate(); };
        viewMenu.DropDownItems.Add(aoItem);
        viewMenu.DropDownItems.Add(new ToolStripMenuItem("Te&xture browser (tab)", null, (_, _) => tabs.SelectedTab = tabs.SelectedTab == texturesTab ? viewTab : texturesTab) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.T });
        viewMenu.DropDownItems.Add(new ToolStripMenuItem("&Frame all", null, (_, _) => FrameAll()) { ShortcutKeyDisplayString = "F" });
        viewMenu.DropDownItems.Add(new ToolStripMenuItem("Frame &selected", null, (_, _) => FrameSelected()) { ShortcutKeyDisplayString = "S" });
        menu.Items.Add(fileMenu);
        menu.Items.Add(viewMenu);
        wireItem.CheckedChanged += (_, _) => { renderer!.Wireframe = wireItem.Checked; view.Invalidate(); };
        texItem.CheckedChanged += (_, _) => { if (doc != null) doc.Textures = texItem.Checked; view.Invalidate(); };

        var left = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 520 };
        left.Panel1.Controls.Add(tree);
        left.Panel2.Controls.Add(switches);
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 400, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(left);
        // right: the 3D view and the texture browser as tabs (no pop-up windows)
        viewTab.Controls.Add(view);
        texturesTab.Controls.Add(texturePanel);
        tabs.TabPages.Add(viewTab);
        tabs.TabPages.Add(texturesTab);
        tabs.SelectedIndexChanged += (_, _) => { if (tabs.SelectedTab == texturesTab) FillTextures(); };
        split.Panel2.Controls.Add(tabs);
        // the car list (File > Open game folder) docks under the tree and the view
        outer.Panel1.Controls.Add(split);
        outer.Panel2Collapsed = true;
        listItem.CheckedChanged += (_, _) => { outer.Panel2Collapsed = !listItem.Checked || carPanel == null; if (!outer.Panel2Collapsed) PlaceListSplitter(); };
        viewMenu.DropDownItems.Add(new ToolStripSeparator());
        viewMenu.DropDownItems.Add(listItem);
        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(status);
        Controls.Add(outer);
        Controls.Add(statusStrip);
        Controls.Add(menu);
        MainMenuStrip = menu;

        renderer = new Renderer();
        view.HandleCreated += (_, _) => renderer.AttachWindow(view.Handle, view.ClientSize.Width, view.ClientSize.Height);
        view.Resize += (_, _) => { renderer.Resize(view.ClientSize.Width, view.ClientSize.Height); view.Invalidate(); };
        view.Paint += (_, _) => RenderView();
        view.MouseDown += (_, e) => { dragging = e.Button; lastMouse = e.Location; view.Focus(); };
        view.MouseUp += (_, _) => dragging = MouseButtons.None;
        view.MouseMove += OnViewMouseMove;
        view.MouseWheel += (_, e) => { camera.Zoom(e.Delta / 120f); view.Invalidate(); };
        tree.AfterCheck += OnAfterCheck;
        tree.AfterSelect += OnAfterSelect;
        tree.NodeMouseDoubleClick += (_, _) => FrameSelected();
        KeyDown += (_, e) =>
        {
            if (tree.Focused || carPanel?.ContainsFocus == true || tabs.SelectedTab != viewTab || e.Modifiers != Keys.None) return;   // typing in the search box
            if (e.KeyCode == Keys.F) { FrameAll(); e.Handled = true; }
            if (e.KeyCode == Keys.S) { FrameSelected(); e.Handled = true; }
        };
        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] f && f.Length > 0) Open(f[0]); };
        // splitter positions only hold once the window has its size
        Load += (_, _) => { split.SplitterDistance = 420; left.SplitterDistance = Math.Max(200, left.Height - 260); };
        Shown += (_, _) =>
        {
            if (file != null) Open(file);
            else status.Text = "File > Open game folder (Ctrl+G) to pick a car by name, File > Open file (Ctrl+O), or drop a model file here.";
            // the game folder of the last session comes back with its car list
            if (!NoAutoLoad && LastGameFolder() is { } last && Directory.Exists(Path.Combine(last, "car"))) _ = LoadGameFolder(last);
        };
        FormClosed += (_, _) => { doc?.Dispose(); renderer.Dispose(); };
    }

    void OpenDialog()
    {
        using var dlg = new OpenFileDialog { Title = "Open a GT5/GT6 model (MDL3)", Filter = "All files|*.*", InitialDirectory = Directory.Exists(@"F:\GT_DEV") ? @"F:\GT_DEV" : "" };
        if (doc != null) dlg.InitialDirectory = Path.GetDirectoryName(doc.Path);
        if (dlg.ShowDialog(this) == DialogResult.OK) Open(dlg.FileName);
    }

    static readonly string SettingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GTViewer", "last_game_folder.txt");
    CarListPanel? carPanel;

    static string? LastGameFolder()
    {
        try { return File.Exists(SettingsFile) ? File.ReadAllText(SettingsFile).Trim() : null; } catch { return null; }
    }

    void OpenGameFolder()
    {
        string last = LastGameFolder() ?? "";
        using var dlg = new FolderBrowserDialog
        {
            Description = "Pick the extracted game folder (GT5_VOL or GT6_VOL)",
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(last) ? last : Directory.Exists(@"F:\GT_DEV") ? @"F:\GT_DEV" : "",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!); File.WriteAllText(SettingsFile, dlg.SelectedPath); } catch { }
        _ = LoadGameFolder(dlg.SelectedPath);
    }

    /// <summary>For --ui-shot: no game folder from the last session.</summary>
    public bool NoAutoLoad;

    /// <summary>--ui-shot: the window with a game folder's list, a search and its first item selected, as a PNG.</summary>
    public async Task ShotUi(string root, int tab, string search, string png)
    {
        await LoadGameFolder(root);
        carPanel!.Select(tab, search);
        Application.DoEvents();
        using var bmp = new Bitmap(Width, Height);
        DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
        bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
    }

    async Task LoadGameFolder(string root)
    {
        carPanel?.Dispose();
        carPanel = new CarListPanel(root, (file, title) => Open(file, title)) { Dock = DockStyle.Fill };
        outer.Panel2.Controls.Add(carPanel);
        listItem.Checked = true;
        outer.Panel2Collapsed = false;
        PlaceListSplitter();
        try { await carPanel.LoadAsync(); }
        catch (Exception e) { status.Text = "Car list: " + e.Message; }
    }

    /// <summary>The car list takes the lower third of the window (at least 260 px).</summary>
    void PlaceListSplitter() => outer.SplitterDistance = Math.Max(200, outer.Height - Math.Max(260, outer.Height / 3));

    /// <summary>The Textures tab shows the open model's textures (filled when the tab is shown, decoded once per model).</summary>
    void FillTextures()
    {
        if (doc != null && texturePanel.Current != doc.TextureSet) texturePanel.SetTextures(doc.TextureSet, Text.Replace("GTViewer - ", ""));
    }

    /// <summary>--tex-shot: the main window with a file in the Textures tab, an image selected, as a PNG.</summary>
    public void ShotTextures(string file, string png, int image, int mode)
    {
        texturePanel.ThumbnailsDone += () => { texturePanel.Shot(png, image, mode, this); Close(); };
        Open(file);
        tabs.SelectedTab = texturesTab;
        FillTextures();
        if (texturePanel.Current == null) { Console.Error.WriteLine(status.Text); Close(); }
    }

    void Open(string path, string? title = null)
    {
        if (path.EndsWith("body_s", StringComparison.OrdinalIgnoreCase)) path = path[..^2];   // GT6: the body_s goes with body
        if (IsTxs3File(path))
        {
            // a texture file (thumbnail, decken): the Textures tab
            tabs.SelectedTab = texturesTab;
            texturePanel.OpenFile(path);
            return;
        }
        ViewerDocument next;
        try
        {
            Cursor = Cursors.WaitCursor;
            next = new ViewerDocument(path, renderer) { Textures = texItem.Checked };
        }
        catch (Exception e)
        {
            status.Text = $"Cannot open {path}: {e.Message} (MDL3 files of the old versions 1-4 are not read.)";
            return;
        }
        finally { Cursor = Cursors.Default; }
        doc?.Dispose();
        doc = next;
        docTitle = title;
        Text = title != null ? $"GTViewer - {title}" : $"GTViewer - {path}";
        if (title != null) doc.Scene.Root.Name = title;
        FillTree();
        FillSwitches();
        FrameAll();
        UpdateStatus();
        if (tabs.SelectedTab == texturesTab) FillTextures();
    }

    void UpdateStatus()
    {
        if (doc == null) return;
        status.Text = $"{doc.File.Models.Count} models, {doc.File.Shapes.Count} shapes, {doc.Scene.Selectors.Count} switches, " +
            $"{doc.LodCount} LOD level(s), showing LOD{doc.Lod}: {doc.VisibleTriangles():N0} triangles{(renderer.Software ? " - software rendering" : "")}";
    }

    static bool IsTxs3File(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            var head = new byte[4];
            return f.Read(head, 0, 4) == 4 && head[0] == 'T' && head[1] == 'X' && head[2] == 'S' && head[3] == '3';
        }
        catch { return false; }
    }

    void FillTree()
    {
        filling = true;
        tree.BeginUpdate();
        tree.Nodes.Clear();
        var root = Add(tree.Nodes, doc!.Scene.Root);
        root.Expand();
        foreach (TreeNode m in root.Nodes) if (m.Checked) m.Expand();
        tree.EndUpdate();
        filling = false;
        UpdateTreeColors();

        TreeNode Add(TreeNodeCollection into, Mdl3SceneNode n)
        {
            var t = new TreeNode(Label(n)) { Tag = n, Checked = !doc.Hidden.Contains(n) };
            into.Add(t);
            foreach (var c in n.Children) Add(t.Nodes, c);
            return t;
        }
    }

    string Label(Mdl3SceneNode n)
    {
        if (n.Kind != "shape" || n.Model < 0) return n.Name;
        // switched shapes: the switch options they belong to
        var conds = doc!.Scene.Draws[n.Shape].Where(d => d.Model == n.Model).SelectMany(d => d.Conditions)
            .Select(q => $"{Short(doc.Scene.Selectors[q.Selector])} {string.Join(",", q.Branches)}").Distinct().ToList();
        // inside op8 LOD selects: the levels it is drawn at
        var levels = Enumerable.Range(0, doc.Scene.Levels).Where(k => doc.Scene.Draws[n.Shape].Any(d => d.Model == n.Model && d.Lods.Length > 0 && d.AtLevel(k))).ToList();
        string lod = levels.Count == 0 || levels.Count == doc.Scene.Levels && doc.Scene.Draws[n.Shape].Any(d => d.Model == n.Model && d.Lods.Length == 0) ? ""
            : levels.Count == 1 ? $" LOD{levels[0]}" : levels.Zip(levels.Skip(1)).All(p => p.Second == p.First + 1) ? $" LOD{levels[0]}-{levels[^1]}" : $" LOD{string.Join(",", levels)}";
        return conds.Count == 0 ? n.Name + lod : $"{n.Name}{lod}  [{string.Join("; ", conds)}]";
    }

    static string Short(Mdl3Selector s) => s.Opcode == 54 && s.Name.StartsWith("custom_") ? s.Name[..s.Name.IndexOf(' ')] : $"switch {s.Register}";

    /// <summary>Grey for nodes not drawn with the current switches and LOD level.</summary>
    void UpdateTreeColors()
    {
        if (doc == null) return;
        var drawn = doc.VisibleShapes().ToHashSet();
        tree.BeginUpdate();
        foreach (var t in All(tree.Nodes))
            if (t.Tag is Mdl3SceneNode { Kind: "shape" } n)
                t.ForeColor = !doc.IsShown(n) || drawn.Contains(n) ? SystemColors.WindowText : SystemColors.GrayText;
        tree.EndUpdate();
    }

    static IEnumerable<TreeNode> All(TreeNodeCollection c)
    {
        foreach (TreeNode t in c)
        {
            yield return t;
            foreach (var x in All(t.Nodes)) yield return x;
        }
    }

    void FillSwitches()
    {
        switches.SuspendLayout();
        switches.Controls.Clear();
        int width = Math.Max(200, switches.ClientSize.Width - 24);
        if (doc!.LodCount > 1)
        {
            switches.Controls.Add(new Label { Text = "LOD level", AutoSize = true, Margin = new Padding(3, 6, 3, 0) });
            var lod = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width };
            for (int i = 0; i < doc.LodCount; i++) lod.Items.Add(i == 0 ? "LOD0 (most detail)" : i == doc.LodCount - 1 ? $"LOD{i} (least detail)" : $"LOD{i}");
            lod.SelectedIndex = doc.Lod;
            lod.SelectedIndexChanged += (_, _) => { doc.Lod = lod.SelectedIndex; UpdateTreeColors(); UpdateStatus(); view.Invalidate(); };
            switches.Controls.Add(lod);
        }
        // GT6 paint colours from the database (ROADMAP 13): the colour of the body and the matching variation
        if (doc.Paints.Count > 0)
        {
            switches.Controls.Add(new Label { Text = "Paint colour", AutoSize = true, Margin = new Padding(3, 6, 3, 0) });
            var paint = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, DrawMode = DrawMode.OwnerDrawFixed };
            foreach (var p in doc.Paints) paint.Items.Add(p.Name);
            paint.DrawItem += (_, e) =>
            {
                e.DrawBackground();
                if (e.Index < 0) return;
                var c = doc.Paints[e.Index].Color;
                using var b = new SolidBrush(Color.FromArgb((int)(c.X * 255), (int)(c.Y * 255), (int)(c.Z * 255)));
                e.Graphics.FillRectangle(b, e.Bounds.X + 2, e.Bounds.Y + 2, 24, e.Bounds.Height - 4);
                e.Graphics.DrawRectangle(Pens.Gray, e.Bounds.X + 2, e.Bounds.Y + 2, 24, e.Bounds.Height - 4);
                TextRenderer.DrawText(e.Graphics, doc.Paints[e.Index].Name, e.Font, new Point(e.Bounds.X + 30, e.Bounds.Y + 1), e.ForeColor);
            };
            paint.SelectedIndex = Math.Max(0, doc.Paint);
            paint.SelectedIndexChanged += (_, _) => { doc.SetPaint(paint.SelectedIndex); view.Invalidate(); };
            switches.Controls.Add(paint);
        }
        // custom parts first, then the state switches
        foreach (var s in doc.Scene.Selectors.OrderBy(s => s.Opcode == 54 ? 0 : 1).ThenBy(s => s.Name))
        {
            switches.Controls.Add(new Label { Text = s.Name, AutoSize = true, Margin = new Padding(3, 6, 3, 0) });
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, Tag = s };
            box.Items.AddRange(s.Options);
            box.SelectedIndex = Math.Min(doc.State[s.Id], s.Options.Length - 1);
            box.SelectedIndexChanged += (_, _) => { doc.State[s.Id] = box.SelectedIndex; UpdateTreeColors(); view.Invalidate(); };
            switches.Controls.Add(box);
        }
        if (doc.Scene.Selectors.Count == 0 && doc.LodCount <= 1) switches.Controls.Add(new Label { Text = "No switches in this file.", AutoSize = true });
        switches.ResumeLayout();
    }

    void OnAfterCheck(object? sender, TreeViewEventArgs e)
    {
        if (filling || doc == null || e.Node?.Tag is not Mdl3SceneNode n) return;
        if (e.Node.Checked) doc.Hidden.Remove(n); else doc.Hidden.Add(n);
        UpdateTreeColors();
        view.Invalidate();
    }

    void OnAfterSelect(object? sender, TreeViewEventArgs e)
    {
        if (doc == null || e.Node?.Tag is not Mdl3SceneNode n) return;
        doc.Selected = n.Kind == "shape" ? n : null;
        status.Text = Describe(n);
        view.Invalidate();
    }

    string Describe(Mdl3SceneNode n)
    {
        var f = doc!.File;
        if (n.Kind != "shape")
        {
            int shapes = n.All().Count(x => x.Kind == "shape");
            return $"{n.Kind} {n.Name}: {shapes} shape(s)";
        }
        var sh = f.Shapes[n.Shape];
        int mi = doc.MaterialOf(n.Shape);
        string mat = mi >= 0 && mi < f.Materials.Count ? $"{f.Materials[mi].Name} ({doc.TextureSet.Look(mi).ColorSource})" : "-";
        var mesh = doc.Mesh(n.Shape, out var why);
        string geo = mesh == null ? $"no mesh: {why}" : $"{mesh.IndexCount / 3} triangles";
        string path = f.ShapeNames.TryGetValue(n.Shape, out var p) ? p[0] : "";
        return $"shape {n.Shape}  material {mi} {mat}  {geo}  |  {path}";
    }

    void OnViewMouseMove(object? sender, MouseEventArgs e)
    {
        if (dragging == MouseButtons.None) return;
        float dx = e.X - lastMouse.X, dy = e.Y - lastMouse.Y;
        lastMouse = e.Location;
        if (dragging == MouseButtons.Left) camera.Orbit(dx, dy);
        else camera.Pan(dx, dy, view.ClientSize.Height);
        view.Invalidate();
    }

    void FrameAll()
    {
        if (doc == null) return;
        doc.DrawItems(out var min, out var max);
        camera.Frame(min, max);
        view.Invalidate();
    }

    void FrameSelected()
    {
        if (doc == null || tree.SelectedNode?.Tag is not Mdl3SceneNode n) return;
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        foreach (var s in n.All().Where(x => x.Kind == "shape"))
            if (doc.Mesh(s.Shape, out _) is { } m) { min = Vector3.Min(min, m.Min); max = Vector3.Max(max, m.Max); }
        if (min.X > max.X) return;
        camera.Frame(min, max);
        view.Invalidate();
    }

    void RenderView()
    {
        if (renderer.Width == 0) return;
        var items = doc?.DrawItems(out _, out _) ?? [];
        renderer.Render(camera.ViewProj(renderer.Width / (float)Math.Max(1, renderer.Height)), new Vector3(-0.4f, -1, -0.6f), items);
        renderer.Present();
    }

    void ExportObj(bool allLods = false)
    {
        if (doc == null) return;
        // the car name when it was opened from the car list, else the file name
        string stem = System.Text.RegularExpressions.Regex.Replace(
            ObjMaterials.Clean(docTitle != null ? docTitle.Split(" - ")[0] : Path.GetFileName(doc.Path)), "_+", "_");
        using var dlg = new SaveFileDialog
        {
            Title = allLods ? "Export the whole car to OBJ: name\\hq, name\\race (lodN), name\\cockpit, name\\wheel\\... (every wheel)" : "Export the visible shapes to OBJ",
            Filter = "Wavefront OBJ|*.obj",
            FileName = stem + ".obj",
            OverwritePrompt = !allLods,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            Cursor = Cursors.WaitCursor;
            if (!allLods)
            {
                int n = ObjExport.Write(doc, dlg.FileName);
                status.Text = $"{n} shapes exported to {dlg.FileName} (LOD{doc.Lod})";
                return;
            }
            string dir = Path.GetDirectoryName(dlg.FileName)!, name = Path.GetFileNameWithoutExtension(dlg.FileName);
            var parts = ObjExport.WriteCar(doc, renderer, dir, name);
            status.Text = $"Exported to {Path.Combine(dir, name)} (shapes) {string.Join(" | ", parts)}";
        }
        catch (Exception e) { status.Text = "Export failed: " + e.Message; }
        finally { Cursor = Cursors.Default; }
    }

    void ExportFbx(bool wholeCar = false)
    {
        if (doc == null) return;
        string stem = System.Text.RegularExpressions.Regex.Replace(
            ObjMaterials.Clean(docTitle != null ? docTitle.Split(" - ")[0] : Path.GetFileName(doc.Path)), "_+", "_");
        using var dlg = new SaveFileDialog
        {
            Title = wholeCar ? "Export the whole car to FBX: name\name_hq.fbx, name_race.fbx, name_cockpit.fbx, name_wheel_...fbx + name\textures" : "Export the visible shapes to FBX (textures in a 'textures' folder next to it)",
            Filter = "Autodesk FBX|*.fbx",
            FileName = stem + ".fbx",
            OverwritePrompt = !wholeCar,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            Cursor = Cursors.WaitCursor;
            if (!wholeCar)
            {
                var counts = FbxExport.Write(doc, dlg.FileName);
                status.Text = $"Exported {dlg.FileName}: {string.Join(", ", counts.Select((c, k) => $"LOD{k} {c} shapes"))}";
                return;
            }
            string dir = Path.GetDirectoryName(dlg.FileName)!, name = Path.GetFileNameWithoutExtension(dlg.FileName);
            var parts = FbxExport.WriteCar(doc, renderer, dir, name);
            status.Text = $"Exported to {Path.Combine(dir, name)}: {string.Join(" | ", parts)}";
        }
        catch (Exception e) { status.Text = "Export failed: " + e.Message; }
        finally { Cursor = Cursors.Default; }
    }

    void ExportTextures()
    {
        if (doc == null) return;
        using var dlg = new FolderBrowserDialog { Description = "Export every texture of this file (TGA) into this folder", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { status.Text = $"{FbxExport.WriteAllTextures(doc, dlg.SelectedPath)} textures (TGA) written to {dlg.SelectedPath}"; }
        catch (Exception e) { status.Text = "Export failed: " + e.Message; }
    }
}
