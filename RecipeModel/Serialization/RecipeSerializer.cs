using System.Xml;
using APS.RecipeModel.Model;

namespace APS.RecipeModel.Serialization;

public static class RecipeSerializer
{
    // Recipes are plain <Recipe> XML and never legitimately contain a
    // DOCTYPE/DTD. Prohibiting DTD processing (and disabling the external
    // resolver) closes the "billion laughs" entity-expansion DoS vector.
    private static readonly XmlReaderSettings SafeReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null
    };

    public static RecipeFile Load(string filePath)
    {
        var doc = new XmlDocument();
        using var reader = XmlReader.Create(filePath, SafeReaderSettings);
        doc.Load(reader);
        return ParseDocument(doc);
    }

    public static RecipeFile LoadFromString(string xml)
    {
        var doc = new XmlDocument();
        using var sr = new StringReader(xml);
        using var reader = XmlReader.Create(sr, SafeReaderSettings);
        doc.Load(reader);
        return ParseDocument(doc);
    }

    public static void Save(RecipeFile recipe, string filePath)
    {
        var doc = BuildDocument(recipe);
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "\t",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace
        };
        using var writer = XmlWriter.Create(filePath, settings);
        doc.Save(writer);
    }

    private static RecipeFile ParseDocument(XmlDocument doc)
    {
        var recipe = new RecipeFile();
        var root = doc.DocumentElement
            ?? throw new XmlException("Missing <Recipe> root element");

        if (root.SelectSingleNode("Description") is XmlElement descEl)
        {
            recipe.Description.Type = descEl.GetAttribute("type");
            recipe.Description.Title = descEl.SelectSingleNode("Title")?.InnerText ?? "";
            recipe.Description.Author = descEl.SelectSingleNode("Author")?.InnerText ?? "";
            recipe.Description.Notes = descEl.SelectSingleNode("Notes")?.InnerText ?? "";
            if (DateTime.TryParse(descEl.SelectSingleNode("Created")?.InnerText, out var created))
                recipe.Description.Created = created;
            if (DateTime.TryParse(descEl.SelectSingleNode("Modified")?.InnerText, out var modified))
                recipe.Description.Modified = modified;
        }

        if (root.SelectSingleNode("Parameters") is XmlElement paramsEl)
        {
            foreach (XmlElement groupEl in paramsEl.SelectNodes("Group")!)
            {
                var group = new ParamGroup { Name = groupEl.GetAttribute("name") };
                foreach (XmlElement paramEl in groupEl.SelectNodes("Param")!)
                {
                    group.Params.Add(new RecipeParam
                    {
                        Name = paramEl.GetAttribute("name"),
                        Caption = paramEl.GetAttribute("caption"),
                        Unit = paramEl.GetAttribute("unit"),
                        ParamType = ParseParamType(paramEl.GetAttribute("type")),
                        Value = paramEl.InnerText.Trim()
                    });
                }
                recipe.Parameters.Add(group);
            }
        }

        if (root.SelectSingleNode("Script") is XmlElement scriptEl)
        {
            recipe.Script = scriptEl.InnerText.Trim();
        }

        return recipe;
    }

    private static XmlDocument BuildDocument(RecipeFile recipe)
    {
        var doc = new XmlDocument();
        var decl = doc.CreateXmlDeclaration("1.0", "utf-8", null);
        doc.AppendChild(decl);

        var root = doc.CreateElement("Recipe");
        doc.AppendChild(root);

        var descEl = doc.CreateElement("Description");
        descEl.SetAttribute("type", recipe.Description.Type);
        AppendTextElement(doc, descEl, "Title", recipe.Description.Title);
        AppendTextElement(doc, descEl, "Author", recipe.Description.Author);
        AppendTextElement(doc, descEl, "Created", recipe.Description.Created.ToString("yyyy-MM-dd"));
        AppendTextElement(doc, descEl, "Modified", recipe.Description.Modified.ToString("yyyy-MM-dd"));
        AppendTextElement(doc, descEl, "Notes", recipe.Description.Notes);
        root.AppendChild(descEl);

        var paramsEl = doc.CreateElement("Parameters");
        foreach (var group in recipe.Parameters)
        {
            var groupEl = doc.CreateElement("Group");
            groupEl.SetAttribute("name", group.Name);
            foreach (var p in group.Params)
            {
                var paramEl = doc.CreateElement("Param");
                paramEl.SetAttribute("name", p.Name);
                paramEl.SetAttribute("caption", p.Caption);
                paramEl.SetAttribute("unit", p.Unit);
                paramEl.SetAttribute("type", p.ParamType.ToString().ToLowerInvariant());
                paramEl.InnerText = p.Value;
                groupEl.AppendChild(paramEl);
            }
            paramsEl.AppendChild(groupEl);
        }
        root.AppendChild(paramsEl);

        var scriptEl = doc.CreateElement("Script");
        scriptEl.InnerText = recipe.Script;
        root.AppendChild(scriptEl);

        return doc;
    }

    private static void AppendTextElement(XmlDocument doc, XmlElement parent, string name, string value)
    {
        var el = doc.CreateElement(name);
        el.InnerText = value;
        parent.AppendChild(el);
    }

    private static ParamType ParseParamType(string type) => type.ToLowerInvariant() switch
    {
        "int" => ParamType.Int,
        "string" => ParamType.String,
        "bool" => ParamType.Bool,
        _ => ParamType.Float
    };
}
