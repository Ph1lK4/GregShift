using System.Text.Json;

namespace EML.Core.Minecraft.Gtnh.Tools;

public sealed record GtnhOreMaterial(string Id, string Name, string EnglishName, string ItemKey,
    string DefaultRoute, bool NeedsReview, IReadOnlyList<string> AvailableProcessing, bool RecommendedProcessing)
{
    public string Label => Name == EnglishName ? Name : $"{Name} · {EnglishName}";
}

public sealed record GtnhOreGeologyVariant(string Prefix, string Name);

/// <summary>只读取公开表格数据，不下载或执行 Wiki 工具脚本及图标。</summary>
public sealed record GtnhOreCatalog(IReadOnlyList<GtnhOreMaterial> Materials, IReadOnlyList<GtnhOreGeologyVariant> Geology)
{
    public const string DataVersion = "GTNH 2.9.0-RC-1";
    public const int MaximumDataCharacters = 10 * 1024 * 1024;
    public static readonly string[] ProcessingIds = ["electrolyzer", "centrifuge", "dehydrator"];

    public static GtnhOreCatalog Parse(string oresJson, string presetsJson, string geologyJson, string? extraOresJson = null)
    {
        var ores = ReadRows(oresJson).Concat(extraOresJson is null ? [] : ReadRows(extraOresJson)).ToArray();
        var presets = ReadRows(presetsJson);
        var geology = ReadRows(geologyJson);
        var materials = new List<GtnhOreMaterial>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in ores)
        {
            var id = Text(row, "oreDict");
            if (id.Length == 0 || !known.Add(id)) continue;
            ValidateToken(id);
            var translated = Text(row, "translatedName", "oreTranslatedName");
            var preset = presets.FirstOrDefault(item => Text(item, "oreDict") == id ||
                Text(item, "oreDict").Length == 0 && translated.Length > 0 && Text(item, "translatedName") == translated);
            var name = preset is null ? translated : Text(preset, "translatedName");
            if (name.Length == 0) name = translated.Length == 0 ? id : translated;
            var route = preset is null ? "common" : Text(preset, "preset");
            var review = preset is not null && Truth(preset, "controversial");
            if (route.Length == 0 || review) route = "common";
            if (route != "common" && !GtnhOreRoutes.All.Any(item => item.Id == route))
                throw new InvalidDataException("矿物推荐表包含暂不支持的处理路线：" + route);
            var available = preset is null ? [] : ReadProcessing(preset);
            var recommended = preset is not null && (preset.ContainsKey("recommended") ? Truth(preset, "recommended") :
                ProcessingIds.Any(op => Truth(preset, op + "Recommended")));
            materials.Add(new GtnhOreMaterial(id, name, id, Text(row, "key"), route, review, available, recommended));
        }
        if (materials.Count == 0) throw new InvalidDataException("原站矿物表没有可用的 oreDict 记录，已保留旧数据。");
        var variants = new List<GtnhOreGeologyVariant>();
        var prefixes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in geology)
        {
            var prefix = Text(row, "prefix", "oreDict");
            if (prefix.Length == 0 || !prefixes.Add(prefix)) continue;
            ValidateToken(prefix);
            var name = Text(row, "translatedName", "name");
            variants.Add(new GtnhOreGeologyVariant(prefix, name.Length == 0 ? prefix : name));
        }
        return new GtnhOreCatalog(materials.OrderBy(item => item.Name, StringComparer.CurrentCulture).ToArray(), variants);
    }

    internal static void ValidateToken(string value)
    {
        if (value.Length is 0 or > 160 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-')))
            throw new InvalidDataException("矿辞或地质前缀包含无效字符，不能生成过滤表达式。");
    }

    private static IReadOnlyList<string> ReadProcessing(Dictionary<string, JsonElement> row)
    {
        if (!row.TryGetValue("available", out var raw) || raw.ValueKind is JsonValueKind.Null)
            return ProcessingIds.Where(op => Truth(row, op + "Recommended")).ToArray();
        string[] values = raw.ValueKind == JsonValueKind.String ? (raw.GetString() ?? "").Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) :
            raw.ValueKind == JsonValueKind.Array ? raw.EnumerateArray().Select(item => item.GetString() ?? "").ToArray() :
            throw new InvalidDataException("矿物推荐表的 available 字段不是字符串或数组。");
        if (values.Any(value => value != "none" && !ProcessingIds.Contains(value, StringComparer.Ordinal)))
            throw new InvalidDataException("矿物推荐表包含暂不支持的分解方式。");
        return ProcessingIds.Where(op => values.Contains(op, StringComparer.Ordinal)).ToArray();
    }

    private static string Text(Dictionary<string, JsonElement> row, params string[] names)
    {
        foreach (var name in names)
            if (row.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                return value.GetString()!.Trim();
        return "";
    }

    private static bool Truth(Dictionary<string, JsonElement> row, string name) => row.TryGetValue(name, out var value) &&
        (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number == 1 ||
         value.ValueKind == JsonValueKind.String && value.GetString() is { } text && text.ToLowerInvariant() is "true" or "1" or "yes");

    private static List<Dictionary<string, JsonElement>> ReadRows(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumDataCharacters) throw new InvalidDataException("矿物数据为空或超过 10 MiB。");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema", out var schema) ||
                !schema.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array ||
                !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() > 10000)
                throw new InvalidDataException("原站表格格式发生变化，需要 schema.fields 和 data。");
            var names = fields.EnumerateArray().Select(field => field.GetProperty("name").GetString() ?? "").ToArray();
            if (names.Length is 0 or > 80 || names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.Ordinal).Count() != names.Length)
                throw new InvalidDataException("原站表格列名无效。");
            var rows = new List<Dictionary<string, JsonElement>>();
            foreach (var row in data.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != names.Length) throw new InvalidDataException("原站表格的行列数量不一致。");
                rows.Add(names.Select((name, index) => KeyValuePair.Create(name, row[index].Clone())).ToDictionary(StringComparer.Ordinal));
            }
            return rows;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        { throw new InvalidDataException("无法解析原站矿物表格，已保留旧数据。", ex); }
    }
}
