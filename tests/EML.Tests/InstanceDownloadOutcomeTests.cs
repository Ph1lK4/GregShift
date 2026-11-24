using EML.App.Services;
using EML.App.ViewModels;
using EML.Core.Downloads;
using EML.Core.Minecraft;
using EML.Core.Minecraft.Catalog;

namespace EML.Tests;

/// <summary>升级编排使用本次真实导入路径和最终报告，不依赖当前选择、通知等级或状态文案。</summary>
public sealed class InstanceDownloadOutcomeTests
{
    [Theory]
    [InlineData("Prism")]
    [InlineData("CurseForge")]
    [InlineData("Modrinth")]
    [InlineData("Zip")]
    public async Task SuccessfulDownloadReturnsExactCommittedPathAndReadiness(string format)
    {
        var fixture = new Fixture(format);

        var outcome = await fixture.DownloadAsync();

        Assert.Equal(fixture.Imported.InstancePath, outcome.InstancePath);
        Assert.True(outcome.LaunchReady);
        Assert.Equal([fixture.Imported.InstancePath], fixture.Downloader.Paths);
        Assert.Single(fixture.Notifications.Notices);
        Assert.False(fixture.ViewModel.IsBusy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PathComesFromCommitEvenWhenScanOrSelectionDoesNotRepresentIt(bool scanThrows)
    {
        var fixture = new Fixture();
        var old = new MinecraftInstance("Old", "old-instance", "old-game", []);
        fixture.Locator.Find = () => scanThrows
            ? Task.FromException<IReadOnlyList<MinecraftInstance>>(new IOException("扫描失败"))
            : Task.FromResult<IReadOnlyList<MinecraftInstance>>([old]);
        fixture.Downloader.Download = (_, _) =>
        {
            fixture.ViewModel.SelectedInstance = old;
            return Task.FromResult(fixture.ReadyReport());
        };

        var outcome = await fixture.DownloadAsync();

        Assert.Same(old, fixture.ViewModel.SelectedInstance);
        Assert.Equal(fixture.Imported.InstancePath, outcome.InstancePath);
        Assert.True(outcome.LaunchReady);
        Assert.Single(fixture.Notifications.Notices);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("content")]
    [InlineData("runtime")]
    [InlineData("cancelled")]
    [InlineData("check-error")]
    [InlineData("no-check")]
    [InlineData("installer")]
    public async Task IncompleteFinalReportKeepsCommittedPathButNeverReportsReady(string problem)
    {
        var fixture = new Fixture();
        var report = fixture.ReadyReport();
        report = problem switch
        {
            "profile" => report with { CheckReport = report.CheckReport! with { LaunchProfilePath = null } },
            "content" => report with { CheckReport = report.CheckReport! with { ContentIssues = ["包内文件缺失"] } },
            "runtime" => report with { CheckReport = report.CheckReport! with { RuntimeIssues = ["Fabric 下载失败"] } },
            "cancelled" => report with { IsCanceled = true },
            "check-error" => report with { CheckError = "复查中断" },
            "no-check" => report with { CheckReport = null },
            "installer" => report with { Runtime = new PrismInstallResult { RuntimeIssues = ["运行依赖未完成"] } },
            _ => throw new InvalidOperationException()
        };
        fixture.Downloader.Download = (_, _) => Task.FromResult(report);

        var outcome = await fixture.DownloadAsync();

        Assert.Equal(fixture.Imported.InstancePath, outcome.InstancePath);
        Assert.False(outcome.LaunchReady);
        Assert.Single(fixture.Notifications.Notices);
        Assert.False(fixture.ViewModel.IsBusy);
    }

    [Fact]
    public async Task CompletionExceptionKeepsCommittedPathAndCanRetryLater()
    {
        var fixture = new Fixture();
        fixture.Downloader.Download = (_, _) => Task.FromException<InstanceDownloadReport>(new IOException("网络中断"));

        var outcome = await fixture.DownloadAsync();

        Assert.Equal(fixture.Imported.InstancePath, outcome.InstancePath);
        Assert.False(outcome.LaunchReady);
        Assert.Contains("网络中断", outcome.Message);
        Assert.Equal(OperationNoticeKind.Warning, Assert.Single(fixture.Notifications.Notices).Kind);
        Assert.False(fixture.ViewModel.IsBusy);
        Assert.True(fixture.ViewModel.DownloadMissingCommand.CanExecute(null));
    }

    [Fact]
    public async Task UnsupportedFormatKeepsCommittedPathWithoutClaimingReadiness()
    {
        var fixture = new Fixture("Unknown");

        var outcome = await fixture.DownloadAsync();

        Assert.Equal(fixture.Imported.InstancePath, outcome.InstancePath);
        Assert.False(outcome.LaunchReady);
        Assert.Empty(fixture.Downloader.Paths);
        Assert.Single(fixture.Notifications.Notices);
    }

    [Fact]
    public async Task ReadinessUsesFinalReportRatherThanWarningNoticeKind()
    {
        var fixture = new Fixture();
        var optional = new InstanceFileRequirement { Path = ".minecraft/optional.txt", Required = false };
        fixture.Downloader.Download = (_, _) => Task.FromResult(fixture.ReadyReport() with
        {
            Files = [new(optional, InstanceDownloadState.Unavailable, "可选文件没有下载来源")]
        });

        var outcome = await fixture.DownloadAsync();

        Assert.True(outcome.LaunchReady);
        Assert.Equal(OperationNoticeKind.Warning, Assert.Single(fixture.Notifications.Notices).Kind);
    }

    [Theory]
    [InlineData("prepare")]
    [InlineData("format")]
    [InlineData("confirmation")]
    [InlineData("import")]
    public async Task BeforeCommitFailureOrCancellationHasNoInstancePath(string stage)
    {
        var fixture = new Fixture(stage == "format" ? "Zip" : "Prism");
        if (stage == "confirmation") fixture.Dialogs.Confirm = false;
        if (stage == "import")
            fixture.Importer.Import = _ => Task.FromException<InstanceImportResult>(new IOException("解压失败"));
        Func<IProgress<FileDownloadProgress>?, CancellationToken, Task<PreparedInstanceArchive>> prepare = stage == "prepare"
            ? (_, _) => Task.FromException<PreparedInstanceArchive>(new HttpRequestException("下载失败"))
            : (_, _) => Task.FromResult(new PreparedInstanceArchive("cache/pack.zip", "GTNH 2.9.0", "Prism"));

        var outcome = await fixture.ViewModel.DownloadInstanceWithResultAsync(prepare, null, CancellationToken.None);

        Assert.Null(outcome.InstancePath);
        Assert.False(outcome.LaunchReady);
        Assert.Empty(fixture.Downloader.Paths);
        Assert.Single(fixture.Notifications.Notices);
        Assert.False(fixture.ViewModel.IsBusy);
    }

    [Fact]
    public async Task CancelDuringPrepareHasNoCommittedInstance()
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();

        var outcome = await fixture.ViewModel.DownloadInstanceWithResultAsync((_, token) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<PreparedInstanceArchive>(token);
        }, null, cancellation.Token);

        Assert.Null(outcome.InstancePath);
        Assert.False(outcome.LaunchReady);
        Assert.Equal(OperationNoticeKind.Cancelled, Assert.Single(fixture.Notifications.Notices).Kind);
        Assert.False(fixture.ViewModel.IsBusy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelAfterCommitNeverUsesReadyReportFromDownloaderIgnoringCancellation(bool dependencyCancel)
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Downloader.Download = (_, _) =>
        {
            if (dependencyCancel) fixture.ViewModel.CancelDependencyDownloadCommand.Execute(null);
            else cancellation.Cancel();
            return Task.FromResult(fixture.ReadyReport());
        };

        var outcome = await fixture.DownloadAsync(cancellation.Token);

        Assert.Equal(fixture.Imported.InstancePath, outcome.InstancePath);
        Assert.False(outcome.LaunchReady);
        Assert.Single(fixture.Notifications.Notices);
        Assert.False(fixture.ViewModel.IsBusy);
    }

    [Fact]
    public async Task OriginalStringEntryPointReturnsSameMessageWithOneNotice()
    {
        var structured = new Fixture();
        var legacy = new Fixture();

        var outcome = await structured.DownloadAsync();
        var message = await legacy.ViewModel.DownloadInstanceAsync((_, _) => Task.FromResult(legacy.Archive), null, CancellationToken.None);

        Assert.Equal(outcome.Message, message);
        Assert.Single(legacy.Notifications.Notices);
        Assert.False(legacy.ViewModel.IsBusy);
    }

    /// <summary>全部替身仅提供内存结果，误触发单独检查或实例写操作会直接失败。</summary>
    private sealed class Fixture
    {
        public InstanceImportResult Imported { get; }
        public PreparedInstanceArchive Archive { get; }
        public FakeImporter Importer { get; }
        public FakeDownloader Downloader { get; }
        public FakeLocator Locator { get; }
        public FakeDialogs Dialogs { get; } = new();
        public RecordingNotifications Notifications { get; } = new();
        public InstancesViewModel ViewModel { get; }

        public Fixture(string format = "Prism")
        {
            var root = Path.Combine(Path.GetTempPath(), "EML-download-outcome-tests", "instances");
            var path = Path.Combine(root, "GTNH 2.9.0");
            Imported = new(path, Path.Combine(path, ".minecraft"), new InstanceImportManifest
            {
                Name = "GTNH 2.9.0", SourceFormat = format, MinecraftVersion = "1.7.10"
            });
            Archive = new("cache/gtnh-prism.zip", Imported.Manifest.Name, format);
            Importer = new(Imported);
            Downloader = new() { Download = (_, _) => Task.FromResult(ReadyReport()) };
            Locator = new() { Find = () => Task.FromResult<IReadOnlyList<MinecraftInstance>>([]) };
            ViewModel = new(Locator, new UnusedManager(), Importer, Dialogs, root, new UnusedChecker(), Downloader,
                notifications: Notifications);
        }

        public Task<InstanceDownloadOutcome> DownloadAsync(CancellationToken token = default) =>
            ViewModel.DownloadInstanceWithResultAsync((_, _) => Task.FromResult(Archive), null, token);

        public InstanceDownloadReport ReadyReport() => new([], new InstanceCheckReport
        {
            InstancePath = Imported.InstancePath, Mode = InstanceCheckMode.Full, HasManifest = true,
            LaunchProfilePath = "launch.json"
        });
    }

    private sealed class FakeImporter(InstanceImportResult result) : IMinecraftInstanceImporter
    {
        public Func<CancellationToken, Task<InstanceImportResult>> Import { get; set; } = _ => Task.FromResult(result);
        public Task<InstanceImportPreview> InspectAsync(string archivePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InstanceImportPreview(result.Manifest.Name, result.Manifest.SourceFormat,
                "1.7.10", [], 1, 1, 0, []));
        public Task<InstanceImportResult> ImportAsync(string archivePath, string instanceName,
            IProgress<InstanceImportProgress>? progress = null, CancellationToken cancellationToken = default) => Import(cancellationToken);
    }

    private sealed class FakeDownloader : IMinecraftInstanceDownloader
    {
        public required Func<string, CancellationToken, Task<InstanceDownloadReport>> Download { get; set; }
        public List<string> Paths { get; } = [];
        public Task<InstanceDownloadReport> DownloadMissingAsync(string instancePath,
            IProgress<InstanceDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Paths.Add(instancePath);
            return Download(instancePath, cancellationToken);
        }
    }

    private sealed class FakeLocator : IMinecraftInstanceLocator
    {
        public required Func<Task<IReadOnlyList<MinecraftInstance>>> Find { get; set; }
        public Task<IReadOnlyList<MinecraftInstance>> FindAllAsync(CancellationToken cancellationToken = default) => Find();
    }

    private sealed class FakeDialogs : IInstanceDialogs
    {
        public bool Confirm { get; set; } = true;
        public string? ConfirmImport(InstanceImportPreview preview) => Confirm ? preview.SuggestedName : null;
        public string? ChooseImportPath() => throw new NotSupportedException();
        public bool ConfirmDelete(MinecraftInstance instance) => throw new NotSupportedException();
        public string? ChooseExportPath(MinecraftInstance instance) => throw new NotSupportedException();
    }

    private sealed class RecordingNotifications : IOperationNotifications
    {
        public List<OperationNotice> Notices { get; } = [];
        public void Report(OperationNotice notice) => Notices.Add(notice);
    }

    private sealed class UnusedChecker : IMinecraftInstanceChecker
    {
        public Task<InstanceCheckReport> CheckAsync(string instancePath, InstanceCheckMode mode = InstanceCheckMode.Full,
            IProgress<InstanceCheckProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedManager : IMinecraftInstanceManager
    {
        public Task DeleteAsync(string instancePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> RenameAsync(string instancePath, string newName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ExportAsync(string instancePath, string destinationZip, IProgress<InstanceExportProgress>? progress = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
