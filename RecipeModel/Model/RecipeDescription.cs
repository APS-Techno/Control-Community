namespace APS.RecipeModel.Model;

public class RecipeDescription
{
    public string Title { get; set; } = "";
    public string Type { get; set; } = "";
    public string Author { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime Modified { get; set; } = DateTime.Now;
}
