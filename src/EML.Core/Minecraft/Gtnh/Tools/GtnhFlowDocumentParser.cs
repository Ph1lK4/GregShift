using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EML.Core.Minecraft.Gtnh.Tools;

/// <summary>只解析原站公开的声明式图表数据；HTML、Lua 和 JavaScript 从不作为程序执行。</summary>
public static partial class GtnhFlowDocumentParser
{
    public const int MaximumPayloadCharacters = 2 * 1024 * 1024;
    private const int MaximumHtmlCharacters = 8 * 1024 * 1024;
    private const int MaximumNodes = 2048;
    private const int MaximumEdges = 8192;
    private const int MaximumGroups = 128;

    /// <summary>仅提取固定 payload 元素；多份不一致的内容拒绝使用。</summary>
    public static string ExtractPayload(string html)
    {
        if (html is null || html.Length > MaximumHtmlCharacters)
            throw Invalid("原站流程图页面过大，已停止解析。");
        try
        {
            var matches = PayloadElement().Matches(ExcludeNonElementContexts(html));
            if (matches.Count != 1) throw Invalid("原站尚未提供可识别的流程图数据，请在原站查看或稍后更新。");
            var value = WebUtility.HtmlDecode(matches[0].Groups["payload"].Value);
            if (value.Length > MaximumPayloadCharacters) throw Invalid("流程图数据过大，已停止解析。");
            return value;
        }
        catch (RegexMatchTimeoutException ex) { throw Invalid("原站流程图页面结构过于复杂，已停止解析。", ex); }
    }

    /// <summary>线性排除注释与 script/style 原始文本；其中的 span 字面量不是 DOM 图表元素。</summary>
    private static string ExcludeNonElementContexts(string html)
    {
        var result = new StringBuilder(html.Length);
        var position = 0;
        while (position < html.Length)
        {
            var opening = html.IndexOf('<', position);
            if (opening < 0) { result.Append(html, position, html.Length - position); break; }
            result.Append(html, position, opening - position);
            if (html.AsSpan(opening).StartsWith("<!--", StringComparison.Ordinal))
            {
                var end = html.IndexOf("-->", opening + 4, StringComparison.Ordinal);
                if (end < 0) throw Invalid("原站流程图页面包含未闭合的注释，已停止解析。");
                result.Append(' '); position = end + 3; continue;
            }
            var first = opening + 1;
            if (first >= html.Length || !(char.IsAsciiLetter(html[first]) || html[first] is '!' or '?' ||
                html[first] == '/' && first + 1 < html.Length && char.IsAsciiLetter(html[first + 1])))
            { result.Append('<'); position = first; continue; }
            var tagEnd = FindTagEnd(html, first);
            if (tagEnd < 0) throw Invalid("原站流程图页面包含未闭合的标签，已停止解析。");
            var rawName = IsTagName(html, first, "script") ? "script" : IsTagName(html, first, "style") ? "style" : null;
            if (rawName is not null)
            {
                var closing = FindRawTextEnd(html, tagEnd + 1, rawName);
                if (closing < 0) throw Invalid("原站流程图页面包含未闭合的 " + rawName + " 元素，已停止解析。");
                result.Append(' '); position = closing + 1; continue;
            }
            result.Append('<');
            // 标签属性中的 '<' 也不是新元素；屏蔽它，避免正则识别属性字符串里的假 span。
            for (var index = first; index <= tagEnd; index++) result.Append(html[index] == '<' ? ' ' : html[index]);
            position = tagEnd + 1;
        }
        return result.ToString();
    }

    private static int FindTagEnd(string html, int start)
    {
        var quote = '\0';
        for (var index = start; index < html.Length; index++)
        {
            var character = html[index];
            if (quote != '\0') { if (character == quote) quote = '\0'; }
            else if (character is '\'' or '"') quote = character;
            else if (character == '>') return index;
        }
        return -1;
    }

    private static bool IsTagName(string html, int start, string name) =>
        start + name.Length <= html.Length && html.AsSpan(start, name.Length).Equals(name, StringComparison.OrdinalIgnoreCase) &&
        (start + name.Length == html.Length || IsTagBoundary(html[start + name.Length]));

    private static bool IsTagBoundary(char character) => character is ' ' or '\t' or '\r' or '\n' or '\f' or '/' or '>';

    private static int FindRawTextEnd(string html, int start, string name)
    {
        var prefix = "</" + name;
        for (var position = start; position < html.Length;)
        {
            var closing = html.IndexOf(prefix, position, StringComparison.OrdinalIgnoreCase);
            if (closing < 0) return -1;
            if (IsTagName(html, closing + 2, name)) return FindTagEnd(html, closing + prefix.Length);
            position = closing + prefix.Length;
        }
        return -1;
    }

    public static long? ReadRevisionId(string html)
    {
        if (html is null || html.Length > MaximumHtmlCharacters) return null;
        try
        {
            var match = Revision().Match(html);
            return match.Success && long.TryParse(match.Groups[1].Value, out var id) && id > 0 ? id : null;
        }
        catch (RegexMatchTimeoutException) { return null; }
    }

    /// <summary>有界读取语言视图，并验证全部节点、边和分组引用，避免绘出不完整的图。</summary>
    public static GtnhFlowDocument ParsePayload(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumPayloadCharacters)
            throw Invalid("流程图数据为空或超过大小限制。");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            RejectDuplicateProperties(root);
            var views = Required(root, "views", JsonValueKind.Object);
            if (views.EnumerateObject().Count() is < 1 or > 4) throw Invalid("流程图语言视图数量不受支持。");
            var result = new List<GtnhFlowView>();
            foreach (var property in views.EnumerateObject())
            {
                var language = property.Name;
                if (language.Length is < 2 or > 12 || language.Any(c => !char.IsAsciiLetter(c) && c != '-'))
                    throw Invalid("流程图包含无效语言标识。");
                var view = property.Value;
                if (!view.TryGetProperty("schemaVersion", out var schema) || !schema.TryGetInt32(out var version) || version != 1)
                    throw Invalid("原站流程图数据版本暂不受支持。");
                if (view.TryGetProperty("lang", out var lang) && (lang.ValueKind != JsonValueKind.String || lang.GetString() != language))
                    throw Invalid("流程图语言标识不一致。");
                var nodesElement = Required(view, "nodes", JsonValueKind.Array);
                var edgesElement = Required(view, "edges", JsonValueKind.Array);
                var groupsElement = Required(view, "groups", JsonValueKind.Array);
                if (nodesElement.GetArrayLength() is < 1 or > MaximumNodes || edgesElement.GetArrayLength() > MaximumEdges ||
                    groupsElement.GetArrayLength() > MaximumGroups)
                    throw Invalid("流程图元素数量超过支持范围。");

                var nodeIds = new HashSet<string>(StringComparer.Ordinal);
                var nodes = new List<GtnhFlowNode>();
                foreach (var item in nodesElement.EnumerateArray())
                {
                    var id = Text(item, "id", 200);
                    if (!nodeIds.Add(id)) throw Invalid("流程图包含重复节点。");
                    nodes.Add(new(id, Text(item, "label", 2048, multiline: true), Text(item, "class", 80),
                        Coordinate(item, "x"), Coordinate(item, "y")));
                }
                var edgeIds = new HashSet<string>(StringComparer.Ordinal);
                var edges = new List<GtnhFlowEdge>();
                foreach (var item in edgesElement.EnumerateArray())
                {
                    var id = Text(item, "id", 200);
                    var source = Text(item, "source", 200); var target = Text(item, "target", 200);
                    if (!edgeIds.Add(id) || !nodeIds.Contains(source) || !nodeIds.Contains(target))
                        throw Invalid("流程图连线重复或引用了不存在的节点。");
                    var curve = item.TryGetProperty("curveness", out var curveValue) ? Number(curveValue, 10) : 0;
                    edges.Add(new(id, source, target, Text(item, "class", 80), curve));
                }
                var groupIds = new HashSet<string>(StringComparer.Ordinal);
                var groups = new List<GtnhFlowGroup>();
                foreach (var item in groupsElement.EnumerateArray())
                {
                    var id = Text(item, "id", 200);
                    if (!groupIds.Add(id)) throw Invalid("流程图包含重复分组。");
                    groups.Add(new(id, Text(item, "label", 300), References(item, "nodes", nodeIds, MaximumNodes),
                        References(item, "edges", edgeIds, MaximumEdges)));
                }
                result.Add(new(language, Text(view, "title", 300), nodes, edges, groups));
            }
            if (!result.Any(view => view.Language == "zh")) throw Invalid("原站暂未提供中文流程图数据。");
            return GtnhFlowMediaParser.Apply(root, new(result));
        }
        catch (JsonException ex) { throw Invalid("原站流程图数据格式无效，未载入不完整的图表。", ex); }
        catch (InvalidOperationException ex) { throw Invalid("原站流程图数据结构无效。", ex); }
        catch (FormatException ex) { throw Invalid("原站流程图数据格式无效。", ex); }
    }

    private static IReadOnlyList<string> References(JsonElement item, string name, HashSet<string> valid, int limit)
    {
        var array = Required(item, name, JsonValueKind.Array);
        if (array.GetArrayLength() > limit) throw Invalid("流程图分组过大。");
        var result = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String || element.GetString() is not { } id || !valid.Contains(id))
                throw Invalid("流程图分组引用了不存在的元素。");
            if (seen.Add(id)) result.Add(id);
        }
        return result;
    }

    private static JsonElement Required(JsonElement item, string name, JsonValueKind kind)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var value) || value.ValueKind != kind)
            throw Invalid("流程图缺少必要字段：" + name + "。");
        return value;
    }
    private static string Text(JsonElement item, string name, int limit, bool multiline = false)
    {
        var value = Required(item, name, JsonValueKind.String).GetString()!;
        if (string.IsNullOrWhiteSpace(value) || value.Length > limit ||
            value.Any(c => char.IsControl(c) && !(multiline && c is '\n' or '\r' or '\t')))
            throw Invalid("流程图文本字段无效：" + name + "。");
        return value;
    }
    private static double Coordinate(JsonElement item, string name) => Number(Required(item, name, JsonValueKind.Number), 1000);
    private static double Number(JsonElement value, double limit)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number) || Math.Abs(number) > limit)
            throw Invalid("流程图包含无效坐标或曲率。");
        return number;
    }
    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Invalid("流程图包含重复 JSON 字段。");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicateProperties(child);
    }
    private static InvalidDataException Invalid(string message, Exception? inner = null) => new(message, inner);

    [GeneratedRegex("<span\\b(?=[^>]{0,4096}\\sclass\\s*=\\s*[\"'][^\"']*(?<![\\w-])fcd3-payload(?![\\w-])[^\"']*[\"'])[^>]{0,4096}>(?<payload>[\\s\\S]*?)</span\\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex PayloadElement();
    [GeneratedRegex("[\"']wgRevisionId[\"']\\s*:\\s*([0-9]{1,18})(?=\\s*[,}])", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Revision();
}
