using EML.Core.Minecraft.Gtnh.Tools;

namespace EML.Tests;

public sealed class GtnhResearchTests
{
    // 人工关系图，仅验证算法，不冒充 GTNH 正式数据。
    private static readonly GtnhResearchAspect[] Graph =
    [
        new("a", "起点", [], 1), new("b", "终点", [], 1),
        new("c", "昂贵捷径", ["a", "b"], 10),
        new("d", "低成本一", ["a", "c"], 1),
        new("e", "低成本二", ["b", "d"], 1)
    ];

    [Fact]
    public void OrdinaryModeFindsFewestIntermediateAspects()
    {
        var result = GtnhResearchPathFinder.Find(Graph, "a", "b");
        Assert.True(result.Success);
        Assert.Equal(["a", "c", "b"], result.Path.Select(item => item.Id));
        Assert.Equal(1, result.IntermediateCount);
    }

    [Fact]
    public void WeightedModeCanPreferALongerButCheaperChain()
    {
        var result = GtnhResearchPathFinder.Find(Graph, "a", "b", weighted: true);
        Assert.True(result.Success);
        Assert.Equal(["a", "d", "e", "b"], result.Path.Select(item => item.Id));
        Assert.Equal(6, result.Cost);
    }

    [Fact]
    public void WeightedCostIncludesBothEndpointsOfEachEdge()
    {
        var result = GtnhResearchPathFinder.Find(Graph, "a", "c", weighted: true);
        Assert.True(result.Success);
        Assert.Equal(11, result.Cost);
    }

    [Fact]
    public void LinksAreUndirectedAndDisabledAspectsAreNotUsed()
    {
        var result = GtnhResearchPathFinder.Find(Graph, "b", "a", weighted: true,
            disabledIds: new HashSet<string>(["d"]));
        Assert.True(result.Success);
        Assert.Equal(["b", "c", "a"], result.Path.Select(item => item.Id));
    }

    [Fact]
    public void DisabledEndpointFailsWithReason()
    {
        var result = GtnhResearchPathFinder.Find(Graph, "a", "b", disabledIds: new HashSet<string>(["a"]));
        Assert.False(result.Success);
        Assert.Contains("禁用", result.Message);
    }

    [Fact]
    public void DisconnectedGraphReportsNoSolution()
    {
        var graph = Graph.Append(new GtnhResearchAspect("isolated", "孤立", [], 1)).ToArray();
        var result = GtnhResearchPathFinder.Find(graph, "a", "isolated");
        Assert.False(result.Success);
        Assert.Empty(result.Path);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(64)]
    public void MinimumIntermediateAllowsRepeatedAspectsWithFiniteSearch(int minimum)
    {
        var result = GtnhResearchPathFinder.Find(Graph, "a", "b", minimum, weighted: true);
        Assert.True(result.Success);
        Assert.True(result.IntermediateCount >= minimum);
        Assert.True(result.Path.Count <= Graph.Length * (minimum + 2));
        foreach (var (first, second) in result.Path.Zip(result.Path.Skip(1)))
            Assert.True(first.Components.Contains(second.Id) || second.Components.Contains(first.Id));
    }

    [Fact]
    public void SameEndpointStillNeedsAValidConnectingChain()
    {
        var result = GtnhResearchPathFinder.Find(Graph, "a", "a");
        Assert.True(result.Success);
        Assert.True(result.Path.Count >= 3);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65)]
    public void InvalidMinimumIsRejected(int minimum) =>
        Assert.False(GtnhResearchPathFinder.Find(Graph, "a", "b", minimum).Success);

    [Fact]
    public void IncompleteOrInvalidGraphDoesNotInventMissingRelations()
    {
        Assert.False(GtnhResearchPathFinder.Find([new("a", "a", ["missing"], 1)], "a", "a").Success);
        Assert.False(GtnhResearchPathFinder.Find([new("a", "a", [], double.NaN)], "a", "a").Success);
        Assert.False(GtnhResearchPathFinder.Find([new("a", "a", ["a"], 1)], "a", "a").Success);
    }

    [Fact]
    public void CalculationHonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => GtnhResearchPathFinder.Find(Graph, "a", "b", cancellationToken: cancellation.Token));
    }

    [Fact]
    public void ParsesNormalizedDeclarativeDataWithoutExecutingScripts()
    {
        const string json = """
            {"aspects":[{"id":"a","name":"甲","components":[],"weight":1},
             {"id":"b","name":"乙","components":[],"weight":2},
             {"id":"c","name":"丙","components":["a","b"],"weight":3}]}
            """;
        var data = GtnhResearchData.ParseWikiJson(json);
        Assert.Equal(3, data.Count);
        Assert.Equal(["a", "b"], data[2].Components);
    }

    [Fact]
    public void ParsesTabularSchemaAndData()
    {
        const string json = """
            {"schema":{"fields":[{"name":"tag"},{"name":"name_zh"},{"name":"components"},{"name":"weight"}]},
             "data":[["a","甲",[],1],["b","乙",[],2],["c","丙",["a","b"],3]]}
            """;
        var data = GtnhResearchData.ParseWikiJson(json);
        Assert.Equal(3, data.Count);
        Assert.Equal("丙", data[2].Name);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("[{\"id\":\"a\",\"name\":\"甲\",\"components\":[\"missing\",\"other\"]}]")]
    [InlineData("[{\"id\":\"a\",\"name\":\"甲\",\"components\":[],\"weight\":0}]")]
    [InlineData("[{\"id\":\"a\",\"name\":\"甲\",\"components\":[]},{\"id\":\"a\",\"name\":\"重复\",\"components\":[]}]")]
    public void InvalidDataIsRejected(string json) => Assert.Throws<InvalidDataException>(() => GtnhResearchData.ParseWikiJson(json));
}
