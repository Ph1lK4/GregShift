using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EML.Core.Minecraft.Gtnh.Tools;

namespace EML.App.ViewModels;

public sealed partial class GtnhResearchAspectOption : ObservableObject
{
    public GtnhResearchAspectOption(GtnhResearchAspect aspect)
    { Aspect = aspect; weightText = aspect.Weight.ToString("G", CultureInfo.InvariantCulture); }
    public GtnhResearchAspect Aspect { get; }
    public string Id => Aspect.Id;
    public string Label => $"{Aspect.Name} · {Aspect.Id}";
    public string Components => Aspect.Components.Count == 0 ? "原始要素" : string.Join(" + ", Aspect.Components);
    [ObservableProperty] private bool isEnabled = true;
    [NotifyPropertyChangedFor(nameof(IsWeightValid))]
    [ObservableProperty] private string weightText;
    public bool IsWeightValid => double.TryParse(WeightText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
        double.IsFinite(number) && number > 0 && number <= 1_000_000_000_000;
}

/// <summary>原生研究助手只保存要素偏好，不读取或修改游戏研究进度。</summary>
public sealed partial class GtnhResearchHelperViewModel : ObservableObject
{
    private int _revision;
    public ObservableCollection<GtnhResearchAspectOption> Aspects { get; } = [];
    public ObservableCollection<GtnhResearchAspectOption> VisibleAspects { get; } = [];
    public ObservableCollection<GtnhResearchAspect> Path { get; } = [];
    [ObservableProperty] private GtnhResearchAspectOption? startAspect;
    [ObservableProperty] private GtnhResearchAspectOption? endAspect;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string minimumIntermediateText = "0";
    [ObservableProperty] private bool isWeighted;
    [ObservableProperty] private string status = "请先读取要素数据；计算在本机完成，不修改研究进度。";
    [ObservableProperty] private string dataLabel = "尚未读取要素数据";
    [ObservableProperty] private string resultSummary = "选择起点、终点和可用要素，再计算连接链。";
    public bool HasData => Aspects.Count > 0;
    public bool HasResult => Path.Count > 0;
    public bool HasInvalidWeights => Aspects.Any(item => !item.IsWeightValid);
    public string PathText => GetPathText();
    public string AvailableSummary => $"可用 {Aspects.Count(item => item.IsEnabled)} / {Aspects.Count} 个要素";

    public void LoadData(string json, string? sourceLabel = null)
    {
        var data = GtnhResearchData.ParseWikiJson(json);
        var previous = Aspects.ToDictionary(item => item.Id, item => (item.IsEnabled, item.WeightText));
        var start = StartAspect?.Id;
        var end = EndAspect?.Id;
        foreach (var item in Aspects) item.PropertyChanged -= OnAspectChanged;
        Aspects.Clear();
        foreach (var aspect in data)
        {
            var item = new GtnhResearchAspectOption(aspect);
            if (previous.TryGetValue(item.Id, out var old)) { item.IsEnabled = old.IsEnabled; item.WeightText = old.WeightText; }
            item.PropertyChanged += OnAspectChanged;
            Aspects.Add(item);
        }
        StartAspect = Aspects.FirstOrDefault(item => item.Id == start);
        EndAspect = Aspects.FirstOrDefault(item => item.Id == end);
        DataLabel = sourceLabel ?? "已读取要素数据，请核对适用的 GTNH 版本";
        ApplyFilter(); InvalidateResult(); OnPropertyChanged(nameof(HasData)); OnPropertyChanged(nameof(AvailableSummary));
        CalculateCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasInvalidWeights));
        Status = $"已读取 {Aspects.Count} 个要素；选择起点和终点后即可计算。";
    }

    [RelayCommand(CanExecute = nameof(HasData))]
    private async Task CalculateAsync(CancellationToken cancellationToken)
    {
        if (!int.TryParse(MinimumIntermediateText, NumberStyles.None, CultureInfo.InvariantCulture, out var minimum) ||
            minimum is < 0 or > GtnhResearchPathFinder.MaximumIntermediateCount)
        { Status = $"最少中间要素数须为 0～{GtnhResearchPathFinder.MaximumIntermediateCount} 的整数。"; return; }
        if (StartAspect is null || EndAspect is null) { Status = "请先选择起点和终点要素。"; return; }
        var data = new List<GtnhResearchAspect>();
        foreach (var item in Aspects)
        {
            if (!double.TryParse(item.WeightText, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight) ||
                !double.IsFinite(weight) || weight <= 0 || weight > 1_000_000_000_000)
            { Status = $"{item.Label} 的权重无效，请输入大于 0 的有限数值（使用小数点）。"; return; }
            data.Add(item.Aspect with { Weight = weight });
        }
        var revision = _revision;
        var start = StartAspect.Id;
        var end = EndAspect.Id;
        var weighted = IsWeighted;
        var disabled = Aspects.Where(item => !item.IsEnabled).Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Status = "正在本机计算连接链……";
        try
        {
            var result = await Task.Run(() => GtnhResearchPathFinder.Find(data, start, end, minimum, weighted, disabled, cancellationToken), cancellationToken);
            if (revision != _revision) { Status = "计算条件已改变，请重新计算。"; return; }
            Path.Clear();
            foreach (var aspect in result.Path) Path.Add(aspect);
            OnPropertyChanged(nameof(HasResult));
            OnPropertyChanged(nameof(PathText));
            ResultSummary = result.Success ? $"{result.IntermediateCount} 个中间要素" +
                (weighted ? $" · 连接成本 {result.Cost.ToString("0.######", CultureInfo.InvariantCulture)}" : " · 最少连接步数") : result.Message;
            Status = result.Success ? "已找到连接链；请根据实际研究笔记布置，结果不会自动填入游戏。" : result.Message;
        }
        catch (OperationCanceledException) { Status = "已取消计算，可调整条件后重试。"; }
    }

    [RelayCommand] private void EnableAll() { foreach (var item in Aspects) item.IsEnabled = true; }
    [RelayCommand] private void DisableAll() { foreach (var item in Aspects) item.IsEnabled = false; }
    [RelayCommand] private void SwapEndpoints() { (StartAspect, EndAspect) = (EndAspect, StartAspect); }
    [RelayCommand] private void ResetWeights()
    { foreach (var item in Aspects) item.WeightText = item.Aspect.Weight.ToString("G", CultureInfo.InvariantCulture); Status = "已恢复本次要素数据提供的默认权重。"; }

    public string GetPathText() => string.Join(" → ", Path.Select(item => $"{item.Name} ({item.Id})"));

    public string ExportSettingsJson()
    {
        if (!int.TryParse(MinimumIntermediateText, NumberStyles.None, CultureInfo.InvariantCulture, out var minimum) ||
            minimum is < 0 or > GtnhResearchPathFinder.MaximumIntermediateCount)
            throw new InvalidDataException("请先修正最少中间要素数（0～64），再保存设置。");
        if (Aspects.FirstOrDefault(item => !item.IsWeightValid) is { } invalid)
            throw new InvalidDataException($"{invalid.Label} 的权重无效，请修正后再保存设置。");
        return JsonSerializer.Serialize(new ResearchSettings(1, StartAspect?.Id, EndAspect?.Id,
            MinimumIntermediateText, IsWeighted, Aspects.Select(item => new AspectSetting(item.Id, item.IsEnabled, item.WeightText)).ToArray()),
            new JsonSerializerOptions { WriteIndented = true });
    }

    public void ImportSettingsJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > 256 * 1024)
            throw new InvalidDataException("研究助手设置必须为非空 JSON，且不超过 256 KiB。");
        ResearchSettings settings;
        try { settings = JsonSerializer.Deserialize<ResearchSettings>(json) ?? throw new InvalidDataException("设置内容为空。"); }
        catch (JsonException ex) { throw new InvalidDataException("这不是有效的研究助手设置。", ex); }
        if (settings.Version != 1 || settings.Aspects is null || settings.Aspects.Length > GtnhResearchPathFinder.MaximumAspectCount ||
            settings.Aspects.Any(item => item is null || string.IsNullOrWhiteSpace(item.Id) || item.WeightText is null || item.WeightText.Length > 40 ||
                !double.TryParse(item.WeightText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                !double.IsFinite(number) || number <= 0 || number > 1_000_000_000_000) ||
            settings.Aspects.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != settings.Aspects.Length ||
            !int.TryParse(settings.MinimumIntermediate, NumberStyles.None, CultureInfo.InvariantCulture, out var minimum) ||
            minimum is < 0 or > GtnhResearchPathFinder.MaximumIntermediateCount)
            throw new InvalidDataException("研究助手设置的版本、权重或最少中间数无效。");
        // 全部验证后再应用，失败不污染当前条件；未知要素会被忽略，便于跨数据版本保留设置。
        var options = settings.Aspects.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var item in Aspects)
            if (options.TryGetValue(item.Id, out var saved)) { item.IsEnabled = saved.Enabled; item.WeightText = saved.WeightText; }
        StartAspect = Aspects.FirstOrDefault(item => string.Equals(item.Id, settings.Start, StringComparison.OrdinalIgnoreCase));
        EndAspect = Aspects.FirstOrDefault(item => string.Equals(item.Id, settings.End, StringComparison.OrdinalIgnoreCase));
        MinimumIntermediateText = settings.MinimumIntermediate; IsWeighted = settings.Weighted;
        InvalidateResult(); Status = "已恢复研究助手设置；请重新计算连接链。";
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnStartAspectChanged(GtnhResearchAspectOption? value) => InvalidateResult();
    partial void OnEndAspectChanged(GtnhResearchAspectOption? value) => InvalidateResult();
    partial void OnMinimumIntermediateTextChanged(string value) => InvalidateResult();
    partial void OnIsWeightedChanged(bool value) => InvalidateResult();
    private void OnAspectChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GtnhResearchAspectOption.IsEnabled) or nameof(GtnhResearchAspectOption.WeightText))
        {
            InvalidateResult(); OnPropertyChanged(nameof(AvailableSummary)); OnPropertyChanged(nameof(HasInvalidWeights));
            if (e.PropertyName == nameof(GtnhResearchAspectOption.WeightText))
                Status = HasInvalidWeights ? "存在无效权重，可点击“定位无效权重”修正。" : "权重已更新，请重新计算连接链。";
        }
    }
    private void InvalidateResult()
    { _revision++; Path.Clear(); ResultSummary = "条件更改后请重新计算连接链。"; OnPropertyChanged(nameof(HasResult)); OnPropertyChanged(nameof(PathText)); }
    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        VisibleAspects.Clear();
        foreach (var item in Aspects.Where(item => query.Length == 0 || item.Label.Contains(query, StringComparison.OrdinalIgnoreCase)))
            VisibleAspects.Add(item);
    }
    private sealed record ResearchSettings(int Version, string? Start, string? End, string MinimumIntermediate, bool Weighted, AspectSetting[] Aspects);
    private sealed record AspectSetting(string Id, bool Enabled, string WeightText);
}
