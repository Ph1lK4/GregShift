namespace EML.Core.Minecraft.Gtnh.Tools;

/// <summary>要素事实数据；图标和网页脚本不参与本地路径计算。</summary>
public sealed record GtnhResearchAspect(string Id, string Name, IReadOnlyList<string> Components, double Weight = 1);

public sealed record GtnhResearchPathResult(bool Success, IReadOnlyList<GtnhResearchAspect> Path, double Cost, string Message)
{
    public int IntermediateCount => Math.Max(0, Path.Count - 2);
}

/// <summary>在要素/组分的无向关系图中搜索连接链，不尝试布置整张六边形研究笔记。</summary>
public static class GtnhResearchPathFinder
{
    public const int MaximumIntermediateCount = 64;
    public const int MaximumAspectCount = 256;

    public static GtnhResearchPathResult Find(IReadOnlyList<GtnhResearchAspect> aspects, string startId, string endId,
        int minimumIntermediate = 0, bool weighted = false, IReadOnlySet<string>? disabledIds = null,
        CancellationToken cancellationToken = default)
    {
        if (minimumIntermediate is < 0 or > MaximumIntermediateCount)
            return Failure($"最少中间要素数须为 0～{MaximumIntermediateCount}。");
        if (aspects.Count is 0 or > MaximumAspectCount) return Failure("请先读取完整且有效的要素数据。");
        var byId = new Dictionary<string, GtnhResearchAspect>(StringComparer.OrdinalIgnoreCase);
        foreach (var aspect in aspects)
        {
            if (string.IsNullOrWhiteSpace(aspect.Id) || !double.IsFinite(aspect.Weight) || aspect.Weight <= 0 ||
                aspect.Weight > 1_000_000_000_000 || aspect.Components is null || aspect.Components.Count is not (0 or 2) || !byId.TryAdd(aspect.Id, aspect))
                return Failure("要素 ID 或权重无效，无法计算。");
        }
        if (string.IsNullOrWhiteSpace(startId) || string.IsNullOrWhiteSpace(endId) ||
            !byId.TryGetValue(startId, out var start) || !byId.TryGetValue(endId, out var end))
            return Failure("请选择起点与终点要素。");
        var disabled = disabledIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (disabled?.Contains(start.Id) == true || disabled?.Contains(end.Id) == true)
            return Failure("起点或终点已被禁用，请先启用或重新选择。");

        var links = byId.Keys.ToDictionary(id => id, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        foreach (var aspect in aspects)
            foreach (var component in aspect.Components)
            {
                if (!byId.ContainsKey(component)) return Failure($"要素数据缺少组分 {component}，请更新完整数据。");
                if (string.Equals(aspect.Id, component, StringComparison.OrdinalIgnoreCase)) return Failure("要素不能与自身建立组分关系。");
                links[aspect.Id].Add(component);
                links[component].Add(aspect.Id);
            }

        // 将满足最小步数后的深度合并成一个桶；即使允许重复要素，也只有有限个搜索状态。
        var minimumEdges = minimumIntermediate + 1;
        var initial = new State(start.Id, 0);
        var costs = new Dictionary<State, double> { [initial] = 0 };
        var parents = new Dictionary<State, State>();
        var queue = new PriorityQueue<State, (double Cost, int Order)>();
        var order = 0;
        queue.Enqueue(initial, (0, order++));
        while (queue.TryDequeue(out var current, out var priority))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!costs.TryGetValue(current, out var currentCost) || priority.Cost != currentCost) continue;
            if (current.Depth == minimumEdges && string.Equals(current.Id, end.Id, StringComparison.OrdinalIgnoreCase))
            {
                var path = new List<GtnhResearchAspect>();
                var node = current;
                while (true)
                {
                    path.Add(byId[node.Id]);
                    if (!parents.TryGetValue(node, out node)) break;
                }
                path.Reverse();
                var cost = weighted ? currentCost : Math.Max(0, path.Count - 2);
                return new(true, path, cost, $"已找到连接链 · {path.Count - 2} 个中间要素。");
            }
            foreach (var nextId in links[current.Id].Order(StringComparer.Ordinal))
            {
                if (disabled?.Contains(nextId) == true) continue;
                var next = new State(byId[nextId].Id, Math.Min(current.Depth + 1, minimumEdges));
                // 与原工具的成本口径一致：每条连线消耗两端的权重；不是玩家实际研究点库存。
                var nextCost = currentCost + (weighted ? byId[current.Id].Weight + byId[nextId].Weight : 1);
                if (costs.TryGetValue(next, out var previousCost) && previousCost <= nextCost) continue;
                costs[next] = nextCost;
                parents[next] = current;
                queue.Enqueue(next, (nextCost, order++));
            }
        }
        return Failure("当前可用要素与最少中间数下没有连接链，请调整条件。");
    }

    private static GtnhResearchPathResult Failure(string message) => new(false, [], 0, message);
    private readonly record struct State(string Id, int Depth);
}
