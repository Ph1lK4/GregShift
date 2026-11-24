using EML.App.Services;
using EML.App.ViewModels;
using EML.Core.Downloads;
using EML.Core.Minecraft;
using EML.Core.Minecraft.Catalog;
using EML.Core.Minecraft.Gtnh;
using EML.Core.Minecraft.Migration;

namespace EML.Tests;

/// <summary>从旧实例直接下载新版的完整编排；替身只返回内存结果，不联网、不读取玩家存档。</summary>
public sealed class GtnhDirectUpgradeTests
{
    [Fact]
    public async Task SingleOldInstanceCanChooseAndDownloadNewerRemoteVersion()
    {
        await using var app = new Fixture();
        await app.PrepareAsync();

        Assert.Single(app.Instances.Instances);
        Assert.True(app.Hub.IsMigration);
        Assert.False(app.Hub.IsPackDownloads);
        Assert.True(app.Hub.IsPreparingUpgrade);
        Assert.Equal(1, app.Services.VersionSelections);
        Assert.True(app.Packs.IsUpgrade);
        Assert.Equal("2.9.0", Assert.Single(app.Packs.Releases).Version);
        Assert.Null(app.Packs.SelectedRelease);
        Assert.False(app.Packs.InstallCommand.CanExecute(null));
        Assert.Equal(0, app.Services.Imports);
        Assert.Equal(0, app.Services.Migrations);
        Assert.Contains(app.Services.Old.Name, app.Packs.UpgradeHint);
        app.Hub.ShowPackDownloadsCommand.Execute(null);
        Assert.True(app.Hub.IsMigration);
        Assert.False(app.Hub.IsPackDownloads);
        Assert.True(app.Packs.IsUpgrade);
    }

    [Fact]
    public async Task CancellingVersionDialogDoesNotPrepareArchiveOrLeaveUpgradePage()
    {
        await using var app = new Fixture();
        await app.PrepareAsync();

        await app.CompleteSelectionAsync(null);

        Assert.True(app.Hub.IsMigration);
        Assert.False(app.Hub.IsPackDownloads);
        Assert.False(app.Hub.IsPreparingUpgrade);
        Assert.False(app.Packs.IsUpgrade);
        Assert.Null(app.Packs.SelectedRelease);
        Assert.Equal(0, app.Services.ArchivePreparations);
        Assert.Equal(0, app.Services.Imports);
        Assert.Equal(0, app.Services.Migrations);
        Assert.False(app.Hub.HasPreparedUpgrade);
        Assert.True(app.Hub.PrepareUpgradeDownloadCommand.CanExecute(null));
        Assert.Contains(app.Services.Old, app.Instances.Instances);
    }

    [Theory]
    [InlineData("2.8.4", "gtnh-2.8.4")]
    [InlineData("2.7.4", "gtnh-2.7.4")]
    [InlineData("2.9.0", "foreign-release")]
    public async Task DialogCannotAuthorizeOlderOrForeignRelease(string version, string id)
    {
        await using var app = new Fixture();
        await app.PrepareAsync();

        await app.CompleteSelectionAsync(FakeServices.Pack(version) with { Id = id });

        Assert.Equal(0, app.Services.ArchivePreparations);
        Assert.Equal(0, app.Services.Imports);
        Assert.Equal(0, app.Services.Migrations);
        Assert.False(app.Hub.HasPreparedUpgrade);
        Assert.True(app.Hub.IsMigration);
        Assert.False(app.Hub.IsPreparingUpgrade);
    }

    [Fact]
    public async Task ExplicitCancellationClosesVersionSelectionBeforeAnyDownload()
    {
        await using var app = new Fixture();
        await app.PrepareAsync();

        app.Hub.PrepareUpgradeDownloadCommand.Cancel();
        await app.CompleteSelectionAsync(null);

        Assert.True(app.Services.VersionSelectionToken.IsCancellationRequested);
        Assert.False(app.Hub.IsPreparingUpgrade);
        Assert.Equal(0, app.Services.ArchivePreparations);
        Assert.Equal(0, app.Services.Imports);
        Assert.False(app.Hub.HasPreparedUpgrade);
        Assert.True(app.Hub.IsMigration);
    }

    [Fact]
    public async Task StoppingHubCancelsAndWaitsForOpenVersionSelection()
    {
        await using var app = new Fixture();
        await app.PrepareAsync();

        await app.Hub.StopAsync();

        Assert.True(app.Services.VersionSelectionToken.IsCancellationRequested);
        Assert.False(app.Hub.IsPreparingUpgrade);
        Assert.False(app.Hub.PrepareUpgradeDownloadCommand.IsRunning);
        Assert.Equal(0, app.Services.ArchivePreparations);
        Assert.Equal(0, app.Services.Imports);
        Assert.False(app.Hub.HasPreparedUpgrade);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2.7.4")]
    [InlineData("2.9.0")]
    public async Task SourceIdentityIsRevalidatedAfterVersionDialog(string? changedVersion)
    {
        await using var app = new Fixture();
        await app.PrepareAsync();
        app.Services.OldVersion = changedVersion;

        await app.ConfirmVersionAsync();

        Assert.Equal(0, app.Services.ArchivePreparations);
        Assert.Equal(0, app.Services.Imports);
        Assert.Equal(0, app.Services.Migrations);
        Assert.False(app.Hub.HasPreparedUpgrade);
        Assert.Contains("变化", app.Hub.UpgradeStatus);
        Assert.Contains(app.Services.Old, app.Instances.Instances);
    }

    [Fact]
    public async Task RemovedSourceCannotStartDownloadAfterVersionDialog()
    {
        await using var app = new Fixture();
        await app.PrepareAsync();
        var choice = Assert.Single(app.Packs.Releases);
        app.Instances.Instances.Remove(app.Services.Old);

        await app.CompleteSelectionAsync(choice);

        Assert.Equal(0, app.Services.ArchivePreparations);
        Assert.Equal(0, app.Services.Imports);
        Assert.Equal(0, app.Services.Migrations);
        Assert.False(app.Hub.HasPreparedUpgrade);
    }

    [Fact]
    public async Task DownloadRemainsOnUpgradePageUntilPreparationFinishes()
    {
        await using var app = new Fixture();
        await app.PrepareAsync();
        var archive = new TaskCompletionSource<PreparedInstanceArchive>(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Services.Prepare = token => archive.Task.WaitAsync(token);
        var operation = app.ConfirmVersionAsync();
        try
        {
            await app.Services.ArchivePreparationEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(app.Hub.IsMigration);
            Assert.False(app.Hub.IsPackDownloads);
            Assert.True(app.Hub.IsPreparingUpgrade);
            Assert.True(app.Packs.IsInstalling);
            Assert.False(app.Hub.PrepareUpgradeDownloadCommand.CanExecute(null));
            Assert.False(app.Hub.ContinueUpgradeCommand.CanExecute(null));
            Assert.Equal(0, app.Services.Migrations);
            archive.SetResult(app.Services.Archive);
            await operation;

            Assert.True(app.Hub.IsMigration);
            Assert.False(app.Hub.IsPackDownloads);
            Assert.False(app.Hub.IsPreparingUpgrade);
            Assert.True(app.Hub.HasPreparedUpgrade);
        }
        finally { archive.TrySetResult(app.Services.Archive); await operation; }
    }

    [Fact]
    public async Task CompletedDownloadPinsExactNewInstanceAndOnlyOffersContinuation()
    {
        await using var app = new Fixture();
        await app.PrepareAsync();
        app.Services.AfterDownload = () => app.Instances.SelectedInstance = app.Services.Decoy;

        await app.ConfirmVersionAsync();

        Assert.True(app.Hub.HasPreparedUpgrade);
        Assert.True(app.Hub.IsMigration);
        Assert.Equal(app.Services.Old.Name, app.Hub.UpgradeSourceName);
        Assert.Contains(app.Services.Imported.Manifest.Name, app.Hub.UpgradeTargetName);
        Assert.Equal([app.Services.Imported.InstancePath], app.Services.DownloadPaths);
        Assert.Equal(0, app.Services.Migrations);
        Assert.Equal(0, app.Services.GroupSelections);
        Assert.Contains("先试运行", app.Hub.UpgradeStatus);
        Assert.False(app.Instances.IsBusy);
        Assert.False(app.Packs.IsInstalling);
    }

    [Fact]
    public async Task EndOfInstallationNotifiesContinuationCommandBecomesAvailable()
    {
        await using var app = new Fixture();
        await app.PrepareAsync();
        var enabledNotifications = 0;
        app.Hub.ContinueUpgradeCommand.CanExecuteChanged += (_, _) =>
        {
            if (app.Hub.ContinueUpgradeCommand.CanExecute(null)) enabledNotifications++;
        };

        await app.ConfirmVersionAsync();

        Assert.True(app.Hub.ContinueUpgradeCommand.CanExecute(null));
        Assert.True(app.Hub.TrialUpgradeCommand.CanExecute(null));
        Assert.True(enabledNotifications > 0);
    }

    [Theory]
    [InlineData("content")]
    [InlineData("runtime")]
    [InlineData("cancelled")]
    [InlineData("check-error")]
    [InlineData("exception")]
    public async Task PartialOrCancelledImportCannotStartMigration(string problem)
    {
        await using var app = new Fixture();
        await app.PrepareAsync();
        app.Services.Problem = problem;

        await app.ConfirmVersionAsync();

        Assert.False(app.Hub.HasPreparedUpgrade);
        Assert.False(app.Hub.ContinueUpgradeCommand.CanExecute(null));
        Assert.Equal(0, app.Services.Migrations);
        Assert.Equal(0, app.Services.GroupSelections);
        Assert.Equal(1, app.Services.Imports);
        Assert.Contains(app.Services.Old, app.Instances.Instances);
        Assert.Contains("旧实例未修改", app.Hub.UpgradeStatus);
        Assert.False(app.Instances.IsBusy);
    }

    [Fact]
    public async Task CancelledImportPreviewDoesNotInventPreparedTarget()
    {
        await using var app = new Fixture();
        await app.PrepareAsync();
        app.Services.AcceptImport = false;

        await app.ConfirmVersionAsync();

        Assert.Single(app.Instances.Instances);
        Assert.Equal(0, app.Services.Imports);
        Assert.False(app.Hub.HasPreparedUpgrade);
        Assert.Equal(0, app.Services.GroupSelections);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2.8.4")]
    [InlineData("2.10.0")]
    public async Task ImportedIdentityMustMatchChosenReleaseAndBeNewer(string? importedVersion)
    {
        await using var app = new Fixture();
        await app.PrepareAsync();
        app.Services.ImportedVersion = importedVersion;

        await app.ConfirmVersionAsync();

        Assert.False(app.Hub.HasPreparedUpgrade);
        Assert.Equal(0, app.Services.Migrations);
        Assert.Contains("版本", app.Hub.UpgradeStatus);
        Assert.Contains(app.Services.Old, app.Instances.Instances);
        Assert.Contains(app.Notifications.Notices, notice => notice.Kind == OperationNoticeKind.Failure);
    }

    [Fact]
    public async Task CurrentSelectionCannotChangePinnedUpgradeDirectionOrMode()
    {
        await using var app = new Fixture();
        await app.PrepareAsync();
        await app.ConfirmVersionAsync();
        app.Instances.SelectedInstance = app.Services.Decoy;
        await app.Instances.MigrationStateLoadTask;
        app.Hub.UpgradeServerClient = true;

        await app.Hub.ContinueUpgradeCommand.ExecuteAsync(null);

        Assert.Equal(app.Services.Old.InstancePath, app.Services.LastRequest!.SourceInstance);
        Assert.Equal(app.Services.Imported.InstancePath, app.Services.LastRequest.TargetInstance);
        Assert.Equal(GtnhMigrationMode.ServerClient, app.Services.LastRequest.Mode);
        Assert.True(app.Services.LastRequest.ExtraModsRiskAccepted);
        Assert.Equal(1, app.Services.GroupSelections);
        Assert.Equal(1, app.Services.FinalConfirmations);
        Assert.Equal(1, app.Services.Migrations);
        Assert.False(app.Hub.HasPreparedUpgrade);
        Assert.Equal(app.Services.Imported.InstancePath, app.Instances.SelectedInstance!.InstancePath);
    }

    [Fact]
    public async Task FinalPreviewCancellationKeepsPreparedUpgradeForRetry()
    {
        await using var app = new Fixture();
        await app.PrepareAsync();
        await app.ConfirmVersionAsync();
        app.Services.AcceptMigration = null;

        await app.Hub.ContinueUpgradeCommand.ExecuteAsync(null);

        Assert.Equal(1, app.Services.GroupSelections);
        Assert.Equal(1, app.Services.FinalConfirmations);
        Assert.Equal(0, app.Services.Migrations);
        Assert.True(app.Hub.HasPreparedUpgrade);
        Assert.True(app.Hub.ContinueUpgradeCommand.CanExecute(null));
        Assert.Contains("取消", app.Hub.UpgradeStatus);
    }

    [Fact]
    public async Task IncompleteMigrationCannotConsumePreparedUpgrade()
    {
        await using var app = new Fixture();
        await app.PrepareAsync();
        await app.ConfirmVersionAsync();
        app.Services.MigrationResultStatus = "PendingRecovery";

        await app.Hub.ContinueUpgradeCommand.ExecuteAsync(null);

        Assert.Equal(1, app.Services.Migrations);
        Assert.True(app.Hub.HasPreparedUpgrade);
        Assert.False(app.Instances.IsBusy);
    }

    [Fact]
    public async Task DownloadContextCannotBeChangedWhileArchiveIsBeingPrepared()
    {
        await using var app = new Fixture();
        await app.PrepareAsync();
        var wait = new TaskCompletionSource<PreparedInstanceArchive>(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Services.Prepare = token => wait.Task.WaitAsync(token);
        var operation = app.ConfirmVersionAsync();
        try
        {
            await app.Services.ArchivePreparationEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(app.Packs.IsInstalling);
            Assert.True(app.Hub.IsMigration);
            Assert.False(app.Hub.IsPackDownloads);
            app.Hub.ShowPackDownloadsCommand.Execute(null);
            Assert.True(app.Hub.IsMigration);
            Assert.False(app.Hub.IsPackDownloads);
            app.Instances.SelectedInstance = app.Services.Decoy;
            app.Packs.ConfigureUpgrade(new("elsewhere", "different", "elsewhere/game", "2.9.0"));
            Assert.True(app.Packs.IsUpgrade);
            Assert.Contains(app.Services.Old.Name, app.Packs.UpgradeHint);
            wait.SetResult(app.Services.Archive);
            await operation;
            Assert.Equal(app.Services.Old.Name, app.Hub.UpgradeSourceName);
            Assert.True(app.Hub.HasPreparedUpgrade);
            Assert.Equal(0, app.Services.Migrations);
        }
        finally { wait.TrySetResult(app.Services.Archive); await operation; }
    }

    [Fact]
    public async Task CancellationDuringFinalIdentityReadCannotPrepareMigration()
    {
        await using var app = new Fixture();
        await app.PrepareAsync();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Services.BeforeImportedInspection = (count, _) =>
        {
            if (count != 2) return Task.CompletedTask;
            reached.TrySetResult();
            return release.Task;
        };
        var operation = app.ConfirmVersionAsync();
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            app.Hub.PrepareUpgradeDownloadCommand.Cancel();
            release.SetResult();
            await operation;

            Assert.False(app.Hub.HasPreparedUpgrade);
            Assert.Equal(0, app.Services.GroupSelections);
            Assert.Equal(0, app.Services.Migrations);
            Assert.Contains(app.Services.Old, app.Instances.Instances);
        }
        finally { release.TrySetResult(); await operation; }
    }

    [Fact]
    public async Task PreparedTargetVersionIsRevalidatedBeforeShowingMigrationGroups()
    {
        await using var app = new Fixture();
        await app.PrepareAsync();
        await app.ConfirmVersionAsync();
        app.Services.ImportedVersion = "2.10.0";

        await app.Hub.ContinueUpgradeCommand.ExecuteAsync(null);

        Assert.Equal(0, app.Services.GroupSelections);
        Assert.Equal(0, app.Services.Migrations);
        Assert.True(app.Hub.HasPreparedUpgrade);
        Assert.Contains("发生变化", app.Hub.UpgradeStatus);
        Assert.False(app.Instances.IsBusy);
    }

    [Fact]
    public async Task ChangingSourceWhileChoosingVersionDoesNotDownloadForAnotherInstance()
    {
        await using var app = new Fixture();
        await app.PrepareAsync();

        app.Instances.SelectedInstance = app.Services.Decoy;
        await app.Instances.MigrationStateLoadTask;
        await app.CompleteSelectionAsync(FakeServices.Pack("2.9.0"));

        Assert.False(app.Packs.IsUpgrade);
        Assert.Null(app.Packs.SelectedRelease);
        Assert.DoesNotContain(app.Services.Old.Name, app.Hub.UpgradeSourceName);
        Assert.False(app.Hub.HasPreparedUpgrade);
        Assert.Equal(0, app.Services.ArchivePreparations);
        Assert.Equal(0, app.Services.Imports);
    }

    [Fact]
    public async Task CancellationWhileReadingGroupsDoesNotOpenMigrationDialog()
    {
        await using var app = new Fixture();
        await app.PrepareAsync();
        await app.ConfirmVersionAsync();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Services.BeforeGroups = () => { reached.TrySetResult(); return release.Task; };
        var operation = app.Hub.ContinueUpgradeCommand.ExecuteAsync(null);
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            app.Hub.ContinueUpgradeCommand.Cancel();
            release.SetResult();
            await operation;

            Assert.Equal(0, app.Services.GroupSelections);
            Assert.Equal(0, app.Services.Migrations);
            Assert.True(app.Hub.HasPreparedUpgrade);
        }
        finally { release.TrySetResult(); await operation; }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public FakeServices Services { get; } = new();
        public NoticeRecorder Notifications { get; } = new();
        public InstancesViewModel Instances { get; }
        public GtnhHubViewModel Hub => Instances.Gtnh!;
        public GtnhPackDownloadViewModel Packs => Hub.Packs!;
        private Task? _upgrade;
        public Fixture()
        {
            Instances = new(Services, Services, Services, Services, @"C:\direct-upgrade-test", Services, Services,
                migration: Services, migrationDialogs: Services, gtnhAddons: Services, gtnhPacks: Services,
                notifications: Notifications);
            Services.AfterDownload = () => { };
            Instances.Instances.Add(Services.Old);
            Instances.SelectedInstance = Services.Old;
        }
        public async Task OpenUpgradeAsync()
        {
            Hub.SetActive(true);
            await Hub.TargetLoadTask;
            await Instances.MigrationStateLoadTask;
            Hub.ShowMigrationCommand.Execute(null);
        }
        public async Task PrepareAsync()
        {
            await OpenUpgradeAsync();
            Assert.True(Hub.PrepareUpgradeDownloadCommand.CanExecute(null));
            _upgrade = Hub.PrepareUpgradeDownloadCommand.ExecuteAsync(null);
            await Services.VersionSelectionEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Packs.ReleasesLoadTask;
            Assert.Single(Packs.Releases);
            Assert.Null(Packs.SelectedRelease);
        }
        public Task ConfirmVersionAsync() => CompleteSelectionAsync(Assert.Single(Packs.Releases));
        public Task CompleteSelectionAsync(GtnhPackRelease? release)
        {
            Services.VersionChoice.TrySetResult(release);
            return _upgrade ?? throw new InvalidOperationException("尚未打开版本选择。");
        }
        public async ValueTask DisposeAsync() { await Hub.StopAsync(); Hub.Dispose(); }
    }

    private sealed class NoticeRecorder : IOperationNotifications
    {
        public List<OperationNotice> Notices { get; } = [];
        public void Report(OperationNotice notice) => Notices.Add(notice);
    }

    private sealed class FakeServices : IMinecraftInstanceLocator, IMinecraftInstanceManager,
        IMinecraftInstanceImporter, IInstanceDialogs, IMinecraftInstanceChecker, IMinecraftInstanceDownloader,
        IMinecraftInstanceMigrationService, IGtnhAddonService, IGtnhPackService, IInstanceMigrationDialogs
    {
        public MinecraftInstance Old { get; } = new("Old GTNH", @"C:\direct-upgrade-test\old", @"C:\direct-upgrade-test\old\.minecraft", ["1.7.10"]);
        public MinecraftInstance Decoy { get; } = new("Other vanilla", @"C:\direct-upgrade-test\other", @"C:\direct-upgrade-test\other\.minecraft", ["1.7.10"]);
        public InstanceImportResult Imported { get; } = new(@"C:\direct-upgrade-test\precise-import", @"C:\direct-upgrade-test\precise-import\.minecraft",
            new InstanceImportManifest { Name = "Downloaded GTNH", SourceFormat = "Prism", MinecraftVersion = "1.7.10" });
        public PreparedInstanceArchive Archive { get; } = new("cache/gtnh-prism.zip", "Downloaded GTNH", "Prism");
        public string? ImportedVersion { get; set; } = "2.9.0";
        public string? OldVersion { get; set; } = "2.8.4";
        public string? Problem { get; set; }
        public bool AcceptImport { get; set; } = true;
        public bool? AcceptMigration { get; set; } = false;
        public string MigrationResultStatus { get; set; } = "Completed";
        public Action? AfterDownload { get; set; }
        public Func<CancellationToken, Task<PreparedInstanceArchive>>? Prepare { get; set; }
        public Func<int, CancellationToken, Task>? BeforeImportedInspection { get; set; }
        public Func<Task>? BeforeGroups { get; set; }
        public TaskCompletionSource VersionSelectionEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<GtnhPackRelease?> VersionChoice { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ArchivePreparationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken VersionSelectionToken { get; private set; }
        private int _importedInspections;
        public int VersionSelections { get; private set; }
        public int ArchivePreparations { get; private set; }
        public int Imports { get; private set; }
        public int Migrations { get; private set; }
        public int GroupSelections { get; private set; }
        public int FinalConfirmations { get; private set; }
        public InstanceMigrationRequest? LastRequest { get; private set; }
        public List<string> DownloadPaths { get; } = [];
        public IReadOnlyList<GtnhAddon> Addons => [];
        public Task<IReadOnlyList<MinecraftInstance>> FindAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MinecraftInstance>>(Imports == 0 ? [Old] : [Old, Decoy,
                new(Imported.Manifest.Name, Imported.InstancePath, Imported.GameDirectory, ["1.7.10"])]);
        public Task<InstanceImportPreview> InspectAsync(string archivePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InstanceImportPreview("Downloaded GTNH", "Prism", "1.7.10", [], 1, 1, 0, []));
        public Task<InstanceImportResult> ImportAsync(string archivePath, string instanceName, IProgress<InstanceImportProgress>? progress = null,
            CancellationToken cancellationToken = default) { Imports++; return Task.FromResult(Imported); }
        public string? ConfirmImport(InstanceImportPreview preview) => AcceptImport ? preview.SuggestedName : null;
        public Task<InstanceDownloadReport> DownloadMissingAsync(string instancePath, IProgress<InstanceDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            DownloadPaths.Add(instancePath);
            AfterDownload?.Invoke();
            if (Problem == "exception") throw new IOException("下载中断");
            var report = new InstanceDownloadReport([], new InstanceCheckReport
            {
                InstancePath = instancePath, Mode = InstanceCheckMode.Full, HasManifest = true,
                LaunchProfilePath = "launch.json",
                ContentIssues = Problem == "content" ? ["缺少必要内容"] : [],
                RuntimeIssues = Problem == "runtime" ? ["缺少运行依赖"] : []
            }) { IsCanceled = Problem == "cancelled", CheckError = Problem == "check-error" ? "复查失败" : null };
            return Task.FromResult(report);
        }
        async Task<GtnhInstanceInfo?> IMinecraftInstanceMigrationService.InspectAsync(string instancePath, CancellationToken token)
        {
            if (instancePath == Imported.InstancePath && BeforeImportedInspection is { } before)
                await before(++_importedInspections, token);
            var info = instancePath == Old.InstancePath && OldVersion is not null ? new GtnhInstanceInfo(instancePath, Old.Name, Old.GameDirectory, OldVersion) :
                instancePath == Imported.InstancePath && ImportedVersion is not null
                    ? new(instancePath, Imported.Manifest.Name, Imported.GameDirectory, ImportedVersion) : null;
            return info;
        }
        public Task<MigrationRecoveryInfo?> GetRecoveryAsync(string instancePath, CancellationToken token = default) => Task.FromResult<MigrationRecoveryInfo?>(null);
        public async Task<IReadOnlyList<MigrationGroup>> GetGroupsAsync(string sourceInstance, GtnhMigrationMode mode, CancellationToken token = default)
        {
            if (BeforeGroups is { } before) await before();
            return [new("servers", "服务器列表", true), new("mod:mods/extra.jar", "额外模组", false)];
        }
        public Task<InstanceMigrationPlan> PreviewAsync(InstanceMigrationRequest request, CancellationToken token = default)
        {
            LastRequest = request;
            return Task.FromResult(new InstanceMigrationPlan(request,
                new(Old.InstancePath, Old.Name, Old.GameDirectory, "2.8.4"),
                new(Imported.InstancePath, Imported.Manifest.Name, Imported.GameDirectory, ImportedVersion!), [], [], "fixed"));
        }
        public Task<InstanceMigrationResult> MigrateAsync(InstanceMigrationPlan plan, bool overwriteConflicts,
            IProgress<InstanceMigrationProgress>? progress = null, CancellationToken token = default)
        { Migrations++; return Task.FromResult(new InstanceMigrationResult("migration", MigrationResultStatus, 1, 0, "journal.json", ["已处理"])); }
        public MigrationSelection? ChooseGroups(GtnhInstanceInfo source, GtnhInstanceInfo target, IReadOnlyList<MigrationGroup> groups)
        { GroupSelections++; return new(["servers", "mod:mods/extra.jar"], true, true) { ExtraModsRiskAccepted = true }; }
        public bool? ConfirmMigration(InstanceMigrationPlan plan) { FinalConfirmations++; return AcceptMigration; }
        public Task<IReadOnlyList<GtnhPackRelease>> GetReleasesAsync(bool includePrereleases = false, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GtnhPackRelease>>([Pack("2.9.0"), Pack("2.8.4"), Pack("2.7.4")]);
        public static GtnhPackRelease Pack(string version) => new()
        { Id = "gtnh-" + version, Version = version, JavaVersion = "17-26", FileName = "gtnh-" + version + ".zip", Size = 12 };
        public Task<PreparedInstanceArchive> PrepareAsync(string releaseId, IProgress<FileDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArchivePreparations++;
            ArchivePreparationEntered.TrySetResult();
            return Prepare?.Invoke(cancellationToken) ?? Task.FromResult(Archive);
        }
        public async Task<GtnhPackRelease?> ChooseUpgradeVersionAsync(GtnhPackDownloadViewModel versions, CancellationToken token = default)
        {
            VersionSelections++;
            VersionSelectionToken = token;
            await versions.ReleasesLoadTask;
            VersionSelectionEntered.TrySetResult();
            return await VersionChoice.Task.WaitAsync(token);
        }
        public Task DeleteAsync(string instancePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> RenameAsync(string instancePath, string newName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ExportAsync(string instancePath, string destinationZip, IProgress<InstanceExportProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public bool ConfirmDelete(MinecraftInstance instance) => throw new NotSupportedException();
        public string? ChooseExportPath(MinecraftInstance instance) => throw new NotSupportedException();
        public string? ChooseImportPath() => throw new NotSupportedException();
        public Task<InstanceCheckReport> CheckAsync(string instancePath, InstanceCheckMode mode = InstanceCheckMode.Full,
            IProgress<InstanceCheckProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InstanceMigrationResult> RestoreAsync(string instancePath, string migrationId,
            IProgress<InstanceMigrationProgress>? progress = null, CancellationToken token = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<GtnhAddonRelease>> GetReleasesAsync(string instancePath, string addonId, CancellationToken token = default) => throw new NotSupportedException();
        public Task<GtnhAddonInstallResult> InstallAsync(string instancePath, GtnhAddonRelease release, bool compatibilityConfirmed,
            IProgress<FileDownloadProgress>? progress = null, CancellationToken token = default) => throw new NotSupportedException();
        public MigrationTargetChoice? ChooseUpgradeTarget(GtnhInstanceInfo source, IReadOnlyList<GtnhInstanceInfo> targets) => throw new NotSupportedException();
        public MigrationSourceChoice? ChooseSource(GtnhInstanceInfo target, IReadOnlyList<GtnhInstanceInfo> sources) => throw new NotSupportedException();
        public bool ConfirmRestore(string instanceName, MigrationRecoveryInfo recovery) => throw new NotSupportedException();
    }
}
