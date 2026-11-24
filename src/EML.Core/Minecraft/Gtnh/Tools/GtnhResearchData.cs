using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EML.Core.Minecraft.Gtnh.Tools;

/// <summary>只读取声明式要素数据；不执行导入内容中的代码或远程资源。</summary>
public static class GtnhResearchData
{
    public const int MaximumBytes = 2 * 1024 * 1024;

    public static IReadOnlyList<GtnhResearchAspect> ParseWikiJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaximumBytes)
            throw new InvalidDataException("要素数据须为非空 JSON，且不能超过 2 MiB。");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 24 });
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("aspects", out var normalized)) root = normalized;
            var items = root.ValueKind == JsonValueKind.Array ? ReadObjects(root) : ReadTabular(root);
            if (items.Count is 0 or > GtnhResearchPathFinder.MaximumAspectCount)
                throw new InvalidDataException("要素数据必须包含 1～256 个要素。");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                if (!Regex.IsMatch(item.Id, "^[a-z][a-z0-9_]{0,63}$", RegexOptions.CultureInvariant) || !ids.Add(item.Id) ||
                    string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 160 || item.Name.Any(char.IsControl) ||
                    !double.IsFinite(item.Weight) || item.Weight <= 0 || item.Weight > 1_000_000_000_000 ||
                    item.Components.Count is not (0 or 2))
                    throw new InvalidDataException("要素 ID、名称、组分或权重无效。");
            }
            foreach (var item in items)
                if (item.Components.Any(component => !ids.Contains(component) || component == item.Id))
                    throw new InvalidDataException($"{item.Name} 的组分不完整或包含自身，请读取完整要素数据。");
            return items;
        }
        catch (JsonException ex) { throw new InvalidDataException("要素数据不是受支持的 JSON。", ex); }
        catch (InvalidOperationException ex) { throw new InvalidDataException("要素数据的字段类型不正确。", ex); }
        catch (KeyNotFoundException ex) { throw new InvalidDataException("要素表缺少必要字段。", ex); }
    }

    private static List<GtnhResearchAspect> ReadObjects(JsonElement array)
    {
        if (array.GetArrayLength() > GtnhResearchPathFinder.MaximumAspectCount) throw new InvalidDataException("要素数量过多。");
        var result = new List<GtnhResearchAspect>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var id) ||
                !item.TryGetProperty("name", out var name) || !item.TryGetProperty("components", out var components))
                throw new InvalidDataException("要素数据缺少 id、name 或 components。");
            var weight = item.TryGetProperty("weight", out var value) ? Number(value) : 1;
            result.Add(new(Text(id).ToLowerInvariant(), Text(name), Components(components), weight));
        }
        return result;
    }

    private static List<GtnhResearchAspect> ReadTabular(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema", out var schema) ||
            !schema.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array ||
            data.GetArrayLength() > GtnhResearchPathFinder.MaximumAspectCount)
            throw new InvalidDataException("要素表缺少 schema.fields 或 data。");
        var names = fields.EnumerateArray().Select(field => field.GetProperty("name").GetString() ?? "").ToArray();
        var idIndex = Index(names, "id", "tag", "aspect", "要素ID", "要素");
        var nameIndex = Index(names, "name", "name_zh", "zh", "名称", "中文名");
        var componentsIndex = Index(names, "components", "parents", "组分");
        var firstIndex = Index(names, "component1", "parent1", "组分1");
        var secondIndex = Index(names, "component2", "parent2", "组分2");
        var weightIndex = Index(names, "weight", "权重");
        if (idIndex < 0 || nameIndex < 0 || componentsIndex < 0 && (firstIndex < 0 || secondIndex < 0))
            throw new InvalidDataException("要素表缺少 ID、中文名称或组分字段。");
        var result = new List<GtnhResearchAspect>();
        foreach (var row in data.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != names.Length)
                throw new InvalidDataException("要素表行与字段定义不一致。");
            var components = componentsIndex >= 0 ? Components(row[componentsIndex]) :
                new[] { Text(row[firstIndex]), Text(row[secondIndex]) }.Where(text => text.Length > 0).Select(text => text.ToLowerInvariant()).ToArray();
            result.Add(new(Text(row[idIndex]).ToLowerInvariant(), Text(row[nameIndex]), components,
                weightIndex >= 0 ? Number(row[weightIndex]) : 1));
        }
        return result;
    }

    private static int Index(string[] names, params string[] options) => Array.FindIndex(names,
        name => options.Contains(name, StringComparer.OrdinalIgnoreCase));
    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.Null ? "" : value.GetString()?.Trim() ?? "";
    private static double Number(JsonElement value) => value.ValueKind == JsonValueKind.Number ? value.GetDouble() :
        double.TryParse(Text(value), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : double.NaN;
    private static string[] Components(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => [],
        JsonValueKind.Array => value.EnumerateArray().Select(Text).Where(text => text.Length > 0).Select(text => text.ToLowerInvariant()).ToArray(),
        JsonValueKind.String => Text(value).Split([',', '+', '|', '/', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(text => text.ToLowerInvariant()).ToArray(),
        _ => throw new InvalidDataException("要素组分必须为数组或字符串。")
    };
}
