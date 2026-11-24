using System.Text.Json;

namespace EML.Core.Minecraft.Gtnh.Tools;

/// <summary>只接受固定 CDN 上有界的 PNG 与声明式配方，不接受 HTML、CSS 或执行代码。</summary>
public static class GtnhFlowMediaParser
{
    public static bool IsAllowedIconAddress(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
        uri.Host is "huiji-public.huijistatic.com" or "huiji-thumb.huijistatic.com" &&
        uri.AbsolutePath.StartsWith("/gtnh/uploads/", StringComparison.Ordinal) &&
        uri.AbsolutePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && value.Length <= 2048 &&
        !value.Any(char.IsControl) && !Uri.UnescapeDataString(uri.AbsolutePath).Split('/').Any(part => part is "." or "..");

    internal static GtnhFlowDocument Apply(JsonElement root, GtnhFlowDocument document)
    {
        if (!root.TryGetProperty("gregShiftMedia", out var media)) return document;
        if (media.ValueKind != JsonValueKind.Object || !media.TryGetProperty("version", out var version) ||
            !version.TryGetInt32(out var number) || number != 1) throw Invalid();
        var sourceViews = Required(media, "views", JsonValueKind.Object);
        if (sourceViews.EnumerateObject().Count() > 4) throw Invalid();
        var views = document.Views.ToDictionary(view => view.Language, StringComparer.Ordinal);
        var output = new Dictionary<string, GtnhFlowView>(views, StringComparer.Ordinal);
        var totalItems = 0;
        foreach (var property in sourceViews.EnumerateObject())
        {
            if (!views.TryGetValue(property.Name, out var view)) throw Invalid();
            var nodes = view.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
            var icons = Required(property.Value, "icons", JsonValueKind.Array);
            var recipes = Required(property.Value, "recipes", JsonValueKind.Array);
            if (icons.GetArrayLength() > 2048 || recipes.GetArrayLength() > 2048) throw Invalid();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in icons.EnumerateArray())
            {
                var id = Text(element, "nodeId", 200);
                if (!nodes.TryGetValue(id, out var node) || !seen.Add(id)) throw Invalid();
                nodes[id] = node with { Icon = Icon(Required(element, "icon", JsonValueKind.Object)) };
            }
            var recipeMap = new Dictionary<string, List<GtnhFlowRecipe>>(StringComparer.Ordinal);
            foreach (var element in recipes.EnumerateArray())
            {
                var id = Text(element, "nodeId", 200);
                if (!nodes.TryGetValue(id, out var node) || node.Kind != "recipe") throw Invalid();
                if (!recipeMap.TryGetValue(id, out var list)) recipeMap.Add(id, list = []);
                if (list.Count >= 16) throw Invalid();
                var width = Number(element, "width", 32, 2048);
                var height = Number(element, "height", 32, 2048);
                var slotsElement = Required(element, "slots", JsonValueKind.Array);
                if (slotsElement.GetArrayLength() > 256) throw Invalid();
                var slots = new List<GtnhFlowRecipeSlot>();
                foreach (var slot in slotsElement.EnumerateArray())
                {
                    var x = Number(slot, "x", 0, width - 16);
                    var y = Number(slot, "y", 0, height - 16);
                    var itemsElement = Required(slot, "items", JsonValueKind.Array);
                    if (itemsElement.GetArrayLength() is < 1 or > 64 || (totalItems += itemsElement.GetArrayLength()) > 16384) throw Invalid();
                    var items = new List<GtnhFlowRecipeItem>();
                    foreach (var item in itemsElement.EnumerateArray())
                    {
                        var icon = item.TryGetProperty("icon", out var iconElement) && iconElement.ValueKind != JsonValueKind.Null ? Icon(iconElement) : null;
                        items.Add(new(Text(item, "name", 300), Text(item, "annotation", 100, true), icon));
                    }
                    slots.Add(new(x, y, items));
                }
                var informationElement = Required(element, "information", JsonValueKind.Array);
                if (informationElement.GetArrayLength() > 32) throw Invalid();
                var information = informationElement.EnumerateArray().Select(value => String(value, 600, false)).ToArray();
                list.Add(new(Text(element, "title", 300), width, height, slots, information));
            }
            foreach (var pair in recipeMap) nodes[pair.Key] = nodes[pair.Key] with { Recipes = pair.Value };
            output[property.Name] = view with { Nodes = view.Nodes.Select(node => nodes[node.Id]).ToArray() };
        }
        return document with { Views = document.Views.Select(view => output[view.Language]).ToArray() };
    }

    public static bool HasMedia(string json)
    {
        using var root = JsonDocument.Parse(json);
        return root.RootElement.TryGetProperty("gregShiftMedia", out _);
    }

    public static IEnumerable<GtnhFlowIcon> Icons(GtnhFlowDocument document) => document.Views.SelectMany(view => view.Nodes)
        .SelectMany(node => (node.Icon is null ? Enumerable.Empty<GtnhFlowIcon>() : [node.Icon])
            .Concat(node.Recipes?.SelectMany(recipe => recipe.Slots).SelectMany(slot => slot.Items)
                .Where(item => item.Icon is not null).Select(item => item.Icon!) ?? []));

    private static GtnhFlowIcon Icon(JsonElement element)
    {
        var address = Text(element, "address", 2048);
        if (!IsAllowedIconAddress(address)) throw Invalid();
        var framesElement = Required(element, "frames", JsonValueKind.Number);
        if (!framesElement.TryGetInt32(out var frames) || frames is < 1 or > 512) throw Invalid();
        return new(address, frames, Number(element, "frameSeconds", .01, 10));
    }
    private static JsonElement Required(JsonElement item, string name, JsonValueKind kind) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == kind ? value : throw Invalid();
    private static string Text(JsonElement item, string name, int maximum, bool empty = false) => String(Required(item, name, JsonValueKind.String), maximum, empty);
    private static string String(JsonElement value, int maximum, bool empty)
    {
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text || text.Length > maximum ||
            (!empty && string.IsNullOrWhiteSpace(text)) || text.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t')) throw Invalid();
        return text;
    }
    private static double Number(JsonElement item, string name, double minimum, double maximum)
    {
        var value = Required(item, name, JsonValueKind.Number);
        if (!value.TryGetDouble(out var number) || !double.IsFinite(number) || number < minimum || number > maximum) throw Invalid();
        return number;
    }
    private static InvalidDataException Invalid() => new("原站流程图图标或配方数据无效，未接受不安全的资源或不完整的配方。");
}
