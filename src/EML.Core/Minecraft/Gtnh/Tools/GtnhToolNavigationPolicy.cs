namespace EML.Core.Minecraft.Gtnh.Tools;

/// <summary>工具只接收明确配置站点的 HTTPS 顶层导航，不为网页提供本地协议入口。</summary>
public static class GtnhToolNavigationPolicy
{
    private const string FlowChartPrefix = "模板:FlowChart/";

    public static bool IsAllowed(Uri? address) => address is
        { IsAbsoluteUri: true, Scheme: "https", IsDefaultPort: true, UserInfo.Length: 0 } &&
        address.IdnHost is "gtnh.huijiwiki.com" or "gtnhquestsbook.top" or "gtnhplanner.com" or "chunkbasenh.beyomoes.com";

    public static Uri PlannerUri { get; } = new("https://gtnhplanner.com/");

    public static bool IsPlanner(Uri? address) => IsAllowed(address) && address!.IdnHost == PlannerUri.IdnHost;

    public static bool IsFlowChartPage(Uri? address) => TryGetFlowChartPageTitle(address, out _);

    /// <summary>不信任 DOM 返回的链接；只有模板子页才能成为可打开的目录项。</summary>
    public static bool TryGetFlowChartPageTitle(Uri? address, out string pageTitle)
    {
        pageTitle = "";
        if (!IsAllowed(address) || address!.IdnHost != "gtnh.huijiwiki.com" || address.Query.Length != 0) return false;
        string decoded;
        try { decoded = Uri.UnescapeDataString(address.AbsolutePath); }
        catch (UriFormatException) { return false; }
        if (!decoded.StartsWith("/wiki/", StringComparison.Ordinal)) return false;
        var title = decoded[6..];
        if (!IsValidPageTitle(title)) return false;
        pageTitle = title;
        return true;
    }

    internal static bool IsValidPageTitle(string title)
    {
        if (!title.StartsWith(FlowChartPrefix, StringComparison.Ordinal) || title.Length > 240) return false;
        var suffix = title[FlowChartPrefix.Length..];
        return suffix.Length > 0 && suffix == suffix.Trim() &&
            !suffix.Any(character => char.IsControl(character) || character is '\\' or '%' or '?' or '#' or '\uFFFD') &&
            !suffix.Split('/').Any(part => part.Length == 0 || part is "." or "..");
    }
}
