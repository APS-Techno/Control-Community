using System.Xml;

namespace APS.RecipeModel.Validation;

public static class ScriptLineMap
{
    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null
    };

    public static int CountLineBreaks(string text)
    {
        int n = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') n++;
            else if (text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n')) n++;
        }
        return n;
    }

    public static Func<int, int?> ForRcpx(string rcpxXml, int paramLineCount)
    {
        int? first = FirstScriptTextLine(rcpxXml);
        if (first is not { } firstLine) return _ => null;
        return scriptLine => scriptLine > paramLineCount ? firstLine + (scriptLine - paramLineCount - 1) : null;
    }

    private static int? FirstScriptTextLine(string xml)
    {
        int? tagLine = null, tagColumn = null;
        using (var sr = new StringReader(xml))
        using (var reader = XmlReader.Create(sr, ReaderSettings))
        {
            var info = (IXmlLineInfo)reader;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1 && reader.Name == "Script")
                {
                    if (reader.IsEmptyElement) return null;
                    tagLine = info.LineNumber;
                    tagColumn = info.LinePosition;
                    break;
                }
            }
        }
        if (tagLine is null || tagColumn is null) return null;

        int offset = OffsetOf(xml, tagLine.Value, tagColumn.Value);
        offset = EndOfStartTag(xml, offset);
        if (offset < 0) return null;

        while (offset < xml.Length)
        {
            if (char.IsWhiteSpace(xml[offset])) { offset++; continue; }
            if (string.CompareOrdinal(xml, offset, "<![CDATA[", 0, 9) == 0) { offset += 9; continue; }
            if (string.CompareOrdinal(xml, offset, "<!--", 0, 4) == 0)
            {
                int close = xml.IndexOf("-->", offset + 4, StringComparison.Ordinal);
                if (close < 0) return null;
                offset = close + 3;
                continue;
            }
            if (string.CompareOrdinal(xml, offset, "]]>", 0, 3) == 0) { offset += 3; continue; }
            break;
        }

        if (offset >= xml.Length || string.CompareOrdinal(xml, offset, "</Script", 0, 8) == 0)
            return null;

        return CountLineBreaks(xml[..offset]) + 1;
    }

    private static int OffsetOf(string text, int line, int column)
    {
        int current = 1, i = 0;
        while (current < line && i < text.Length)
        {
            if (text[i] == '\n') current++;
            else if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                current++;
            }
            i++;
        }
        return Math.Min(text.Length, i + column - 1);
    }

    private static int EndOfStartTag(string text, int from)
    {
        char quote = '\0';
        for (int i = from; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
            if (c == '"' || c == '\'') { quote = c; continue; }
            if (c == '>') return i + 1;
        }
        return -1;
    }
}
