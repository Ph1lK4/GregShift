using System.Text.Json.Serialization;

namespace EML.Core.Minecraft.Gtnh.Tools;

/// <summary>从公开流程图解析的声明式数据；不包含或执行远端程序。</summary>
public sealed record GtnhFlowDocument(IReadOnlyList<GtnhFlowView> Views);

public sealed record GtnhFlowView(string Language, string Title,
    IReadOnlyList<GtnhFlowNode> Nodes, IReadOnlyList<GtnhFlowEdge> Edges, IReadOnlyList<GtnhFlowGroup> Groups);

public sealed record GtnhFlowNode(string Id, string Label, string Kind, double X, double Y,
    GtnhFlowIcon? Icon = null, IReadOnlyList<GtnhFlowRecipe>? Recipes = null);
public sealed record GtnhFlowIcon(string Address, int Frames = 1, double FrameSeconds = .05);
public sealed record GtnhFlowRecipe(string Title, double Width, double Height,
    IReadOnlyList<GtnhFlowRecipeSlot> Slots, IReadOnlyList<string> Information);
public sealed record GtnhFlowRecipeSlot(double X, double Y, IReadOnlyList<GtnhFlowRecipeItem> Items);
public sealed record GtnhFlowRecipeItem(string Name, string Annotation, GtnhFlowIcon? Icon);
public sealed record GtnhFlowEdge(string Id, string Source, string Target, string Kind, double Curveness);
public sealed record GtnhFlowGroup(string Id, string Label, IReadOnlyList<string> Nodes, IReadOnlyList<string> Edges);
public sealed record GtnhFlowPageData(string PayloadJson, long? RevisionId);

/// <summary>缓存的来源与许可不与启动器代码的 GPL 许可证混淆。</summary>
public sealed record GtnhFlowChartContent(GtnhFlowDocument Document, string PayloadJson,
    string SourceAddress, long? RevisionId, DateTimeOffset RetrievedAt, bool FromCache, string? Warning = null)
{
    [JsonIgnore] public IReadOnlyDictionary<string, byte[]> IconData { get; init; } = new Dictionary<string, byte[]>();
    public bool HasMedia { get; init; }
    public const string LicenseName = "CC BY-NC-SA 3.0（原站中文原创内容；其他来源遵循各自许可）";
    public const string LicenseAddress = "https://creativecommons.org/licenses/by-nc-sa/3.0/";
    public string Attribution => "图表数据：GTNH 中文维基贡献者；绘制：GregShift。";
    public string HistoryAddress => "https://gtnh.huijiwiki.com/index.php?title=" +
        Uri.EscapeDataString(Uri.UnescapeDataString(new Uri(SourceAddress).AbsolutePath[6..])) + "&action=history";
}

/// <summary>原站拒绝直接读取时，交由正常浏览器页面读取，并保留已验证的缓存。</summary>
public sealed class GtnhFlowBrowserRequiredException : IOException
{
    public GtnhFlowChartContent? CachedContent { get; }

    public GtnhFlowBrowserRequiredException(GtnhFlowChartContent? cachedContent, Exception innerException)
        : base("原站拒绝直接读取流程图，请通过正常浏览器页面读取。", innerException)
    {
        CachedContent = cachedContent;
    }
}

public interface IGtnhFlowChartService
{
    Task<GtnhFlowChartContent> LoadAsync(GtnhFlowChart chart, bool refresh = false, CancellationToken cancellationToken = default);
    Task<GtnhFlowChartContent> ImportPageDataAsync(GtnhFlowChart chart, string payloadJson, long? revisionId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("此图表服务不支持从原站页面读取数据。");
}
