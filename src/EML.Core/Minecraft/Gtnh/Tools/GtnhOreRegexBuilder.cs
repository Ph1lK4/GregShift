using System.Text;

namespace EML.Core.Minecraft.Gtnh.Tools;

/// <summary>仅把已知矿辞名称组成精确匹配式；不把外部数据当作正则源码执行。</summary>
public static class GtnhOreRegexBuilder
{
    public const int MaximumTokens = 20000;
    public const int MaximumTokenLength = 160;

    public static string Build(IEnumerable<string> names)
    {
        var fragment = BuildFragment(names);
        return fragment.Length == 0 ? "(?!)" : "^(?:" + fragment + ")\\z";
    }

    internal static string BuildFragment(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var root = new Node();
        var count = 0;
        foreach (var name in names.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (++count > MaximumTokens) throw new InvalidDataException("矿辞数量超过工具可处理的上限。");
            if (string.IsNullOrEmpty(name) || name.Length > MaximumTokenLength || name.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-')))
                throw new InvalidDataException("矿辞名称必须为不超过 160 字符的字母、数字、下划线或连字符。");
            var node = root;
            foreach (var character in name)
            {
                if (!node.Children.TryGetValue(character, out var next)) node.Children.Add(character, next = new Node());
                node = next;
            }
            node.Terminal = true;
        }
        return count == 0 ? "" : Render(root);
    }

    private static string Render(Node node)
    {
        var branches = new List<string>();
        foreach (var pair in node.Children)
        {
            var text = new StringBuilder().Append(pair.Key);
            var child = pair.Value;
            while (!child.Terminal && child.Children.Count == 1)
            {
                var only = child.Children.First();
                text.Append(only.Key); child = only.Value;
            }
            text.Append(Render(child));
            branches.Add(text.ToString());
        }
        if (branches.Count == 0) return "";
        var expression = branches.Count == 1 ? branches[0] : "(?:" + string.Join('|', branches) + ")";
        return node.Terminal ? "(?:" + expression + ")?" : expression;
    }

    private sealed class Node
    {
        public bool Terminal { get; set; }
        public SortedDictionary<char, Node> Children { get; } = [];
    }
}

public sealed record GtnhOreRegexResult(string MachineId, string MachineName, string Expression, int CharacterLimit, int MaterialCount, bool HasGeneralRule = false)
{
    public int Length => Expression.Length;
    public bool IsOverLimit => Length > CharacterLimit;
    public string LengthLabel => $"{Length} / {CharacterLimit} 字符";
    public string Status => IsOverLimit ? "超出过滤卡长度上限，请调整或拆分处理方案；不会截断表达式。" :
        MaterialCount == 0 && !HasGeneralRule ? "无匹配物料，不接收任何矿辞。" : "可以复制到矿辞过滤卡。";
}
