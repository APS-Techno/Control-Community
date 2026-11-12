namespace APS.RecipeModel.Blocks;

public interface IDeleteConfirmer
{
    /// <summary>Called only for containers with <c>childCount &gt; 0</c>.</summary>
    bool ConfirmDeleteContainer(string keyword, int childCount);
}

public sealed class StubDeleteConfirmer : IDeleteConfirmer
{
    private readonly bool _answer;
    public StubDeleteConfirmer(bool answer) => _answer = answer;
    public bool ConfirmDeleteContainer(string keyword, int childCount) => _answer;
}

public sealed class MessageBoxDeleteConfirmer : IDeleteConfirmer
{
    public bool ConfirmDeleteContainer(string keyword, int childCount)
    {
        var result = APS.Controls.Theme.ThemedMessageBox.Show(
            $"Delete this {keyword} and its {childCount} block{(childCount == 1 ? "" : "s")}?",
            "Delete block",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);
        return result == DialogResult.OK;
    }
}
