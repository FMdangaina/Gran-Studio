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
    ViewerDocument? doc;
    bool filling;
    Point lastMouse;
    MouseButtons dragging;

    public MainForm(string? file)
    {
        Text = "GTViewer";
        ClientSize = new Size(1400, 860);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        AllowDrop = true;

        var menu = new MenuStrip();
        var fileMenu = new ToolStripMenuItem("&File");
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("&Open...", null, (_, _) => OpenDialog()) { ShortcutKeys = Keys.Control | Keys.O });
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("&Export visible to OBJ...", null, (_, _) => ExportObj()) { ShortcutKeys = Keys.Control | Keys.E });
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (_, _) => Close()));
        var viewMenu = new ToolStripMenuItem("&View");
        viewMenu.DropDownItems.Add(wireItem);
        viewMenu.DropDownItems.Add(texItem);
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
        split.Panel2.Controls.Add(view);
        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(status);
        Controls.Add(split);
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
            if (e.KeyCode == Keys.F && !tree.Focused) { FrameAll(); e.Handled = true; }
            if (e.KeyCode == Keys.S && !tree.Focused) { FrameSelected(); e.Handled = true; }
        };
        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] f && f.Length > 0) Open(f[0]); };
        // splitter positions only hold once the window has its size
        Load += (_, _) => { split.SplitterDistance = 420; left.SplitterDistance = Math.Max(200, left.Height - 260); };
        Shown += (_, _) => { if (file != null) Open(file); else status.Text = "File > Open (Ctrl+O) or drop a GT5/GT6 model file (car body, interior, wheel) here."; };
        FormClosed += (_, _) => { doc?.Dispose(); renderer.Dispose(); };
    }

    void OpenDialog()
    {
        using var dlg = new OpenFileDialog { Title = "Open a GT5/GT6 model (MDL3)", Filter = "All files|*.*", InitialDirectory = Directory.Exists(@"F:\GT_DEV") ? @"F:\GT_DEV" : "" };
        if (doc != null) dlg.InitialDirectory = Path.GetDirectoryName(doc.Path);
        if (dlg.ShowDialog(this) == DialogResult.OK) Open(dlg.FileName);
    }

    void Open(string path)
    {
        if (path.EndsWith("body_s", StringComparison.OrdinalIgnoreCase)) path = path[..^2];   // GT6: the body_s goes with body
        ViewerDocument next;
        try
        {
            Cursor = Cursors.WaitCursor;
            next = new ViewerDocument(path, renderer) { Textures = texItem.Checked };
        }
        catch (Exception e)
        {
            MessageBox.Show(this, $"Cannot open {path}:\n{e.Message}\n\n(MDL3 files of the old versions 1-4 are not read.)", "GTViewer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        finally { Cursor = Cursors.Default; }
        doc?.Dispose();
        doc = next;
        Text = $"GTViewer - {path}";
        FillTree();
        FillSwitches();
        FrameAll();
        status.Text = $"{doc.File.Models.Count} models, {doc.File.Shapes.Count} shapes, {doc.Scene.Selectors.Count} switches, {doc.LodCount} LOD level(s){(renderer.Software ? " - software rendering" : "")}";
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
        string lod = doc.Scene.ShapeLevel.TryGetValue(n.Shape, out int l) ? $" LOD{l}" : "";
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
            for (int i = 0; i < doc.LodCount; i++) lod.Items.Add($"LOD{i}");
            lod.SelectedIndex = doc.Lod;
            lod.SelectedIndexChanged += (_, _) => { doc.Lod = lod.SelectedIndex; UpdateTreeColors(); view.Invalidate(); };
            switches.Controls.Add(lod);
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
        string mat = sh.MaterialIndex >= 0 && sh.MaterialIndex < f.Materials.Count ? f.Materials[sh.MaterialIndex].Name : "-";
        var mesh = doc.Mesh(n.Shape, out var why);
        string geo = mesh == null ? $"no mesh: {why}" : $"{mesh.IndexCount / 3} triangles";
        string path = f.ShapeNames.TryGetValue(n.Shape, out var p) ? p[0] : "";
        return $"shape {n.Shape}  material {sh.MaterialIndex} {mat}  {geo}  |  {path}";
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

    void ExportObj()
    {
        if (doc == null) return;
        using var dlg = new SaveFileDialog { Title = "Export the visible shapes to OBJ", Filter = "Wavefront OBJ|*.obj", FileName = Path.GetFileName(doc.Path) + ".obj" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            int n = ObjExport.Write(doc, dlg.FileName);
            status.Text = $"{n} shapes exported to {dlg.FileName}";
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
