using System.Text.Json;

namespace APS.SystemEmulator.Help;

public sealed class HelpTopic
{
    public string Title { get; set; } = "";
    public string? File { get; set; }
    public List<HelpTopic>? Children { get; set; }
}

public sealed class HelpCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly List<string> _topicFolders;

    public IReadOnlyList<HelpTopic> Topics { get; }

    private HelpCatalog(List<HelpTopic> topics, List<string> topicFolders)
    {
        Topics = topics;
        _topicFolders = topicFolders;
    }

    public static HelpCatalog Load(string helpDir, IReadOnlyList<string> extraFolders)
    {
        var topics = new List<HelpTopic>();
        var folders = new List<string>();
        foreach (var dir in new[] { helpDir }.Concat(extraFolders))
        {
            var index = ReadIndex(Path.Combine(dir, "help-index.json"));
            if (index == null) continue;
            folders.Add(Path.Combine(dir, "topics"));
            Merge(topics, index);
        }
        return new HelpCatalog(topics, folders);
    }

    public string? ResolveTopic(string file)
        => Enumerable.Reverse(_topicFolders).Select(f => Path.Combine(f, file)).FirstOrDefault(File.Exists);

    private static List<HelpTopic>? ReadIndex(string indexPath)
    {
        if (!File.Exists(indexPath)) return null;
        return JsonSerializer.Deserialize<HelpIndex>(File.ReadAllText(indexPath), JsonOptions)?.Topics;
    }

    private static void Merge(List<HelpTopic> target, List<HelpTopic> source)
    {
        foreach (var topic in source)
        {
            var existing = topic.File == null
                ? target.Find(t => t.File == null && string.Equals(t.Title, topic.Title, StringComparison.OrdinalIgnoreCase))
                : null;
            if (existing == null)
            {
                if (topic.File == null || !TryReplace(target, topic)) target.Add(topic);
                continue;
            }
            existing.Children ??= [];
            Merge(existing.Children, topic.Children ?? []);
        }
    }

    private static bool TryReplace(List<HelpTopic> topics, HelpTopic replacement)
    {
        for (int i = 0; i < topics.Count; i++)
        {
            if (string.Equals(topics[i].File, replacement.File, StringComparison.OrdinalIgnoreCase))
            {
                topics[i] = replacement;
                return true;
            }
            if (topics[i].Children != null && TryReplace(topics[i].Children!, replacement)) return true;
        }
        return false;
    }

    private sealed class HelpIndex
    {
        public List<HelpTopic>? Topics { get; set; }
    }
}
