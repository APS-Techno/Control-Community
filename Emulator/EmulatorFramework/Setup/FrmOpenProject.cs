using APS.Controls.Theme;
using APS.SystemEmulator.Engine;

namespace APS.SystemEmulator.Setup;

public partial class FrmOpenProject : Form
{
    private readonly ProjectLifecycleController _controller;

    public string? SelectedProject { get; private set; }
    public string? NewConfigsRoot { get; private set; }

    public bool SyncControlRequested =>
        _chkSyncControl.Checked && !string.IsNullOrEmpty(SelectedProject);

    public FrmOpenProject(ProjectLifecycleController controller)
    {
        _controller = controller;
        InitializeComponent();

        string root = controller.Settings.ConfigsRoot ?? "";
        if (string.IsNullOrEmpty(root))
        {
            string defaultRoot = DefaultConfigsRoot(AppContext.BaseDirectory);
            if (Directory.Exists(defaultRoot))
            {
                root = defaultRoot;
                // Surface the default so the caller persists it even if the user doesn't click Browse.
                NewConfigsRoot = defaultRoot;
            }
        }
        _txtConfigsRoot.Text = root;

        RepopulateProjects();
        DarkModeHelper.FixControls(this);
    }

    public static string DefaultConfigsRoot(string baseDirectory)
        => Path.GetFullPath(Path.Combine(baseDirectory, "..", "Configs"));

    private void RepopulateProjects()
    {
        _lstProjects.Items.Clear();
        string root = _txtConfigsRoot.Text;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;

        var current = _controller.Settings.LastProject;
        var dirs = Directory.EnumerateDirectories(root)
            .Where(d => File.Exists(Path.Combine(d, "HWConf.Emulator.xml")))
            .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);
        foreach (var dir in dirs)
        {
            string name = Path.GetFileName(dir)!;
            string display = name == current ? $"{name}  (current)" : name;
            _lstProjects.Items.Add(new ProjectItem(name, display));
        }
    }

    private void BtnBrowse_Click(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select Configs root folder",
            UseDescriptionForTitle = true,
            InitialDirectory = _txtConfigsRoot.Text,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _txtConfigsRoot.Text = dlg.SelectedPath;
        NewConfigsRoot = dlg.SelectedPath;
        RepopulateProjects();
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        if (_lstProjects.SelectedItem is ProjectItem pi)
            SelectedProject = pi.Name;
    }

    private void LstProjects_DoubleClick(object? sender, EventArgs e)
    {
        if (_lstProjects.SelectedItem is ProjectItem pi)
        {
            SelectedProject = pi.Name;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private sealed record ProjectItem(string Name, string Display)
    {
        public override string ToString() => Display;
    }
}
