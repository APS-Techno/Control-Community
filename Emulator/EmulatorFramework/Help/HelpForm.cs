namespace APS.SystemEmulator.Help;

public class HelpForm : Form
{
    private readonly TreeView _tree;
    private readonly WebBrowser _browser;
    private readonly string _helpDir;
    private readonly IReadOnlyList<string> _extraFolders;
    private HelpCatalog _catalog = HelpCatalog.Load(string.Empty, []);

    public HelpForm(IReadOnlyList<string> extraFolders)
    {
        _extraFolders = extraFolders;
        Text = "APS System Emulator \u2014 Help";
        ClientSize = new Size(1200, 700);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimumSize = new Size(600, 400);

        var iconPath = Path.Combine(AppContext.BaseDirectory, "EmulatorIcon.ico");
        if (File.Exists(iconPath)) Icon = new Icon(iconPath);

        _helpDir = Path.Combine(AppContext.BaseDirectory, "Help");

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            Panel1MinSize = 250,
        };
        Controls.Add(split);
        Load += (_, _) => split.SplitterDistance = 240;

        _tree = new TreeView
        {
            Dock = DockStyle.Fill,
            HideSelection = false,
            FullRowSelect = true,
            ShowLines = true,
            ShowPlusMinus = true,
            Font = new Font("Segoe UI", 9.75f),
            Indent = 16,
            Scrollable = true,
        };
        _tree.AfterSelect += Tree_AfterSelect;
        split.Panel1.Controls.Add(new APS.Controls.Theme.DarkScrollHost(_tree) { Dock = DockStyle.Fill });

        _browser = new WebBrowser
        {
            Dock = DockStyle.Fill,
            AllowNavigation = true,
            IsWebBrowserContextMenuEnabled = false,
            AllowWebBrowserDrop = false,
            ScriptErrorsSuppressed = true,
        };
        _browser.Navigating += Browser_Navigating;
        split.Panel2.Controls.Add(_browser);

        ApplyDarkTheme(split);
        LoadIndex();
    }

    private void ApplyDarkTheme(SplitContainer split)
    {
        var bgDark = Color.FromArgb(13, 27, 42);
        var bgPanel = Color.FromArgb(22, 32, 50);
        var textPrimary = Color.FromArgb(200, 214, 229);
        var accent = Color.FromArgb(72, 169, 254);

        BackColor = bgDark;
        split.BackColor = bgDark;
        split.Panel1.BackColor = bgPanel;
        split.Panel2.BackColor = bgDark;

        _tree.BackColor = bgPanel;
        _tree.ForeColor = textPrimary;
        _tree.BorderStyle = BorderStyle.None;
        _tree.LineColor = Color.FromArgb(40, 60, 90);
    }

    private void LoadIndex()
    {
        var indexPath = Path.Combine(_helpDir, "help-index.json");
        if (!File.Exists(indexPath))
        {
            _browser.DocumentText = "<html><body style='background:#0D1B2A;color:#C8D6E5;font-family:Segoe UI'>"
                + "<h2>Help files not found</h2>"
                + $"<p>Expected help index at:<br><code>{indexPath}</code></p>"
                + "</body></html>";
            return;
        }

        _catalog = HelpCatalog.Load(_helpDir, _extraFolders);

        _tree.BeginUpdate();
        TreeNode? firstLeaf = null;
        foreach (var topic in _catalog.Topics)
        {
            var node = CreateNode(topic, ref firstLeaf);
            _tree.Nodes.Add(node);
        }
        _tree.ExpandAll();
        _tree.EndUpdate();

        if (firstLeaf != null)
            _tree.SelectedNode = firstLeaf;
    }

    private TreeNode CreateNode(HelpTopic topic, ref TreeNode? firstLeaf)
    {
        var node = new TreeNode(topic.Title) { Tag = topic.File };

        if (topic.Children != null)
        {
            foreach (var child in topic.Children)
                node.Nodes.Add(CreateNode(child, ref firstLeaf));
        }

        if (topic.File != null && firstLeaf == null)
            firstLeaf = node;

        return node;
    }

    private void Tree_AfterSelect(object? sender, TreeViewEventArgs e)
    {
        if (e.Node?.Tag is string file && !string.IsNullOrEmpty(file))
        {
            var path = _catalog.ResolveTopic(file);
            if (path != null)
                _browser.Navigate(new Uri(path));
            else
                _browser.DocumentText = $"<html><body style='background:#0D1B2A;color:#C8D6E5;font-family:Segoe UI'>"
                    + $"<h2>Topic not found</h2><p><code>{Path.Combine(_helpDir, "topics", file)}</code></p></body></html>";
        }
    }

    private void Browser_Navigating(object? sender, WebBrowserNavigatingEventArgs e)
    {
        if (e.Url is null) return;
        if (e.Url.Scheme == "file" || e.Url.Scheme == "about") return;
        e.Cancel = true;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Url.AbsoluteUri) { UseShellExecute = true }); }
        catch { }
    }

    public void NavigateTo(string topicFile)
    {
        var target = FindNode(_tree.Nodes, topicFile);
        if (target != null)
            _tree.SelectedNode = target;
    }

    private static TreeNode? FindNode(TreeNodeCollection nodes, string file)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.Tag is string f && f == file) return node;
            var child = FindNode(node.Nodes, file);
            if (child != null) return child;
        }
        return null;
    }
}
