using System.IO;
using EML.App.ViewModels;

namespace EML.App.Tests;

public sealed class GtnhResearchHelperTests
{
    private const string Data = """
        {"aspects":[{"id":"a","name":"甲","components":[],"weight":1},
         {"id":"b","name":"乙","components":[],"weight":2},
         {"id":"c","name":"丙","components":["a","b"],"weight":3}]}
        """;

    [Fact]
    public async Task CalculatesAndInvalidatesStaleResultAfterInputChanges()
    {
        var model = Create();
        model.StartAspect = model.Aspects[0]; model.EndAspect = model.Aspects[1];
        await model.CalculateCommand.ExecuteAsync(null);
        Assert.True(model.HasResult);
        Assert.Contains("丙", model.PathText);
        model.MinimumIntermediateText = "2";
        Assert.False(model.HasResult);
        Assert.Empty(model.PathText);
    }

    [Fact]
    public void ChineseAndIdSearchCanBeUsedRepeatedly()
    {
        var model = Create();
        model.SearchText = "甲";
        Assert.Equal("a", Assert.Single(model.VisibleAspects).Id);
        model.SearchText = "c";
        Assert.Equal("c", Assert.Single(model.VisibleAspects).Id);
        model.SearchText = "";
        Assert.Equal(3, model.VisibleAspects.Count);
    }

    [Fact]
    public async Task InvalidWeightDoesNotKeepAnOldUsableResult()
    {
        var model = Create();
        model.StartAspect = model.Aspects[0]; model.EndAspect = model.Aspects[1];
        await model.CalculateCommand.ExecuteAsync(null);
        model.Aspects[2].WeightText = "NaN";
        await model.CalculateCommand.ExecuteAsync(null);
        Assert.False(model.HasResult);
        Assert.Contains("权重无效", model.Status);
    }

    [Fact]
    public void SettingsRoundTripPreservesSelectionWeightsAndDisabledAspects()
    {
        var source = Create();
        source.StartAspect = source.Aspects[0]; source.EndAspect = source.Aspects[1];
        source.Aspects[2].IsEnabled = false; source.Aspects[0].WeightText = "0.25";
        source.MinimumIntermediateText = "4"; source.IsWeighted = true;
        var destination = Create();
        destination.ImportSettingsJson(source.ExportSettingsJson());
        Assert.Equal("a", destination.StartAspect?.Id); Assert.Equal("b", destination.EndAspect?.Id);
        Assert.False(destination.Aspects[2].IsEnabled); Assert.Equal("0.25", destination.Aspects[0].WeightText);
        Assert.Equal("4", destination.MinimumIntermediateText); Assert.True(destination.IsWeighted);
    }

    [Fact]
    public void FailedSettingsImportDoesNotChangeExistingInputs()
    {
        var model = Create(); model.StartAspect = model.Aspects[0];
        Assert.Throws<InvalidDataException>(() => model.ImportSettingsJson("{\"Version\":1,\"MinimumIntermediate\":\"65\",\"Aspects\":[]}"));
        Assert.Equal("a", model.StartAspect?.Id);
        Assert.Equal("0", model.MinimumIntermediateText);
    }

    [Theory]
    [InlineData("{\"Version\":1,\"MinimumIntermediate\":\"0\",\"Aspects\":[null]}")]
    [InlineData("{\"Version\":1,\"MinimumIntermediate\":\"0\",\"Aspects\":null}")]
    [InlineData("{\"Version\":1,\"MinimumIntermediate\":\"0\",\"Aspects\":[{\"Id\":\"a\",\"Enabled\":true,\"WeightText\":null}]}")]
    public void MalformedSettingsAreRejectedWithoutChangingExistingInputs(string json)
    {
        var model = Create(); model.StartAspect = model.Aspects[0];
        Assert.Throws<InvalidDataException>(() => model.ImportSettingsJson(json));
        Assert.Equal("a", model.StartAspect?.Id);
        Assert.Equal(3, model.Aspects.Count);
        Assert.True(model.Aspects[0].IsEnabled);
    }

    [Fact]
    public void InvalidSettingsCannotBeExportedAsAnUnrestorableFile()
    {
        var model = Create(); model.Aspects[0].WeightText = "-1";
        Assert.Throws<InvalidDataException>(model.ExportSettingsJson);
    }

    private static GtnhResearchHelperViewModel Create()
    { var model = new GtnhResearchHelperViewModel(); model.LoadData(Data, "人工测试关系图"); return model; }
}
