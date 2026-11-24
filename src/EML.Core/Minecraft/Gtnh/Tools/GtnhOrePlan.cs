using System.Text.Json;

namespace EML.Core.Minecraft.Gtnh.Tools;

public sealed record GtnhOrePlan
{
    public int SchemaVersion { get; init; } = 1;
    public string DataVersion { get; init; } = GtnhOreCatalog.DataVersion;
    public string CommonRouteId { get; init; } = "MPMC";
    public bool IntegratedFactory { get; init; }
    public int CharacterLimit { get; init; } = 1024;
    public Dictionary<string, string> Routes { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Processing { get; init; } = new(StringComparer.Ordinal);
    public string[] GeologyPrefixes { get; init; } = [];

    public static GtnhOrePlan Defaults(GtnhOreCatalog catalog) => new()
    {
        Routes = catalog.Materials.Where(ore => ore.DefaultRoute != "common").ToDictionary(ore => ore.Id, ore => ore.DefaultRoute, StringComparer.Ordinal),
        Processing = catalog.Materials.Where(ore => ore.RecommendedProcessing && ore.AvailableProcessing.Count > 0)
            .ToDictionary(ore => ore.Id, ore => ore.AvailableProcessing[0], StringComparer.Ordinal),
        GeologyPrefixes = catalog.Geology.Select(item => item.Prefix).ToArray()
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static GtnhOrePlan FromJson(string json, GtnhOreCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 1024 * 1024) throw new InvalidDataException("处理方案为空或超过 1 MiB。");
        try
        {
            var plan = JsonSerializer.Deserialize<GtnhOrePlan>(json, JsonOptions) ?? throw new InvalidDataException("处理方案为空。");
            plan.Validate(catalog);
            return plan;
        }
        catch (JsonException ex) { throw new InvalidDataException("处理方案不是有效的 JSON。", ex); }
    }

    public void Validate(GtnhOreCatalog catalog)
    {
        if (SchemaVersion != 1 || DataVersion != GtnhOreCatalog.DataVersion || !GtnhOreRoutes.Common.Any(route => route.Id == CommonRouteId) ||
            CharacterLimit is not (256 or 1024) || Routes is null || Processing is null || GeologyPrefixes is null ||
            Routes.Count > catalog.Materials.Count || Processing.Count > catalog.Materials.Count || GeologyPrefixes.Length > catalog.Geology.Count)
            throw new InvalidDataException("处理方案版本或设置不受支持。请核对矿物数据版本。");
        var materials = catalog.Materials.ToDictionary(ore => ore.Id, StringComparer.Ordinal);
        foreach (var (ore, route) in Routes)
            if (!materials.ContainsKey(ore) || !GtnhOreRoutes.All.Any(item => item.Id == route)) throw new InvalidDataException("处理方案引用了未知矿物或路线。");
        foreach (var (ore, op) in Processing)
            if (!materials.TryGetValue(ore, out var material) || !material.AvailableProcessing.Contains(op, StringComparer.Ordinal))
                throw new InvalidDataException("处理方案选择了该矿物不支持的分解方式。");
        if (GeologyPrefixes.Distinct(StringComparer.Ordinal).Count() != GeologyPrefixes.Length ||
            GeologyPrefixes.Any(prefix => !catalog.Geology.Any(variant => variant.Prefix == prefix)))
            throw new InvalidDataException("处理方案包含未知或重复的地质变种。");
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, MaxDepth = 12 };
}
