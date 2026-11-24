using System.Text.Json;

namespace EML.Core.Minecraft.Gtnh.Tools;

/// <summary>保留原站目录顺序与未创建的页面；只在完整、可信的目录元数据到达后替换快照。</summary>
public static class GtnhToolsCatalog
{
    public static Uri FlowChartIndexUri { get; } = new("https://gtnh.huijiwiki.com/wiki/%E6%A8%A1%E6%9D%BF:FlowChart");
    public static Uri QuestBookUri { get; } = new("https://gtnhquestsbook.top/");
    public const string SnapshotDate = "2026-11-22";
    public const int MaximumDirectoryEntries = 512;
    public const int MaximumDirectoryJsonLength = 524288;

    public static IReadOnlyList<GtnhFlowChart> FlowCharts { get; } = CreateSnapshot();

    /// <summary>读取原站已渲染目录。失败时不返回半份结果，避免把解析异常当成页面尚未创建。</summary>
    public static bool TryReadDirectoryJson(string? json, out IReadOnlyList<GtnhFlowChart> charts)
    {
        charts = Array.Empty<GtnhFlowChart>();
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumDirectoryJsonLength) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Array ||
                document.RootElement.GetArrayLength() is 0 or > MaximumDirectoryEntries) return false;
            var items = new List<GtnhFlowChart>();
            var pages = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || !TryText(entry, "name", 160, out var name) ||
                    !TryText(entry, "section", 80, out var section) || !TryText(entry, "href", 2048, out var href) ||
                    !entry.TryGetProperty("isCreated", out var created) ||
                    created.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                    !Uri.TryCreate(FlowChartIndexUri, href, out var address) ||
                    !TryGetDirectoryPageTitle(address, created.GetBoolean(), out var pageTitle)) return false;
                // The same page may appear in more than one part of the wiki directory. Keep its first position.
                if (!pages.Add(pageTitle)) continue;
                items.Add(new(pageTitle, name, section, pageTitle, created.GetBoolean()));
            }
            if (items.Count == 0) return false;
            charts = items.AsReadOnly();
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool TryText(JsonElement entry, string property, int maximumLength, out string value)
    {
        value = "";
        if (!entry.TryGetProperty(property, out var field) || field.ValueKind != JsonValueKind.String) return false;
        value = field.GetString()?.Trim() ?? "";
        return value.Length is > 0 && value.Length <= maximumLength &&
            !value.Any(character => char.IsControl(character) || character == '\uFFFD');
    }

    private static bool TryGetDirectoryPageTitle(Uri address, bool isCreated, out string pageTitle)
    {
        if (GtnhToolNavigationPolicy.TryGetFlowChartPageTitle(address, out pageTitle)) return true;
        // Missing wiki pages use index.php?action=edit&redlink=1. Only extract the title;
        // the native placeholder never navigates to this edit link.
        if (isCreated || !GtnhToolNavigationPolicy.IsAllowed(address) || address.IdnHost != "gtnh.huijiwiki.com" ||
            address.AbsolutePath != "/index.php") return false;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var segment in address.Query.TrimStart('?').Split('&'))
        {
            var separator = segment.IndexOf('=');
            if (separator < 1) return false;
            var key = Uri.UnescapeDataString(segment[..separator].Replace('+', ' '));
            var value = Uri.UnescapeDataString(segment[(separator + 1)..].Replace('+', ' '));
            if (key is not ("title" or "action" or "redlink") || !fields.TryAdd(key, value)) return false;
        }
        if (fields.Count != 3 || !fields.TryGetValue("title", out var title) || !GtnhToolNavigationPolicy.IsValidPageTitle(title) ||
            fields["action"] != "edit" || fields["redlink"] != "1") return false;
        pageTitle = title;
        return true;
    }

    private static IReadOnlyList<GtnhFlowChart> CreateSnapshot()
    {
        var result = new List<GtnhFlowChart>();
        AddSection("工艺流程图", [
            "轻燃油", "聚乙烯", "柴油", "高十六烷值柴油", "粗汽油", "乙醇汽油", "抗爆剂", "高辛烷值汽油", "苯", "生物柴油",
            "绿色高十六烷值柴油", "发酵生物质", "硝基苯", "磷酸制氢", "太阳能级硅(多晶硅)(三氯化硅法)", "太阳能级硅(多晶硅)(四氯化硅法)",
            "硅氧产线", "钢氧产线", "三氯化铁", "过硫酸钠", "红宝石浆", "聚四氟乙烯", "过氧化氢", "工业氢氟酸", "火箭燃料",
            "金红石", "钛铁矿", "铝土矿", "环氧树脂", "钨", "铟", "氟金云母", "聚苯并咪唑", "铂处理", "铂渣", "铂", "钯", "铑", "钌", "锇", "铱",
            "独居石/主线", "独居石/铪锆", "独居石/铈", "独居石/铕钐钆", "氟碳镧铈主线", "氟碳镧铈矿", "钐稀土精粉", "钕稀土精粉",
            "GT++稀土处理", "钐精粉", "钐精粉简单处理", "锌钍合金", "钫", "下界之星循环", "净化水", "活性炭滤网", "聚氯化铝", "下界合金", "碘", "松油",
            "?乙基黄原酸钾", "?乙基黄原酸钠", "晶体循环", "硅岩处理", "硅岩", "富集硅岩", "超能硅岩", "轻质硅岩燃料与重质硅岩燃料", "硅岩基流体燃料",
            "氰基丙烯酸乙酯", "海晶酸", "六硼化镧", "培养基原液", "天体钨等离子体", "?纳米萃取树脂", "掺铈镥铝石榴石循环高压釜线", "掺铈镥铝石榴石循环真空炉线",
            "凯芙拉", "生物培养基原液", "拉多X聚合物", "超时空金属", "永恒", "无损声子传输介质"
        ]);
        AddSection("多选择 / 配方对比", [
            "?钢", "?铝", "石油化工概览", "木材干馏概览", "?氢", "?氧", "?氯", "?氟", "氟锑酸", "?氡", "?硫酸", "?硝酸", "?磷酸", "?钍-232",
            "?中子", "?基岩", "?宇宙中子态素", "?无尽", "?海珀珍", "宇宙蜂培育路线"
        ]);
        return result.AsReadOnly();

        void AddSection(string section, string[] names)
        {
            foreach (var value in names)
            {
                var created = !value.StartsWith('?');
                var name = created ? value : value[1..];
                var pageTitle = "模板:FlowChart/" + name;
                result.Add(new(pageTitle, name, section, pageTitle, created));
            }
        }
    }
}
