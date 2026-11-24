using System.IO;
using System.Text.RegularExpressions;
using EML.App.ViewModels;
using EML.Core.Minecraft.Gtnh.Tools;

namespace EML.App.Tests;

public sealed class GtnhOreRegexViewModelTests
{
    [Fact]
    public void DoesNotPretendToHaveDataBeforeCatalogLoads()
    {
        var model = new GtnhOreRegexViewModel();
        Assert.False(model.HasCatalog); Assert.Empty(model.Materials); Assert.Empty(model.Results);
        Assert.Throws<InvalidOperationException>(model.ExportPlanJson);
    }

    [Fact]
    public void RepeatedChineseAndEnglishSearchKeepsMaterialChoices()
    {
        var model = Create();
        model.SearchText = "铁"; Assert.Equal("Iron", Assert.Single(model.Materials).Id);
        model.SelectedRouteId = "None";
        model.SearchText = "gold"; Assert.Equal("Gold", Assert.Single(model.Materials).Id);
        model.SelectedRouteId = "MPS"; model.SelectedProcessingId = "electrolyzer";
        model.SearchText = "铁"; Assert.Equal("None", model.SelectedRouteId);
        model.SearchText = "";
        Assert.Equal(2, model.Materials.Count); Assert.Equal("None", model.GetPlan().Routes["Iron"]);
        Assert.Equal("MPS", model.GetPlan().Routes["Gold"]); Assert.Equal("electrolyzer", model.GetPlan().Processing["Gold"]);
    }

    [Fact]
    public void RouteAndGeologyChangesRebuildRulesAndNotifyPersistence()
    {
        var model = Create(); var notifications = 0; model.PlanChanged += (_, _) => notifications++;
        model.SelectedMaterial = model.Materials.Single(item => item.Id == "Iron");
        model.SelectedRouteId = "None";
        var result = model.Results.Single(item => item.MachineId == "macerator");
        Assert.DoesNotMatch(new Regex(result.Expression), "oreIron");
        model.ClearGeologyCommand.Execute(null);
        Assert.All(model.Geology, item => Assert.False(item.IsSelected));
        Assert.True(notifications >= 2);
    }

    [Fact]
    public void PlanRoundTripPreservesInputsAndFailedImportLeavesExistingPlan()
    {
        var source = Create(); source.CommonRouteId = "HHW"; source.IntegratedFactory = true; source.CharacterLimit = 256;
        source.SelectedMaterial = source.Materials.Single(item => item.Id == "Gold"); source.SelectedRouteId = "MPS";
        source.SelectedProcessingId = "electrolyzer"; source.ClearGeologyCommand.Execute(null);
        var target = Create(); target.ImportPlanJson(source.ExportPlanJson());
        Assert.Equal(source.ExportPlanJson(), target.ExportPlanJson());
        var previous = target.ExportPlanJson();
        Assert.Throws<InvalidDataException>(() => target.ImportPlanJson("{\"schemaVersion\":99}"));
        Assert.Equal(previous, target.ExportPlanJson());
    }

    [Fact]
    public void InvalidPublicInputsClearStaleResultsAndCannotBeExported()
    {
        var model = Create(); Assert.NotEmpty(model.Results);
        model.CharacterLimit = 3;
        Assert.Empty(model.Results); Assert.False(model.HasResults);
        Assert.Throws<InvalidDataException>(model.ExportPlanJson);
    }

    [Fact]
    public void BindingSelectionTransitionsDoNotThrowOrOverwriteAChoice()
    {
        var model = Create(); model.SelectedRouteId = "None"; model.SelectedRouteId = null!;
        model.SelectedProcessingId = null!;
        Assert.Equal("None", model.GetPlan().Routes[model.SelectedMaterial!.Id]);
    }

    private static GtnhOreRegexViewModel Create()
    {
        var model = new GtnhOreRegexViewModel(); model.LoadCatalog(new GtnhOreCatalog(
            [new("Iron", "铁", "Iron", "iron", "common", false, [], false),
             new("Gold", "金", "Gold", "gold", "common", false, ["electrolyzer"], false)],
            [new("Granite", "花岗岩")])); return model;
    }
}
