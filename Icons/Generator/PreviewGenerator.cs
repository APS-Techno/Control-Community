using System.Text;

namespace APS.Icons.Generator;

public static class PreviewGenerator
{
    public static void WritePreview(string svgDir, string outFile)
    {
        var names = Directory.GetFiles(svgDir, "*.svg")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'>");
        sb.AppendLine("<title>APS.Icons preview</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{background:#1e1e1e;color:#e0e0e0;font-family:Segoe UI,sans-serif;padding:24px}");
        sb.AppendLine(".row{display:flex;align-items:center;gap:16px;padding:8px;border-bottom:1px solid #333}");
        sb.AppendLine(".name{width:200px;font-family:Consolas,monospace;color:#8aa}");
        sb.AppendLine(".sizes{display:flex;gap:24px;align-items:center}");
        sb.AppendLine(".box{display:flex;flex-direction:column;align-items:center;gap:4px}");
        sb.AppendLine(".box small{color:#666;font-size:10px}");
        sb.AppendLine("h1{font-weight:300;color:#8cf}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine($"<h1>APS.Icons — {names.Count} icons</h1>");

        foreach (var n in names)
        {
            sb.AppendLine("<div class='row'>");
            sb.AppendLine($"  <div class='name'>{n}</div>");
            sb.AppendLine("  <div class='sizes'>");
            foreach (var s in Program.Sizes)
                sb.AppendLine($"    <div class='box'><img src='png/{s}/{n}.png' width='{s}' height='{s}'/><small>{s}</small></div>");
            sb.AppendLine("  </div>");
            sb.AppendLine("</div>");
        }
        sb.AppendLine("</body></html>");

        File.WriteAllText(outFile, sb.ToString());
    }
}
