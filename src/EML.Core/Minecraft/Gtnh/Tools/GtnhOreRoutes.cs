namespace EML.Core.Minecraft.Gtnh.Tools;

public sealed record GtnhOreStep(string MachineId, string Form);
public sealed record GtnhOreRoute(string Id, string Label, IReadOnlyList<GtnhOreStep> Steps)
{
    public string ShortLabel => Id == "None" ? "不处理" : Id;
}

/// <summary>矿物处理路线是固定的机器输入图，不接受表格或方案中的可执行表达式。</summary>
public static class GtnhOreRoutes
{
    public static IReadOnlyList<GtnhOreRoute> All { get; } =
    [
        Route("MPMC", "研磨 → 洗矿 → 研磨 → 离心", ("macerator", "raw"), ("washingPlant", "crushed"), ("macerator", "crushedPurified"), ("centrifuge", "dustPure")),
        Route("MMC", "研磨 → 研磨 → 离心", ("macerator", "raw"), ("macerator", "crushed"), ("centrifuge", "dustImpure")),
        Route("MHW", "研磨 → 锻造锤 → 简易洗矿", ("macerator", "raw"), ("forgeHammer", "crushed"), ("simpleWasher", "dustImpure")),
        Route("HHW", "锻造锤 → 锻造锤 → 简易洗矿", ("forgeHammer", "raw"), ("forgeHammer", "crushed"), ("simpleWasher", "dustImpure")),
        Route("MPS", "研磨 → 洗矿 → 筛选", ("macerator", "raw"), ("washingPlant", "crushed"), ("sifter", "crushedPurified")),
        Route("MTM", "研磨 → 热力离心 → 研磨", ("macerator", "raw"), ("thermalCentrifuge", "crushed"), ("macerator", "crushedCentrifuged")),
        Route("MPTM", "研磨 → 洗矿 → 热力离心 → 研磨", ("macerator", "raw"), ("washingPlant", "crushed"), ("thermalCentrifuge", "crushedPurified"), ("macerator", "crushedCentrifuged")),
        Route("MBMC", "研磨 → 化学浸洗 → 研磨 → 离心", ("macerator", "raw"), ("chemicalBath", "crushed"), ("macerator", "crushedPurified"), ("centrifuge", "dustPure")),
        Route("MBTM", "研磨 → 化学浸洗 → 热力离心 → 研磨", ("macerator", "raw"), ("chemicalBath", "crushed"), ("thermalCentrifuge", "crushedPurified"), ("macerator", "crushedCentrifuged")),
        Route("H", "仅锻造锤", ("forgeHammer", "raw")),
        Route("M", "仅研磨矿石", ("macerator", "raw")),
        Route("MM", "研磨矿石与粉碎矿石", ("macerator", "raw"), ("macerator", "crushed")),
        Route("MP", "研磨 → 洗矿（不完全处理）", ("macerator", "raw"), ("washingPlant", "crushed")),
        Route("None", "不处理")
    ];
    public static IReadOnlyList<GtnhOreRoute> Common { get; } = All.Where(route => route.Id is "MPMC" or "MMC" or "MHW" or "HHW").ToArray();
    public static readonly string[] MachineOrder = ["macerator", "forgeHammer", "simpleWasher", "washingPlant", "chemicalBath", "centrifuge", "thermalCentrifuge", "sifter", "electrolyzer", "dehydrator"];
    public static readonly string[] IntegratedRouteIds = ["MPTM", "MPMC", "MMC", "MPS", "MBMC", "MBTM", "HHW"];

    public static string MachineName(string id) => id.StartsWith("integrated:", StringComparison.Ordinal) ? "集成矿石处理厂 · " + All.First(route => route.Id == id[11..]).Label : id switch
    {
        "macerator" => "研磨机", "forgeHammer" => "锻造锤", "simpleWasher" => "简易洗矿池", "washingPlant" => "洗矿厂",
        "chemicalBath" => "化学浸洗机", "centrifuge" => "离心机", "thermalCentrifuge" => "热力离心机", "sifter" => "筛选机",
        "electrolyzer" => "电解机", "dehydrator" => "脱水机", _ => id
    };
    public static string IntegratedRoute(string id) => id switch { "MHW" => "MMC", "MTM" => "MPTM", _ => id };
    private static GtnhOreRoute Route(string id, string label, params (string Machine, string Form)[] steps) =>
        new(id, label, steps.Select(step => new GtnhOreStep(step.Machine, step.Form)).ToArray());
}
