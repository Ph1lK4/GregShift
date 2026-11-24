using System.IO;
using System.Text.Json;

namespace EML.App.Controls;

internal sealed record GtnhPlannerSearchItem(string English, string Chinese)
{
    public string Label => Chinese == English ? English : $"{Chinese} · {English}";
}

internal static class GtnhPlannerResources
{
    private static readonly Lazy<Dictionary<string, string>> Names = new(() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(Read("names.json")) ?? []);

    internal static IReadOnlyList<GtnhPlannerSearchItem> Search(string text)
    {
        var query = text.Trim();
        if (query.Length == 0 || query.Length > 128) return [];
        return Names.Value.Where(pair => pair.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                pair.Value.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.Key.Equals(query, StringComparison.OrdinalIgnoreCase) || pair.Value == query ? 0 : 1)
            .ThenBy(pair => pair.Key.Length).ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(60).Select(pair => new GtnhPlannerSearchItem(pair.Key, pair.Value)).ToArray();
    }

    internal static string Script => Read("planner.js").Replace("/*__NAMES__*/{}", Read("names.json"), StringComparison.Ordinal)
        .Replace("/*__UI__*/{}", Read("ui.json"), StringComparison.Ordinal);

    internal static string Read(string file)
    {
        using var stream = typeof(GtnhPlannerResources).Assembly.GetManifestResourceStream("EML.App.Resources.GtnhPlanner." + file)
            ?? throw new InvalidDataException("规划器内置资源缺失：" + file);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
