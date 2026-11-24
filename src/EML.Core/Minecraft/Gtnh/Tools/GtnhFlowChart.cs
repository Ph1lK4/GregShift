namespace EML.Core.Minecraft.Gtnh.Tools;

/// <summary>目录只记录页面元数据；图表与交互由原站点提供，不复制其正文或脚本。</summary>
public sealed record GtnhFlowChart(string Id, string Name, string Section, string PageTitle, bool IsCreated)
{
    public Uri Address => new("https://gtnh.huijiwiki.com/wiki/" + Uri.EscapeDataString(PageTitle)
        .Replace("%2F", "/", StringComparison.Ordinal).Replace("%3A", ":", StringComparison.Ordinal));
    public string StatusText => IsCreated ? "可查看" : "未创建，等待更新";
}
