using EML.App.ViewModels;
using EML.Core.Downloads;
using EML.Core.Minecraft;
using EML.Core.Minecraft.Catalog;
using EML.Core.Minecraft.Mods.Online;

namespace EML.App.Tests;

/// <summary>目录页的隔离数据；不读取账号、不联网，也不写入实例。</summary>
internal sealed class CatalogPageFixture
{
    internal OnlineModsViewModel Mods { get; }
    public InstanceDownloadsViewModel Downloads { get; }
    public object Instances { get; } = new { TransferPercent = 0d, TransferStatus = "" };

    /// <summary>注入内存目录和禁止实际安装的委托，生产 ViewModel 仍负责导航与选择。</summary>
    internal CatalogPageFixture()
    {
        Mods = new(new ModCatalog(), new ModInstaller());
        Mods.SetInstance(new("GTNH 2.9.0", @"preview:\GTNH", @"preview:\GTNH\.minecraft", ["1.7.10"])
        { Versions = [new("forge", "1.7.10", "Forge", "unused")] });
        Downloads = new(new PackCatalog(), (_, _, _) => Task.FromException<string>(
            new InvalidOperationException("隔离预览不创建实例。")), () => true);
    }

    /// <summary>仅运行内存目录请求，完成后两页保留真实绑定的结果集合。</summary>
    internal async Task InitializeAsync()
    {
        Mods.SetActive(true); Downloads.SetActive(true);
        await Task.WhenAll(Mods.SearchTask, Downloads.SearchTask);
    }

    /// <summary>关闭时等待查询退出，避免测试留下派发器回调。</summary>
    internal Task StopAsync() => Task.WhenAll(Mods.StopAsync(), Downloads.StopAsync());

    private static readonly string[] ModNames = ["NeverEnoughCharacters Rework", "JourneyMap", "WorldEdit for GTNH", "AE2 Things"];
    private static readonly string[] ModSummaries = ["提供中文拼音搜索和输入辅助，让物品检索更加方便。", "查看世界地图、管理路径点，并保留探索记录。", "为大规模建造提供选择、复制和编辑工具。", "为应用能源网络提供额外的操作和存储功能。"];

    /// <summary>模拟项目与精确版本接口，长介绍用于验证真实滚动布局。</summary>
    private sealed class ModCatalog : IOnlineModCatalog
    {
        private static OnlineModProject Project(int index) => new()
        {
            Id = new(ModProvider.Modrinth, index.ToString()), Name = ModNames[index % 4], Author = "GTNH Community",
            Summary = ModSummaries[index % 4], Description = string.Join("\n\n", Enumerable.Repeat(ModSummaries[index % 4] + "安装前请核对游戏版本、加载器和服务器要求。", 12)),
            GameVersions = ["1.7.10"], Loaders = ["forge"], Downloads = 128000, Environment = "客户端", License = "MIT"
        };

        /// <summary>返回可滚动的固定目录，查询只改变展示集合。</summary>
        public Task<OnlineModSearchPage> SearchAsync(OnlineModQuery query, CancellationToken token = default) =>
            Task.FromResult(new OnlineModSearchPage(Enumerable.Range(0, 12).Select(Project).ToArray(), false, []));
        /// <summary>按身份返回同项目的介绍。</summary>
        public Task<OnlineModProject> GetProjectAsync(OnlineProjectId project, CancellationToken token = default) => Task.FromResult(Project(int.Parse(project.ProjectId)));
        /// <summary>两个精确版本供切换与保留选择测试。</summary>
        public Task<OnlineModVersionPage> GetVersionsAsync(OnlineProjectId project, string gameVersion, string loader, int page = 0, CancellationToken token = default) =>
            Task.FromResult(new OnlineModVersionPage([Version(project, "2.1.0"), Version(project, "2.0.0")], false));
        /// <summary>固定版本包含可选依赖和可信文件要求。</summary>
        public Task<OnlineModVersion> GetVersionAsync(OnlineProjectId project, string versionId, CancellationToken token = default) => Task.FromResult(Version(project, versionId));
        /// <summary>无下载发生，保留选择的文件对象。</summary>
        public Task<OnlineModFile> ResolveDownloadAsync(OnlineModVersion version, OnlineModFile file, CancellationToken token = default) => Task.FromResult(file);
    }

    /// <summary>固定文件只用于展示大小与摘要，不提供真实下载链接。</summary>
    private static OnlineModVersion Version(OnlineProjectId project, string id) => new()
    {
        Project = project, Id = id, Name = id, VersionNumber = id, Channel = "release", GameVersions = ["1.7.10"], Loaders = ["forge"], Published = new(2026, 11, 18, 0, 0, 0, TimeSpan.Zero),
        Files = [new() { FileName = "gtnh-addon-" + id + ".jar", Primary = true, Requirement = new() { Size = 2097152, Hashes = new Dictionary<string, string> { ["sha1"] = new('a', 40) } } }],
        Dependencies = [new() { Key = "optional", Name = "额外界面支持", Kind = OnlineDependencyKind.Optional }],
        Changelog = "修复输入兼容性，改善中文搜索体验。"
    };

    /// <summary>只生成内存清单，实际安装入口始终拒绝写入。</summary>
    private sealed class ModInstaller : IOnlineModInstaller
    {
        /// <summary>清单沿用选定版本与依赖偏好。</summary>
        public Task<OnlineModInstallPlan> PlanAsync(OnlineModPlanRequest request, CancellationToken token = default)
        {
            var version = Version(request.Project, request.VersionId);
            return Task.FromResult(new OnlineModInstallPlan { Id = "preview", Request = request,
                Items = [new(version, version.Files[0], "用户选择", OnlineModPlanStatus.Download)] });
        }
        /// <summary>预览不允许修改游戏数据。</summary>
        public Task<OnlineModInstallResult> InstallAsync(OnlineModInstallPlan plan, IProgress<OnlineModInstallProgress>? progress = null, CancellationToken token = default) =>
            Task.FromException<OnlineModInstallResult>(new InvalidOperationException("隔离预览不安装模组。"));
    }

    /// <summary>模拟三种下载来源，官方原版仍使用相同的列表与详情导航。</summary>
    private sealed class PackCatalog : IInstanceCatalog
    {
        private static readonly string[] Names = ["GT New Horizons", "Prominence II: Hasturian Era", "Cobblemon Official Modpack", "All the Mods"];
        /// <summary>返回真实项目形状的目录，不访问远程平台。</summary>
        public Task<InstanceCatalogPage> SearchAsync(InstanceCatalogQuery query, CancellationToken cancellationToken = default) => Task.FromResult(new InstanceCatalogPage(
            Enumerable.Range(0, 12).Select(index => new InstanceCatalogProject
            {
                Provider = query.Provider, Id = index.ToString(), Name = query.Provider == InstanceCatalogProvider.Minecraft ? "Minecraft 1.21." + index : Names[index % 4], Author = "Community",
                Summary = "探索、建造与长线发展。请选择适合自己的版本，下载后自动导入并补全依赖。", Downloads = 365000
            }).ToArray(), 12, false));
        /// <summary>长平台正文用于检查可复制内容和滚动。</summary>
        public Task<InstanceCatalogProject> GetProjectAsync(InstanceCatalogProject project, CancellationToken cancellationToken = default) =>
            Task.FromResult(project with { Description = string.Join("\n\n", Enumerable.Repeat("从简单的起点开始，逐步建立你的世界。作者提供的中文介绍与 English description 均保持平台原文。", 14)) });
        /// <summary>版本选择不得改变项目身份。</summary>
        public Task<InstanceVersionPage> GetVersionsAsync(InstanceCatalogProject project, string gameVersion = "", string loader = "", int offset = 0, int limit = 20, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InstanceVersionPage([Release(project, "2.9.0"), Release(project, "2.8.4")], 2, false));
        /// <summary>离线目录无需重解析下载链接。</summary>
        public Task<InstanceCatalogRelease> GetReleaseAsync(InstanceCatalogProvider provider, string projectId, string releaseId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Release(new() { Provider = provider, Id = projectId, Name = "GTNH" }, releaseId));
        /// <summary>提供固定发布记录但不执行下载。</summary>
        private static InstanceCatalogRelease Release(InstanceCatalogProject project, string id) => new()
        {
            Provider = project.Provider, ProjectId = project.Id, Id = id, Name = id, VersionNumber = id,
            GameVersions = [project.Provider == InstanceCatalogProvider.Minecraft ? "1.21.1" : "1.7.10"], Loaders = project.Provider == InstanceCatalogProvider.Minecraft ? [] : ["forge"],
            ReleaseType = "正式版", FileName = "GTNH-" + id + ".mrpack", Size = 104857600,
            Hashes = new Dictionary<string, string> { ["sha1"] = new('a', 40) }, DownloadUrls = ["https://example.invalid/preview"]
        };
    }
}
