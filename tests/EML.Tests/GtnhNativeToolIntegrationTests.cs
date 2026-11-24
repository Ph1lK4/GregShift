using System.Text.RegularExpressions;
using EML.App.ViewModels;
using EML.Core.Minecraft.Gtnh.Tools;

namespace EML.Tests;

public sealed class GtnhNativeToolIntegrationTests
{
    [Fact]
    public void BundledOreSnapshotLoadsAllRowsAndRealHyphenatedNames()
    {
        var catalog = GtnhNativeToolData.LoadOres();
        Assert.Equal(348, catalog.Materials.Count); Assert.Equal(40, catalog.Geology.Count);
        Assert.Equal("MPS", catalog.Materials.Single(item => item.Id == "Diamond").DefaultRoute);
        Assert.Equal("MBTM", catalog.Materials.Single(item => item.Id == "Nickel").DefaultRoute);
        Assert.Equal("None", catalog.Materials.Single(item => item.Id == "Debris").DefaultRoute);
        Assert.Equal("H", catalog.Materials.Single(item => item.Id == "HeeInstabilityOrb").DefaultRoute);
        var rules = GtnhOreRegexGenerator.Generate(catalog, GtnhOrePlan.Defaults(catalog));
        Assert.NotEmpty(rules);
        var grinding = rules.Single(rule => rule.MachineId == "macerator");
        Assert.True(Regex.IsMatch("oreChromo-Alumino-Povondraite", grinding.Expression, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));
        Assert.False(Regex.IsMatch("oreDebris", grinding.Expression, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));
        Assert.All(rules, rule => Assert.NotNull(new Regex(rule.Expression, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))));
    }

    [Fact]
    public void BundledResearchSnapshotIsCompleteAndWorksWithoutNetwork()
    {
        var aspects = GtnhResearchData.ParseWikiJson(GtnhNativeToolData.LoadResearchJson());
        Assert.Equal(69, aspects.Count); Assert.Equal(6, aspects.Count(item => item.Components.Count == 0));
        var normal = GtnhResearchPathFinder.Find(aspects, "aer", "terra");
        Assert.True(normal.Success); Assert.Equal("aer", normal.Path[0].Id); Assert.Equal("terra", normal.Path[^1].Id);
        var weighted = GtnhResearchPathFinder.Find(aspects, "aer", "terra", weighted: true);
        Assert.True(weighted.Success);
        Assert.Equal(weighted.Path.Zip(weighted.Path.Skip(1), (a, b) => a.Weight + b.Weight).Sum(), weighted.Cost);
    }

    [Fact]
    public void SevenToolDirectoryOnlyNavigatesAfterExplicitOpenAndBackStopsReader()
    {
        using var model = new GtnhToolsViewModel();
        Assert.True(model.IsToolDirectory); Assert.False(model.HasBrowser); Assert.Equal(7, model.Tools.Count);
        Assert.Equal(7, model.Tools.Select(tool => tool.Id).Distinct().Count());
        foreach (var index in new[] { 3, 4 })
        {
            model.OpenToolCommand.Execute(model.Tools[index]);
            Assert.False(model.IsToolDirectory); Assert.False(model.HasBrowser); Assert.Equal(index, model.SelectedToolIndex);
            model.BackToToolsCommand.Execute(null); Assert.True(model.IsToolDirectory);
        }
        model.OpenToolCommand.Execute(model.Tools[5]);
        Assert.Equal(GtnhToolDirectory.SeedMapUri.AbsoluteUri, model.RequestedAddress);
        model.UpdateBrowserState("https://gtnhquestsbook.top/", false, true, true, "stale", false);
        Assert.Equal(GtnhToolDirectory.SeedMapUri.AbsoluteUri, model.CurrentAddress); Assert.False(model.HasError);
        model.UpdateBrowserState("https://chunkbasenh.beyomoes.com/#map", false, false, false, null, false);
        model.BackToToolsCommand.Execute(null);
        Assert.Null(model.RequestedAddress); Assert.False(model.HasBrowser);
        model.OpenToolCommand.Execute(model.Tools[6]);
        Assert.Equal(GtnhToolDirectory.ThaumonomiconUri.AbsoluteUri, model.RequestedAddress);
        Assert.False(model.ShowBrowserAddress);
        model.OpenToolCommand.Execute(model.Tools[5]);
        Assert.Equal("https://chunkbasenh.beyomoes.com/#map", model.RequestedAddress);
        model.OpenToolCommand.Execute(new GtnhToolEntry(5, "evil", "外部", "", "", "", new Uri("https://evil.example/")));
        Assert.Equal("https://chunkbasenh.beyomoes.com/#map", model.RequestedAddress);
    }

    [Theory]
    [InlineData("https://chunkbasenh.beyomoes.com/", true)]
    [InlineData("https://chunkbasenh.beyomoes.com/#map", true)]
    [InlineData("http://chunkbasenh.beyomoes.com/", false)]
    [InlineData("https://chunkbasenh.beyomoes.com.evil.example/", false)]
    [InlineData("https://user@chunkbasenh.beyomoes.com/", false)]
    [InlineData("https://chunkbasenh.beyomoes.com:8443/", false)]
    [InlineData("file:///C:/private", false)]
    public void SeedReaderUsesExactHttpsHost(string address, bool expected) => Assert.Equal(expected, GtnhToolNavigationPolicy.IsAllowed(new Uri(address)));
}
