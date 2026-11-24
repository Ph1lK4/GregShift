using System.Text.Json;
using System.Text.RegularExpressions;
using EML.Core.Minecraft.Gtnh.Tools;

namespace EML.Tests;

public sealed class GtnhOreRegexGeneratorTests
{
    [Fact]
    public void CommonRouteAndExactOverridesDoNotCrossContaminate()
    {
        var catalog = Catalog();
        var plan = GtnhOrePlan.Defaults(catalog) with { Routes = new() { ["Gold"] = "MPS", ["Iron"] = "None" } };
        var rules = Rules(catalog, plan);
        AssertMatch(rules, "macerator", ["oreGold", "oreGraniteGold", "rawOreGold", "variantOreGold", "oreCopper", "crushedPurifiedCopper"],
            ["oreIron", "oreGraniteIron", "rawOreIron", "crushedPurifiedGold", "crushedGold", "dustCopper"]);
        AssertMatch(rules, "washingPlant", ["crushedGold", "crushedCopper"], ["crushedIron", "crushedPurifiedGold", "crushedCentrifugedGold"]);
        AssertMatch(rules, "sifter", ["crushedPurifiedGold"], ["crushedPurifiedCopper", "crushedGold", "crushedPurifiedGoldExtra"]);
    }

    [Fact]
    public void ExactExclusionsDoNotExcludeLongerMineralNames()
    {
        var catalog = Catalog();
        var plan = GtnhOrePlan.Defaults(catalog) with { Routes = new() { ["Iron"] = "None" } };
        var rules = Rules(catalog, plan);
        AssertMatch(rules, "macerator", ["oreIronwood", "oreGraniteIronwood", "crushedPurifiedIronwood"], ["oreIron", "oreGraniteIron", "crushedPurifiedIron"]);
    }

    [Fact]
    public void PartialGeologySelectionCannotBacktrackIntoMaterialName()
    {
        var catalog = Catalog();
        var plan = GtnhOrePlan.Defaults(catalog) with { GeologyPrefixes = ["Granite"], Routes = new() { ["Gold"] = "MPS" } };
        var rules = Rules(catalog, plan);
        AssertMatch(rules, "macerator", ["oreGraniteGold", "oreGold", "rawOreGold", "oreIron"], ["oreBasaltGold", "oreBasaltIron", "variantOreGold"]);
    }

    [Fact]
    public void NoGeologyStillAcceptsOrdinaryAndRawOres()
    {
        var catalog = Catalog();
        var rules = Rules(catalog, GtnhOrePlan.Defaults(catalog) with { GeologyPrefixes = [] });
        AssertMatch(rules, "macerator", ["oreIron", "rawOreIron"], ["oreGraniteIron", "oreBasaltIron", "variantOreIron"]);
    }

    [Fact]
    public void IntegratedRoutesAcceptRawAndCrushedButKeepIncompleteRoutesExternal()
    {
        var catalog = Catalog();
        var plan = GtnhOrePlan.Defaults(catalog) with { IntegratedFactory = true,
            Routes = new() { ["Gold"] = "MPS", ["Iron"] = "M", ["Ironwood"] = "None" } };
        var rules = Rules(catalog, plan);
        AssertMatch(rules, "integrated:MPMC", ["oreCopper", "crushedCopper", "oreGraniteCopper"], ["oreGold", "crushedGold", "oreIron", "crushedIron", "oreIronwood", "crushedPurifiedCopper"]);
        AssertMatch(rules, "integrated:MPS", ["oreGold", "crushedGold", "oreBasaltGold", "rawOreGold"], ["oreCopper", "crushedIron", "crushedPurifiedGold"]);
        AssertMatch(rules, "macerator", ["oreIron", "rawOreIron"], ["oreCopper", "crushedIron", "oreIronwood"]);
    }

    [Fact]
    public void IntegratedAliasesUseCompatibleFactoryRoute()
    {
        var catalog = Catalog();
        var rules = Rules(catalog, GtnhOrePlan.Defaults(catalog) with { IntegratedFactory = true, CommonRouteId = "MHW", Routes = new() { ["Gold"] = "MTM" } });
        AssertMatch(rules, "integrated:MMC", ["oreIron", "crushedIron"], ["oreGold"]);
        AssertMatch(rules, "integrated:MPTM", ["oreGold", "crushedGold"], ["oreIron"]);
        Assert.DoesNotContain("forgeHammer", rules.Keys);
    }

    [Fact]
    public void FinalDustProcessingIsExactAndMergesCentrifugeRequirements()
    {
        var catalog = Catalog();
        var plan = GtnhOrePlan.Defaults(catalog) with { Processing = new() { ["Iron"] = "centrifuge", ["Gold"] = "electrolyzer" } };
        var rules = Rules(catalog, plan);
        AssertMatch(rules, "centrifuge", ["dustIron", "dustPureIron", "dustPureGold"], ["dustGold", "dustImpureIron", "dustIronwood", "dustIronExtra"]);
        AssertMatch(rules, "electrolyzer", ["dustGold"], ["dustPureGold", "dustImpureGold", "dustGoldExtra", "dustIron"]);
    }

    [Fact]
    public void RulesAreStrictlyAnchoredAndOverLimitRulesAreNeverTruncated()
    {
        var catalog = Catalog();
        var results = GtnhOreRegexGenerator.Generate(catalog, GtnhOrePlan.Defaults(catalog));
        var rule = Assert.Single(results, item => item.MachineId == "macerator");
        Assert.DoesNotMatch(rule.Expression, "oreIron\n");
        Assert.DoesNotMatch(rule.Expression, "xoreIron");
        var over = rule with { CharacterLimit = 1 };
        Assert.True(over.IsOverLimit); Assert.Equal(rule.Expression, over.Expression);
    }

    [Fact]
    public void PlanRoundTripsWithoutLosingGeologyRoutesOrOperations()
    {
        var catalog = Catalog();
        var plan = GtnhOrePlan.Defaults(catalog) with { CommonRouteId = "HHW", CharacterLimit = 256, IntegratedFactory = true,
            Routes = new() { ["Iron"] = "None" }, Processing = new() { ["Gold"] = "electrolyzer" }, GeologyPrefixes = ["Basalt"] };
        var loaded = GtnhOrePlan.FromJson(plan.ToJson(), catalog);
        Assert.Equal(plan.ToJson(), loaded.ToJson());
        Assert.Equal(GtnhOreRegexGenerator.Generate(catalog, plan), GtnhOreRegexGenerator.Generate(catalog, loaded));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2}")]
    [InlineData("{\"dataVersion\":\"old\"}")]
    [InlineData("{\"characterLimit\":10}")]
    [InlineData("{\"routes\":{\"Unknown\":\"MPMC\"}}")]
    [InlineData("{\"routes\":{\"Iron\":\"evil\"}}")]
    [InlineData("{\"processing\":{\"Iron\":\"dehydrator\"}}")]
    [InlineData("{\"geologyPrefixes\":[\"Granite\",\"Granite\"]}")]
    [InlineData("{\"routes\":null}")]
    public void RejectsIncompatibleOrInvalidPlans(string json) => Assert.Throws<InvalidDataException>(() => GtnhOrePlan.FromJson(json, Catalog()));

    [Fact]
    public void ReadsActualTabxShapeAndKeepsUncertainPresetsExplicit()
    {
        var ores = Table(["oreDict", "translatedName", "key"], ["Iron", "铁", "gregtech:gt.blockores:0"], ["Gold", "金", "gold"]);
        var presets = Table(["oreDict", "translatedName", "preset", "controversial", "available", "recommended"],
            ["Iron", "铁", "MPTM", false, "electrolyzer|centrifuge", true], ["Gold", "金", "MPS", true, "none", false]);
        var catalog = GtnhOreCatalog.Parse(ores, presets, Table(["prefix", "translatedName"], ["Granite", "花岗岩"]),
            Table(["oreDict", "translatedName", "key"], ["Copper", "铜", "copper"]));
        Assert.Equal(3, catalog.Materials.Count);
        var iron = Assert.Single(catalog.Materials, item => item.Id == "Iron");
        Assert.Equal("铁 · Iron", iron.Label); Assert.Equal("MPTM", iron.DefaultRoute);
        Assert.Equal(["electrolyzer", "centrifuge"], iron.AvailableProcessing); Assert.True(iron.RecommendedProcessing);
        var gold = Assert.Single(catalog.Materials, item => item.Id == "Gold");
        Assert.True(gold.NeedsReview); Assert.Equal("common", gold.DefaultRoute);
        var plan = GtnhOrePlan.Defaults(catalog);
        Assert.Equal("MPTM", plan.Routes["Iron"]); Assert.Equal("electrolyzer", plan.Processing["Iron"]);
        Assert.DoesNotContain("Gold", plan.Routes.Keys);
    }

    [Fact]
    public void RejectsMalformedAndInjectedCatalogTables()
    {
        var empty = Table(["oreDict"]);
        Assert.Throws<InvalidDataException>(() => GtnhOreCatalog.Parse(empty, empty, empty));
        Assert.Throws<InvalidDataException>(() => GtnhOreCatalog.Parse(Table(["oreDict"], ["Iron.*"]), empty, empty));
        Assert.Throws<InvalidDataException>(() => GtnhOreCatalog.Parse("{\"schema\":{\"fields\":[{\"name\":\"oreDict\"}]},\"data\":[[]]}", empty, empty));
        Assert.Throws<InvalidDataException>(() => GtnhOreCatalog.Parse(Table(["oreDict"], ["Iron"]),
            Table(["oreDict", "available"], ["Iron", "runScript"]), empty));
    }

    private static Dictionary<string, Regex> Rules(GtnhOreCatalog catalog, GtnhOrePlan plan) => GtnhOreRegexGenerator.Generate(catalog, plan)
        .ToDictionary(item => item.MachineId, item => new Regex(item.Expression, RegexOptions.None, TimeSpan.FromSeconds(1)));
    private static void AssertMatch(IReadOnlyDictionary<string, Regex> rules, string machine, string[] accepted, string[] rejected)
    { Assert.All(accepted, value => Assert.True(rules[machine].IsMatch(value), $"{machine} should accept {value}"));
        Assert.All(rejected, value => Assert.False(rules[machine].IsMatch(value), $"{machine} should reject {value}")); }
    private static GtnhOreCatalog Catalog() => new(
        [new("Iron", "铁", "Iron", "iron", "common", false, ["centrifuge", "electrolyzer"], false),
         new("Ironwood", "铁木", "Ironwood", "ironwood", "common", false, [], false),
         new("Gold", "金", "Gold", "gold", "common", false, ["electrolyzer"], false),
         new("Copper", "铜", "Copper", "copper", "common", false, [], false)],
        [new("Granite", "花岗岩"), new("Basalt", "玄武岩")]);
    private static string Table(string[] columns, params object[][] rows) => JsonSerializer.Serialize(new
    { schema = new { fields = columns.Select(name => new { name }) }, data = rows });
}
