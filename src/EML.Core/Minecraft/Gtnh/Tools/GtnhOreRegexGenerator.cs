namespace EML.Core.Minecraft.Gtnh.Tools;

/// <summary>从固定处理图与矿物选择生成过滤规则。外部表格只提供字面量，不提供正则片段。</summary>
public static class GtnhOreRegexGenerator
{
    public static IReadOnlyList<GtnhOreRegexResult> Generate(GtnhOreCatalog catalog, GtnhOrePlan plan)
    {
        plan.Validate(catalog);
        var fragments = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var counts = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var generalMachines = new HashSet<string>(StringComparer.Ordinal);
        var actual = catalog.Materials.ToDictionary(ore => ore.Id,
            ore => GtnhOreRoutes.All.First(route => route.Id == plan.Routes.GetValueOrDefault(ore.Id, plan.CommonRouteId)), StringComparer.Ordinal);
        if (plan.IntegratedFactory)
        {
            var common = GtnhOreRoutes.IntegratedRoute(plan.CommonRouteId);
            foreach (var routeId in GtnhOreRoutes.IntegratedRouteIds)
            {
                var machine = "integrated:" + routeId;
                var included = actual.Where(item => GtnhOreRoutes.IntegratedRoute(item.Value.Id) == routeId).Select(item => item.Key).ToArray();
                if (routeId == common)
                {
                    var excluded = actual.Where(item => GtnhOreRoutes.IntegratedRoute(item.Value.Id) != routeId).Select(item => item.Key).ToArray();
                    Add(machine, General("raw", excluded), included, true);
                    Add(machine, General("crushed", excluded), included, true);
                }
                else if (included.Length > 0)
                {
                    Add(machine, Exact("raw", included), included);
                    Add(machine, Exact("crushed", included), included);
                }
            }
            // 不完全处理路线不能交给集成厂，否则会继续处理到玩家未选择的阶段。
            foreach (var group in actual.Where(item => !GtnhOreRoutes.IntegratedRouteIds.Contains(GtnhOreRoutes.IntegratedRoute(item.Value.Id), StringComparer.Ordinal))
                         .SelectMany(item => item.Value.Steps.Select(step => (step, item.Key))).GroupBy(item => item.step))
                Add(group.Key.MachineId, Exact(group.Key.Form, group.Select(item => item.Key)), group.Select(item => item.Key));
        }
        else
        {
            var common = GtnhOreRoutes.Common.First(route => route.Id == plan.CommonRouteId);
            var allSteps = common.Steps.Concat(actual.Values.SelectMany(route => route.Steps)).Distinct().ToArray();
            foreach (var step in allSteps)
            {
                var included = actual.Where(item => item.Value.Steps.Contains(step)).Select(item => item.Key).ToArray();
                if (common.Steps.Contains(step))
                {
                    var excluded = actual.Where(item => !item.Value.Steps.Contains(step)).Select(item => item.Key).ToArray();
                    Add(step.MachineId, General(step.Form, excluded), included, true);
                }
                else if (included.Length > 0) Add(step.MachineId, Exact(step.Form, included), included);
            }
        }
        foreach (var op in GtnhOreCatalog.ProcessingIds)
        {
            var minerals = plan.Processing.Where(item => item.Value == op).Select(item => item.Key).ToArray();
            if (minerals.Length > 0) Add(op, Exact("dust", minerals), minerals);
        }
        var order = GtnhOreRoutes.IntegratedRouteIds.Select(id => "integrated:" + id).Concat(GtnhOreRoutes.MachineOrder);
        return order.Where(fragments.ContainsKey).Select(machine => new GtnhOreRegexResult(machine, GtnhOreRoutes.MachineName(machine),
            "^(?:" + string.Join('|', fragments[machine].Distinct(StringComparer.Ordinal)) + ")\\z", plan.CharacterLimit, counts[machine].Count,
            generalMachines.Contains(machine))).ToArray();

        void Add(string machine, string fragment, IEnumerable<string> minerals, bool general = false)
        {
            if (fragment.Length == 0) return;
            if (!fragments.TryGetValue(machine, out var list)) fragments.Add(machine, list = []);
            list.Add(fragment);
            if (!counts.TryGetValue(machine, out var set)) counts.Add(machine, set = new(StringComparer.Ordinal));
            set.UnionWith(minerals);
            if (general) generalMachines.Add(machine);
        }
        string General(string form, IEnumerable<string> excluded)
        {
            // 排除项以完整矿辞匹配，避免 Iron 与 Ironwood、Purified 等前缀发生误伤。
            var suffix = GtnhOreRegexBuilder.BuildFragment(excluded);
            var prefix = Prefix(form);
            return (suffix.Length == 0 ? "" : "(?!" + prefix + "(?:" + suffix + ")\\z)") + prefix + "[A-Z][A-Za-z0-9_-]*";
        }
        string Exact(string form, IEnumerable<string> minerals)
        {
            var suffix = GtnhOreRegexBuilder.BuildFragment(minerals);
            return suffix.Length == 0 ? "" : Prefix(form) + "(?:" + suffix + ")";
        }
        string Prefix(string form) => form switch
        {
            "raw" => RawPrefix(),
            "crushed" => "crushed(?!Purified|Centrifuged)",
            "crushedPurified" => "crushedPurified", "crushedCentrifuged" => "crushedCentrifuged",
            "dustImpure" => "dustImpure", "dustPure" => "dustPure", "dust" => "dust(?!Impure|Pure)",
            _ => throw new InvalidDataException("未知矿物形态。")
        };
        string RawPrefix()
        {
            var geology = GtnhOreRegexBuilder.BuildFragment(plan.GeologyPrefixes);
            var prefix = geology.Length == 0 ? "ore" : "ore(?:(?:" + geology + "))?";
            if (catalog.Geology.Count > 0 && plan.GeologyPrefixes.Length == catalog.Geology.Count) return "(?:" + prefix + "|variantOre|rawOre)";
            // 广泛的通用路线不能把未勾选的地质前缀当成矿物名称的一部分重新接收。
            var excluded = GtnhOreRegexBuilder.BuildFragment(catalog.Geology.Select(item => item.Prefix).Except(plan.GeologyPrefixes, StringComparer.Ordinal));
            return (excluded.Length == 0 ? "" : "(?!ore(?:" + excluded + ")[A-Z][A-Za-z0-9_-]*\\z)") + "(?:" + prefix + "|rawOre)";
        }
    }
}
