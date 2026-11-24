using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EML.Core.Minecraft.Gtnh.Tools;

namespace EML.Infrastructure.Minecraft.Gtnh;

/// <summary>对固定原站元素作有界 HTML 读取；不执行脚本，不把远端 HTML 交给本地 UI。</summary>
internal static partial class GtnhFlowHtmlMedia
{
    internal static string Enrich(string payload, string html)
    {
        GtnhFlowDocumentParser.ParsePayload(payload);
        var root = JsonNode.Parse(payload)!.AsObject();
        var tree = ReadHtml(html);
        var chart = tree.All().FirstOrDefault(node => node.HasClass("fcd3-payload"))?.Ancestors()
            .FirstOrDefault(node => node.HasClass("gtnh-flowchart-d3"));
        if (chart is null) return payload;
        var icons = chart.All().Where(node => node.HasClass("fcd3-icon-source")).ToArray();
        var sources = chart.All().Where(node => node.HasClass("fcd3-language-sources")).ToArray();
        if (icons.Length == 0 && sources.Length == 0) return payload;
        var views = new JsonObject();
        foreach (var language in root["views"]!.AsObject())
        {
            var ids = language.Value!["nodes"]!.AsArray().Select(node => node!["id"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            var iconArray = new JsonArray();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            // 同语言专属图标优先，共用图标只作回退，不能借用另一语言的同名节点。
            foreach (var node in icons.OrderByDescending(node => node.Ancestors().Any(parent =>
                parent.HasClass("fcd3-language-sources") && parent.Attribute("data-lang") == language.Key)))
            {
                var owner = node.Ancestors().FirstOrDefault(parent => parent.HasClass("fcd3-language-sources"));
                if (owner is not null && owner.Attribute("data-lang") != language.Key) continue;
                var id = node.Attribute("data-node");
                var icon = ReadIcon(node);
                if (!ids.Contains(id) || icon is null || !seen.Add(id)) continue;
                iconArray.Add(new JsonObject { ["nodeId"] = id, ["icon"] = icon });
            }
            var recipes = new JsonArray();
            foreach (var source in sources.Where(node => node.Attribute("data-lang") == language.Key))
            foreach (var tip in source.All().Where(node => node.HasClass("fcd3-tooltip-source")))
            {
                var id = tip.Attribute("data-node");
                if (!ids.Contains(id)) continue;
                foreach (var recipe in tip.All().Where(node => node.HasClass("grid-recipe")).Take(16))
                {
                    var board = recipe.All().FirstOrDefault(node => node.HasClass("grid-bg-board"));
                    var title = recipe.All().FirstOrDefault(node => node.HasClass("grid-bg-title"))?.Text().Trim();
                    if (board is null || string.IsNullOrWhiteSpace(title) || title.Length > 300 ||
                        !TryPixels(board, "width", out var width) || !TryPixels(board, "height", out var height) ||
                        width is < 32 or > 2048 || height is < 32 or > 2048) continue;
                    var slots = new JsonArray();
                    foreach (var slot in board.All().Where(node => node.HasClass("grid-item-component")).Take(256))
                    {
                        if (!TryPixels(slot, "left", out var x) || !TryPixels(slot, "top", out var y) ||
                            x < 0 || y < 0 || x > width - 16 || y > height - 16) continue;
                        var variants = slot.All().Where(node => node.HasClass("nei-tt") &&
                            !node.Ancestors().TakeWhile(parent => parent != slot).Any(parent => parent.HasClass("nei-tt"))).Take(64).ToArray();
                        if (variants.Length == 0) variants = [slot];
                        var items = new JsonArray();
                        foreach (var variant in variants)
                        {
                            var data = variant.All().FirstOrDefault(node => node.HasClass("nei-data"))?.Attribute("data-json");
                            string? name = null;
                            if (data is { Length: > 0 and <= 32768 })
                            {
                                try
                                {
                                    using var record = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 16 });
                                    if (record.RootElement.TryGetProperty("name", out var value) && value.ValueKind == JsonValueKind.String) name = value.GetString();
                                }
                                catch (JsonException) { }
                            }
                            name ??= variant.All().FirstOrDefault(node => node.Tag == "a" && node.Attribute("title").Length > 0)?.Attribute("title");
                            name ??= variant.All().FirstOrDefault(node => node.Tag == "img")?.Attribute("alt");
                            if (string.IsNullOrWhiteSpace(name) || name.Length > 300) continue;
                            var annotation = string.Join(" · ", variant.All().Where(node => node.HasClass("item-annotation")).Select(node => node.Text().Trim()).Where(value => value.Length > 0));
                            if (annotation.Length > 100) continue;
                            items.Add(new JsonObject { ["name"] = name, ["annotation"] = annotation, ["icon"] = ReadIcon(variant) });
                        }
                        if (items.Count > 0) slots.Add(new JsonObject { ["x"] = x, ["y"] = y, ["items"] = items });
                    }
                    var information = new JsonArray();
                    var text = recipe.All().FirstOrDefault(node => node.HasClass("recipe-information-text"))?.Text() ?? "";
                    foreach (var line in text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Take(32))
                        if (line.Length <= 600) information.Add(line);
                    recipes.Add(new JsonObject { ["nodeId"] = id, ["title"] = title, ["width"] = width, ["height"] = height, ["slots"] = slots, ["information"] = information });
                }
            }
            views.Add(language.Key, new JsonObject { ["icons"] = iconArray, ["recipes"] = recipes });
        }
        root["gregShiftMedia"] = new JsonObject { ["version"] = 1, ["views"] = views };
        var result = root.ToJsonString();
        // 与浏览器提取路径共享同一校验边界。
        GtnhFlowDocumentParser.ParsePayload(result);
        return result;
    }

    private static JsonObject? ReadIcon(Element source)
    {
        var image = source.All().FirstOrDefault(node => node.Tag == "img");
        var sprite = source.All().FirstOrDefault(node => node.HasClass("grid-sprite"));
        var address = image?.Attribute("src");
        var frames = 1;
        var seconds = .05;
        if (image is not null)
        {
            var sheet = image.Ancestors().TakeWhile(parent => parent != source.Parent).FirstOrDefault(node => node.HasClass("grid-sprite-file"));
            if (sheet is not null)
            {
                int.TryParse(Style(sheet, "--grid-sprite-frames"), out frames);
                if (double.TryParse(Style(sheet, "--grid-sprite-speed").TrimEnd('s'), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) && frames > 0) seconds = duration / frames;
            }
        }
        else if (sprite is not null)
        {
            var match = BackgroundUrl().Match(Style(sprite, "background-image"));
            address = match.Success ? match.Groups[1].Value : null;
            var animation = Animation().Match(Style(sprite, "animation"));
            if (animation.Success && int.TryParse(animation.Groups[2].Value, out frames) && frames > 0 &&
                double.TryParse(animation.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration)) seconds = duration / frames;
            else return null;
        }
        if (!GtnhFlowMediaParser.IsAllowedIconAddress(address) || frames is < 1 or > 512 || !double.IsFinite(seconds) || seconds is < .01 or > 10) return null;
        return new JsonObject { ["address"] = address, ["frames"] = frames, ["frameSeconds"] = seconds };
    }

    private static string Style(Element node, string name)
    {
        foreach (var declaration in node.Attribute("style").Split(';'))
        {
            var separator = declaration.IndexOf(':');
            if (separator > 0 && declaration[..separator].Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) return declaration[(separator + 1)..].Trim();
        }
        return "";
    }
    private static bool TryPixels(Element node, string name, out double value)
    {
        var text = Style(node, name);
        value = 0;
        return text.EndsWith("px", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    // 小型有界标记读取器只为上述固定元素服务。script/style/comment 内容从不成为节点。
    private static Element ReadHtml(string html)
    {
        if (html.Length > 8 * 1024 * 1024) throw new InvalidDataException("流程图页面过大。");
        var root = new Element("root", null);
        var current = root;
        var count = 0;
        for (var position = 0; position < html.Length;)
        {
            var opening = html.IndexOf('<', position);
            if (opening < 0) { current.Children.Add(WebUtility.HtmlDecode(html[position..])); break; }
            if (opening > position) current.Children.Add(WebUtility.HtmlDecode(html[position..opening]));
            if (html.AsSpan(opening).StartsWith("<!--", StringComparison.Ordinal))
            { var closing = html.IndexOf("-->", opening + 4, StringComparison.Ordinal); if (closing < 0) break; position = closing + 3; continue; }
            var tagEnd = EndTag(html, opening + 1);
            if (tagEnd < 0) break;
            var tagText = html[(opening + 1)..tagEnd];
            var tagMatch = TagName().Match(tagText);
            position = tagEnd + 1;
            if (!tagMatch.Success) continue;
            var tag = tagMatch.Groups[2].Value.ToLowerInvariant();
            if (tag is "script" or "style")
            {
                var closing = FindRawClosing(html, position, tag);
                if (closing < 0) break;
                var end = EndTag(html, closing + 2 + tag.Length);
                position = end < 0 ? html.Length : end + 1;
                continue;
            }
            if (tagMatch.Groups[1].Success)
            {
                var ancestor = current.AncestorsAndSelf().FirstOrDefault(node => node.Tag == tag);
                if (ancestor?.Parent is not null) current = ancestor.Parent;
                continue;
            }
            if (++count > 100000) throw new InvalidDataException("流程图页面元素过多。");
            var node = new Element(tag, current);
            foreach (Match attribute in AttributeToken().Matches(tagText[tagMatch.Length..]))
            {
                var name = attribute.Groups[1].Value.ToLowerInvariant();
                var value = attribute.Groups[2].Success ? attribute.Groups[2].Value : attribute.Groups[3].Success ? attribute.Groups[3].Value : attribute.Groups[4].Value;
                node.Attributes.TryAdd(name, WebUtility.HtmlDecode(value));
            }
            current.Children.Add(node);
            if (!tagText.TrimEnd().EndsWith('/') && tag is not ("area" or "base" or "br" or "col" or "embed" or "hr" or "img" or "input" or "link" or "meta" or "param" or "source" or "track" or "wbr"))
            {
                if (node.Ancestors().Take(129).Count() > 128) throw new InvalidDataException("流程图页面嵌套过深。");
                current = node;
            }
        }
        return root;
    }
    private static int EndTag(string html, int start)
    {
        var quote = '\0';
        for (var i = start; i < html.Length; i++)
        {
            if (quote != '\0') { if (html[i] == quote) quote = '\0'; }
            else if (html[i] is '\'' or '"') quote = html[i];
            else if (html[i] == '>') return i;
        }
        return -1;
    }
    private static int FindRawClosing(string html, int position, string tag)
    {
        while (position < html.Length)
        {
            var closing = html.IndexOf("</" + tag, position, StringComparison.OrdinalIgnoreCase);
            if (closing < 0) return -1;
            var boundary = closing + tag.Length + 2;
            if (boundary < html.Length && (char.IsWhiteSpace(html[boundary]) || html[boundary] is '/' or '>')) return closing;
            position = boundary;
        }
        return -1;
    }
    private sealed class Element(string tag, Element? parent)
    {
        internal string Tag { get; } = tag;
        internal Element? Parent { get; } = parent;
        internal List<object> Children { get; } = [];
        internal Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal string Attribute(string name) => Attributes.GetValueOrDefault(name) ?? "";
        internal bool HasClass(string name) => Attribute("class").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains(name, StringComparer.Ordinal);
        internal IEnumerable<Element> All() { yield return this; foreach (var child in Children.OfType<Element>()) foreach (var node in child.All()) yield return node; }
        internal IEnumerable<Element> Ancestors() { for (var node = Parent; node is not null; node = node.Parent) yield return node; }
        internal IEnumerable<Element> AncestorsAndSelf() { yield return this; foreach (var node in Ancestors()) yield return node; }
        internal string Text() { var text = new StringBuilder(); AppendText(this, text); return text.ToString(); }
        private static void AppendText(Element node, StringBuilder result)
        { foreach (var child in node.Children) if (child is string text) result.Append(text); else if (child is Element element) { if (element.Tag == "br") result.Append('\n'); else AppendText(element, result); } }
    }
    [GeneratedRegex("^\\s*(/)?([a-zA-Z][a-zA-Z0-9:-]*)(?=\\s|/|$)", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex TagName();
    [GeneratedRegex("([a-zA-Z_:][a-zA-Z0-9_:.-]*)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex AttributeToken();
    [GeneratedRegex("^url\\(['\"]?([^'\"()]+)['\"]?\\)$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex BackgroundUrl();
    [GeneratedRegex("(?:^|\\s)gridSprite\\s+([0-9.]+)s\\s+steps\\(([0-9]+)\\)", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Animation();
}
