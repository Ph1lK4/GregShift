using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EML.Core.Minecraft.Gtnh.Tools;

namespace EML.App.ViewModels;

/// <summary>只管理本机处理方案；原站表格读取和方案持久化由工具宿主负责。</summary>
public sealed partial class GtnhOreRegexViewModel : ObservableObject
{
    private GtnhOreCatalog? _catalog;
    private readonly Dictionary<string, string> _routes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _processing = new(StringComparer.Ordinal);
    private bool _loading;
    public event EventHandler? PlanChanged;
    public event EventHandler? RefreshRequested;
    public ObservableCollection<GtnhOreMaterial> Materials { get; } = [];
    public ObservableCollection<GtnhOreGeologyChoice> Geology { get; } = [];
    public ObservableCollection<GtnhOreRegexResult> Results { get; } = [];
    public IReadOnlyList<GtnhOreRoute> CommonRoutes => GtnhOreRoutes.Common;
    public IReadOnlyList<GtnhOreRoute> MaterialRoutes { get; } = [new("common", "跟随通用路线", []), .. GtnhOreRoutes.All];
    public IReadOnlyList<int> CharacterLimits => [256, 1024];
    public IReadOnlyList<GtnhOreProcessingChoice> ProcessingChoices => [new("", "不分解"), .. (SelectedMaterial?.AvailableProcessing ?? [])
        .Select(id => new GtnhOreProcessingChoice(id, GtnhOreRoutes.MachineName(id)))];
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string commonRouteId = "MPMC";
    [ObservableProperty] private bool integratedFactory;
    [ObservableProperty] private int characterLimit = 1024;
    [ObservableProperty] private GtnhOreMaterial? selectedMaterial;
    [ObservableProperty] private string selectedRouteId = "common";
    [ObservableProperty] private string selectedProcessingId = "";
    [ObservableProperty] private string status = "首次使用请读取维基矿物数据；成功读取后可离线使用。";
    [ObservableProperty] private string dataStatus = "尚未载入矿物表";
    [ObservableProperty] private bool isLoading;
    public bool HasCatalog => _catalog is not null;
    public bool HasSelection => SelectedMaterial is not null;
    public bool HasResults => Results.Count > 0;
    public bool NoSearchResults => HasCatalog && Materials.Count == 0;
    public string MaterialSummary => _catalog is null ? "等待矿物数据" : $"{Materials.Count} / {_catalog.Materials.Count} 种矿物";
    public string SelectedMaterialNote => SelectedMaterial is null ? "选择矿物可查看或调整专用路线。" :
        (SelectedMaterial.NeedsReview ? "此矿物的推荐路线存在争议，请结合副产物需求自行选择。" : "通用路线用于未单独指定的矿物；专用路线只影响当前矿物。") +
        (SelectedMaterial.AvailableProcessing.Count == 0 ? " 当前数据未列出可用的粉末分解方式。" : " 分解设置只匹配最终粉末，不接收纯净/不纯粉。 ");

    public void LoadCatalog(GtnhOreCatalog catalog, GtnhOrePlan? plan = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        plan ??= _catalog is null ? GtnhOrePlan.Defaults(catalog) : GetPlan();
        plan.Validate(catalog);
        _loading = true;
        try
        {
            foreach (var item in Geology) item.PropertyChanged -= OnGeologyChanged;
            _catalog = catalog; Geology.Clear();
            foreach (var variant in catalog.Geology)
            {
                var item = new GtnhOreGeologyChoice(variant.Prefix, variant.Name, plan.GeologyPrefixes.Contains(variant.Prefix, StringComparer.Ordinal));
                item.PropertyChanged += OnGeologyChanged; Geology.Add(item);
            }
            ApplyPlanSettings(plan); ApplyFilter();
            DataStatus = $"{GtnhOreCatalog.DataVersion} · {catalog.Materials.Count} 种矿物 · {catalog.Geology.Count} 种地质变种";
            OnPropertyChanged(nameof(HasCatalog));
        }
        finally { _loading = false; }
        Rebuild(false);
    }

    public GtnhOrePlan GetPlan()
    {
        if (_catalog is null) throw new InvalidOperationException("请先读取矿物数据。");
        var plan = new GtnhOrePlan { CommonRouteId = CommonRouteId, IntegratedFactory = IntegratedFactory, CharacterLimit = CharacterLimit,
            Routes = new(_routes, StringComparer.Ordinal), Processing = new(_processing, StringComparer.Ordinal),
            GeologyPrefixes = Geology.Where(item => item.IsSelected).Select(item => item.Prefix).ToArray() };
        plan.Validate(_catalog); return plan;
    }
    public string ExportPlanJson() => GetPlan().ToJson();
    public void ImportPlanJson(string json)
    {
        if (_catalog is null) throw new InvalidOperationException("请先读取矿物数据。");
        var plan = GtnhOrePlan.FromJson(json, _catalog);
        _loading = true;
        try { ApplyPlanSettings(plan); foreach (var variant in Geology) variant.IsSelected = plan.GeologyPrefixes.Contains(variant.Prefix, StringComparer.Ordinal); }
        finally { _loading = false; }
        Rebuild(); Status = "已导入处理方案；只更改本工具设置，不修改游戏实例。";
    }
    public void SetLoading(bool loading, string? message = null) { IsLoading = loading; if (message is not null) Status = message; }
    [RelayCommand] private void RefreshData() => RefreshRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void ResetPlan()
    {
        if (_catalog is null) return;
        ImportPlanJson(GtnhOrePlan.Defaults(_catalog).ToJson()); Status = "已恢复当前数据版本的推荐方案。";
    }
    [RelayCommand] private void SelectAllGeology() => SetGeology(true);
    [RelayCommand] private void ClearGeology() => SetGeology(false);
    private void SetGeology(bool selected)
    {
        _loading = true; try { foreach (var item in Geology) item.IsSelected = selected; } finally { _loading = false; }
        Rebuild();
    }
    private void ApplyPlanSettings(GtnhOrePlan plan)
    {
        _routes.Clear(); foreach (var item in plan.Routes) _routes.Add(item.Key, item.Value);
        _processing.Clear(); foreach (var item in plan.Processing) _processing.Add(item.Key, item.Value);
        CommonRouteId = plan.CommonRouteId; IntegratedFactory = plan.IntegratedFactory; CharacterLimit = plan.CharacterLimit;
        UpdateSelection();
    }
    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnCommonRouteIdChanged(string value) => Rebuild();
    partial void OnIntegratedFactoryChanged(bool value) => Rebuild();
    partial void OnCharacterLimitChanged(int value) => Rebuild();
    partial void OnSelectedMaterialChanged(GtnhOreMaterial? value) => UpdateSelection();
    private void UpdateSelection()
    {
        var before = _loading; _loading = true;
        try { SelectedRouteId = SelectedMaterial is null ? "common" : _routes.GetValueOrDefault(SelectedMaterial.Id, "common");
            SelectedProcessingId = SelectedMaterial is null ? "" : _processing.GetValueOrDefault(SelectedMaterial.Id, ""); }
        finally { _loading = before; }
        OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(ProcessingChoices)); OnPropertyChanged(nameof(SelectedMaterialNote));
    }
    partial void OnSelectedRouteIdChanged(string value)
    {
        if (_loading || SelectedMaterial is null || string.IsNullOrEmpty(value)) return;
        if (value == "common") _routes.Remove(SelectedMaterial.Id); else _routes[SelectedMaterial.Id] = value;
        Rebuild();
    }
    partial void OnSelectedProcessingIdChanged(string value)
    {
        if (_loading || SelectedMaterial is null || value is null) return;
        if (value.Length == 0) _processing.Remove(SelectedMaterial.Id); else _processing[SelectedMaterial.Id] = value;
        Rebuild();
    }
    private void ApplyFilter()
    {
        var selectedId = SelectedMaterial?.Id; Materials.Clear();
        foreach (var item in (_catalog?.Materials ?? []).Where(item => string.IsNullOrWhiteSpace(SearchText) ||
                     item.Label.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase))) Materials.Add(item);
        SelectedMaterial = Materials.FirstOrDefault(item => item.Id == selectedId) ?? Materials.FirstOrDefault();
        OnPropertyChanged(nameof(MaterialSummary)); OnPropertyChanged(nameof(NoSearchResults));
    }
    private void OnGeologyChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == nameof(GtnhOreGeologyChoice.IsSelected)) Rebuild(); }
    private void Rebuild(bool persist = true)
    {
        if (_loading || _catalog is null) return;
        try
        {
            var results = GtnhOreRegexGenerator.Generate(_catalog, GetPlan()); Results.Clear();
            foreach (var result in results) Results.Add(result);
            Status = results.Any(item => item.IsOverLimit) ? "部分规则超过过滤卡上限，请先调整方案，不要截断后使用。" :
                results.Count == 0 ? "当前方案不接收任何矿物。" : "规则已更新。通用路线包含未单独配置的矿物；使用前请核对游戏内机器与副产物需求。";
            OnPropertyChanged(nameof(HasResults)); if (persist) PlanChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (InvalidDataException ex) { Results.Clear(); OnPropertyChanged(nameof(HasResults)); Status = ex.Message; }
    }
}

public sealed partial class GtnhOreGeologyChoice(string prefix, string name, bool selected) : ObservableObject
{
    public string Prefix { get; } = prefix;
    public string Name { get; } = name;
    [ObservableProperty] private bool isSelected = selected;
}
public sealed record GtnhOreProcessingChoice(string Id, string Name);
