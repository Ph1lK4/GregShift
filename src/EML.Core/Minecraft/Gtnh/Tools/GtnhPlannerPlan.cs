using System.Text.Json;

namespace EML.Core.Minecraft.Gtnh.Tools;

/// <summary>文件传输边界校验；详细配方兼容性仍由原站的 Zod schema 和导入器判断。</summary>
public static class GtnhPlannerPlan
{
    public const int MaximumBytes = 10 * 1024 * 1024;
    public const int MaximumEntries = 20000;

    public static string Validate(ReadOnlyMemory<byte> utf8)
    {
        if (utf8.Length == 0 || utf8.Length > MaximumBytes)
            throw new InvalidDataException("方案必须为非空 JSON，且不超过 10 MiB。");
        // 文件可带 UTF-8 BOM；不修改用户的方案数据。
        var data = utf8.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? utf8[3..] : utf8;
        try
        {
            using var document = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 48 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var number) || number != 1 ||
                !HasString(root, "id") || !HasString(root, "name"))
                throw new InvalidDataException("这不是支持的 GTNH Planner 方案（需要 schemaVersion 1、id 和 name）。");
            foreach (var key in new[] { "recipes", "nodes", "edges", "fuelProfiles" }) CheckArray(root, key, required: true);
            foreach (var key in new[] { "storages", "annotations", "pockets", "productionGroups", "tasks", "taskItems", "taskComments" }) CheckArray(root, key, required: false);
            return root.GetProperty("name").GetString()!;
        }
        catch (JsonException ex) { throw new InvalidDataException("方案不是有效的 UTF-8 JSON，或嵌套层数过多。", ex); }
    }

    private static bool HasString(JsonElement root, string name) => root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) && value.GetString()!.Length <= 4096;

    private static void CheckArray(JsonElement root, string name, bool required)
    {
        if (!root.TryGetProperty(name, out var array))
        { if (required) throw new InvalidDataException("方案缺少 " + name + "。"); return; }
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > MaximumEntries ||
            array.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.Object))
            throw new InvalidDataException("方案的 " + name + " 必须为对象数组，且不超过 20000 项。");
    }
}
