namespace EML.Core.Minecraft.Gtnh.Tools;

public sealed record GtnhToolEntry(int Index, string Id, string Name, string Description, string Kind, string Glyph, Uri? Address = null);

/// <summary>稳定工具标识与入口地址，不接受网页或设置文件提供的可执行入口。</summary>
public static class GtnhToolDirectory
{
    public static Uri SeedMapUri { get; } = new("https://chunkbasenh.beyomoes.com/");
    public static Uri ThaumonomiconUri { get; } = new("https://gtnh.huijiwiki.com/wiki/研究#研究一览");
    public static IReadOnlyList<GtnhToolEntry> Entries { get; } = Array.AsReadOnly<GtnhToolEntry>(
    [
        new(0, "flow-charts", "工艺流程图", "浏览工艺路线、材料与机器，保留未创建的目录。", "本地阅读 · 按需联网", "\uE8A5"),
        new(1, "quest-book", "在线任务书", "在启动器内查阅任务，不同步游戏中的任务进度。", "在线工具", "\uE8F1", GtnhToolsCatalog.QuestBookUri),
        new(2, "planner", "产线规划器", "用中文查找物品，规划配方与生产比例。", "在线工具 · 中文辅助", "\uE9D9", GtnhToolNavigationPolicy.PlannerUri),
        new(3, "ore-regex", "矿物处理正则", "按默认路线或逐矿设置，生成矿辞过滤卡规则。", "原生工具 · 可离线", "\uE9CE"),
        new(4, "research-helper", "神秘时代研究助手", "寻找要素连接链，自定义可用要素与权重。", "原生工具 · 可离线", "\uE721"),
        new(5, "seed-map", "GTNH 种子地图", "输入种子查看地形、矿脉等预测图层。", "在线工具", "\uE707", SeedMapUri),
        new(6, "thaumonomicon", "在线魔导手册", "浏览研究分类、前置关系与研究详情。", "在线工具", "\uE8A4", ThaumonomiconUri)
    ]);
}
