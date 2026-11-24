using EML.App.Services;
using EML.App.ViewModels;
using EML.Core.Minecraft.Migration;
using EML.Infrastructure.Minecraft.Migration;

namespace EML.Tests;

public sealed partial class GtnhMigrationTests
{
    /// <summary>从旧实例发起时固定方向，只列版本更高且可识别的独立 GTNH。</summary>
    [Fact]
    public async Task UpgradeStartsFromOldInstanceAndFiltersInvalidDestinations()
    {
        var old = await CreateAsync("old"); var target = await CreateAsync("new");
        var lower = await CreateAsync("lower"); var same = await CreateAsync("same"); var invalid = await CreateAsync("invalid");
        Write(lower.Game, "config/txloader/load/mainmenu/version.txt", "GTNH 2.7.4");
        Write(same.Game, "config/txloader/load/mainmenu/version.txt", "GTNH 2.8.4");
        File.Delete(Path.Combine(invalid.Game, "config/txloader/load/mainmenu/version.txt"));
        var dialogs = new MigrationDialogs { UpgradeChoice = (_, _) => null };
        var vm = CreateUpgradeViewModel(old, target, dialogs);
        foreach (var candidate in new[] { lower, same, invalid })
            vm.Instances.Add(new(Path.GetFileName(candidate.Path), candidate.Path, candidate.Game, ["1.7.10"]));
        await vm.MigrationStateLoadTask;

        await vm.UpgradeGtnhCommand.ExecuteAsync(null);

        Assert.Equal(old.Path, dialogs.UpgradeSource!.InstancePath);
        Assert.Equal("2.8.4", dialogs.UpgradeSource.Version);
        Assert.Equal(target.Path, Assert.Single(dialogs.UpgradeTargets).InstancePath);
        Assert.Equal(old.Path, vm.SelectedInstance!.InstancePath);
        Assert.False(vm.IsBusy);
        Assert.False(Directory.Exists(Path.Combine(target.Path, ".eml-runtime")));
    }

    /// <summary>升级复制数据但不改写旧实例，完成后选择新版并保留恢复入口。</summary>
    [Fact]
    public async Task UpgradePreservesOldInstanceAndSelectsNewInstanceForTestingAndRecovery()
    {
        var old = await CreateAsync("old"); var target = await CreateAsync("new");
        Write(old.Game, "servers.dat", "saved servers");
        var dialogs = new MigrationDialogs(); var notices = new MigrationNoticeRecorder();
        var vm = CreateUpgradeViewModel(old, target, dialogs, notices);
        await vm.MigrationStateLoadTask;

        await vm.UpgradeGtnhCommand.ExecuteAsync(null);

        Assert.Equal("saved servers", Read(old.Game, "servers.dat"));
        Assert.Equal("saved servers", Read(target.Game, "servers.dat"));
        Assert.Equal(old.Path, dialogs.Preview!.Request.SourceInstance);
        Assert.Equal(target.Path, dialogs.Preview.Request.TargetInstance);
        Assert.Equal(target.Path, vm.SelectedInstance!.InstancePath);
        Assert.Contains("待试运行", vm.MigrationStatus);
        Assert.Contains("不代表存档或额外模组兼容", vm.MigrationStatus);
        Assert.True(vm.RestoreMigrationCommand.CanExecute(null));
        Assert.False(vm.IsBusy);
        var notice = Assert.Single(notices.Notices);
        Assert.Equal(OperationNoticeKind.Success, notice.Kind);
        Assert.Contains("待试运行", notice.Title);
        Assert.Contains(old.Path, notice.Paths); Assert.Contains(target.Path, notice.Paths);

        await vm.RestoreMigrationCommand.ExecuteAsync(null);

        Assert.False(File.Exists(Path.Combine(target.Game, "servers.dat")));
        Assert.Equal("saved servers", Read(old.Game, "servers.dat"));
        Assert.False(vm.RestoreMigrationCommand.CanExecute(null));
    }

    /// <summary>新版实测和关闭游戏仍是独立门槛，升级入口不能绕过原迁移服务。</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task UpgradeRequiresTestedTargetAndClosedGames(bool tested, bool closed)
    {
        var old = await CreateAsync("old"); var target = await CreateAsync("new"); Write(old.Game, "servers.dat", "saved");
        var dialogs = new MigrationDialogs { Selection = new(["servers"], tested, closed) };
        var vm = CreateUpgradeViewModel(old, target, dialogs);
        await vm.MigrationStateLoadTask;

        await vm.UpgradeGtnhCommand.ExecuteAsync(null);

        Assert.False(File.Exists(Path.Combine(target.Game, "servers.dat")));
        Assert.Equal(old.Path, vm.SelectedInstance!.InstancePath);
        Assert.Contains("请确认新版已成功运行", vm.MigrationStatus);
        Assert.False(vm.IsBusy);
    }

    /// <summary>模组仍默认独立授权，接受风险后才复制选中旧版本，不带配置。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpgradePassesExtraModRiskConfirmationWithoutCopyingConfiguration(bool accepted)
    {
        var old = await CreateAsync("old"); var target = await CreateAsync("new");
        Write(old.Game, "mods/custom.jar", "custom"); Write(old.Game, "config/custom.cfg", "old config");
        var dialogs = new MigrationDialogs { Selection = new(["mod:mods/custom.jar"], true, true) { ExtraModsRiskAccepted = accepted } };
        var vm = CreateUpgradeViewModel(old, target, dialogs);
        await vm.MigrationStateLoadTask;

        await vm.UpgradeGtnhCommand.ExecuteAsync(null);

        Assert.Equal(accepted, File.Exists(Path.Combine(target.Game, "mods/custom.jar")));
        Assert.False(File.Exists(Path.Combine(target.Game, "config/custom.cfg")));
        Assert.Equal("custom", Read(old.Game, "mods/custom.jar"));
        Assert.False(vm.IsBusy);
        Assert.Contains(accepted ? "待试运行" : "请先确认", vm.MigrationStatus);
        if (accepted) Assert.True(dialogs.Preview!.Request.ExtraModsRiskAccepted);
    }

    /// <summary>准备新版仅发送导航请求；无新版时仍能选择下载或导入，且先释放互斥锁。</summary>
    [Theory]
    [InlineData(UpgradeTargetAction.DownloadNewInstance)]
    [InlineData(UpgradeTargetAction.ImportNewInstance)]
    public async Task UpgradePreparationDoesNotMigrateOrKeepInstanceBusy(UpgradeTargetAction action)
    {
        var old = await CreateAsync("old"); var target = await CreateAsync("new"); Write(old.Game, "servers.dat", "saved");
        Write(target.Game, "config/txloader/load/mainmenu/version.txt", "GTNH 2.8.4");
        var dialogs = new MigrationDialogs { UpgradeChoice = (_, targets) =>
        {
            Assert.Empty(targets);
            return new(null, GtnhMigrationMode.FullClient, action);
        } };
        var notices = new MigrationNoticeRecorder();
        var vm = CreateUpgradeViewModel(old, target, dialogs, notices);
        var requested = new List<UpgradeTargetAction>();
        vm.UpgradePreparationRequested += value =>
        {
            Assert.False(vm.IsBusy);
            Assert.Equal(old.Path, vm.SelectedInstance!.InstancePath);
            requested.Add(value);
        };
        await vm.MigrationStateLoadTask;

        await vm.UpgradeGtnhCommand.ExecuteAsync(null);

        Assert.Equal(new[] { action }, requested);
        Assert.Empty(notices.Notices);
        Assert.Null(dialogs.Preview);
        Assert.False(Directory.Exists(Path.Combine(target.Path, ".eml-runtime")));
        Assert.Equal("saved", Read(old.Game, "servers.dat"));
        Assert.Contains("旧实例未修改", vm.MigrationStatus);
    }

    /// <summary>在最终文件预览取消，两个实例和当前选择均保持不变。</summary>
    [Fact]
    public async Task UpgradeFinalPreviewCancellationDoesNotWriteOrSwitchInstances()
    {
        var old = await CreateAsync("old"); var target = await CreateAsync("new"); Write(old.Game, "servers.dat", "saved");
        var notices = new MigrationNoticeRecorder();
        var vm = CreateUpgradeViewModel(old, target, new MigrationDialogs { Confirmation = null }, notices);
        await vm.MigrationStateLoadTask;

        await vm.UpgradeGtnhCommand.ExecuteAsync(null);

        Assert.Equal(old.Path, vm.SelectedInstance!.InstancePath);
        Assert.False(File.Exists(Path.Combine(target.Game, "servers.dat")));
        Assert.False(Directory.Exists(Path.Combine(target.Path, ".eml-runtime")));
        Assert.Equal("saved", Read(old.Game, "servers.dat"));
        Assert.Equal(OperationNoticeKind.Cancelled, Assert.Single(notices.Notices).Kind);
        Assert.False(vm.IsBusy);
    }

    /// <summary>即使对话框返回未提供的实例，也不能绕过目标列表去原地覆盖或降级。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpgradeRejectsDestinationOutsideApprovedCandidates(bool sameInstance)
    {
        var old = await CreateAsync("old"); var target = await CreateAsync("new"); Write(old.Game, "servers.dat", "saved");
        var dialogs = new MigrationDialogs { UpgradeChoice = (source, _) => new(sameInstance ? source :
            new(target.Path, "forged target", target.Game, "2.7.4"), GtnhMigrationMode.FullClient) };
        var notices = new MigrationNoticeRecorder(); var vm = CreateUpgradeViewModel(old, target, dialogs, notices);
        await vm.MigrationStateLoadTask;

        await vm.UpgradeGtnhCommand.ExecuteAsync(null);

        Assert.Equal(old.Path, vm.SelectedInstance!.InstancePath);
        Assert.False(File.Exists(Path.Combine(target.Game, "servers.dat")));
        Assert.Contains("不支持原地覆盖或降级升级", vm.MigrationStatus);
        Assert.Equal(OperationNoticeKind.Failure, Assert.Single(notices.Notices).Kind);
        Assert.False(vm.IsBusy);
    }

    /// <summary>执行前源数据变化使预览失效，失败后仍保留旧实例选择和可再次操作状态。</summary>
    [Fact]
    public async Task UpgradeStalePreviewKeepsOriginalSelectionAndReleasesBusyState()
    {
        var old = await CreateAsync("old"); var target = await CreateAsync("new"); Write(old.Game, "servers.dat", "saved");
        var notices = new MigrationNoticeRecorder();
        var dialogs = new MigrationDialogs { BeforeConfirmation = () => Write(old.Game, "servers.dat", "external edit") };
        var vm = CreateUpgradeViewModel(old, target, dialogs, notices);
        await vm.MigrationStateLoadTask;

        await vm.UpgradeGtnhCommand.ExecuteAsync(null);

        Assert.Equal(old.Path, vm.SelectedInstance!.InstancePath);
        Assert.False(File.Exists(Path.Combine(target.Game, "servers.dat")));
        Assert.Equal("external edit", Read(old.Game, "servers.dat"));
        Assert.Contains("预览后发生变化", vm.MigrationStatus);
        Assert.Equal(OperationNoticeKind.Failure, Assert.Single(notices.Notices).Kind);
        Assert.False(vm.IsBusy);
        Assert.True(vm.UpgradeGtnhCommand.CanExecute(null));
    }

    /// <summary>未结束的迁移事务必须先恢复，不会出现在升级候选中。</summary>
    [Fact]
    public async Task UpgradeExcludesDestinationWithUnfinishedMigration()
    {
        var old = await CreateAsync("old"); var target = await CreateAsync("new"); Write(old.Game, "servers.dat", "saved");
        var migrated = await Service.MigrateAsync(await PlanAsync(old, target, "servers"), false);
        var journal = MigrationJournalStore.Read(target.Path, migrated.Id);
        journal.Status = "Applying";
        MigrationJournalStore.Write(target.Path, journal);
        var dialogs = new MigrationDialogs { UpgradeChoice = (_, _) => null };
        var vm = CreateUpgradeViewModel(old, target, dialogs);
        await vm.MigrationStateLoadTask;

        await vm.UpgradeGtnhCommand.ExecuteAsync(null);

        Assert.Empty(dialogs.UpgradeTargets);
        Assert.Equal(old.Path, vm.SelectedInstance!.InstancePath);
        Assert.Equal("Applying", (await Service.GetRecoveryAsync(target.Path))!.Status);
        Assert.False(vm.IsBusy);
    }

    /// <summary>升级与其他实例操作共用忙碌开关，方法本身也不能绕过禁用状态。</summary>
    [Fact]
    public async Task UpgradeDoesNotRunWhileAnotherInstanceOperationIsBusy()
    {
        var old = await CreateAsync("old"); var target = await CreateAsync("new");
        var dialogs = new MigrationDialogs(); var vm = CreateUpgradeViewModel(old, target, dialogs);
        await vm.MigrationStateLoadTask;
        vm.IsBusy = true;

        Assert.False(vm.UpgradeGtnhCommand.CanExecute(null));
        await vm.UpgradeGtnhCommand.ExecuteAsync(null);

        Assert.Null(dialogs.UpgradeSource);
        Assert.Null(dialogs.Preview);
        vm.IsBusy = false;
        Assert.True(vm.UpgradeGtnhCommand.CanExecute(null));
    }

    private InstancesViewModel CreateUpgradeViewModel(Instance old, Instance target, MigrationDialogs dialogs,
        IOperationNotifications? notices = null)
    {
        Write(old.Game, "config/txloader/load/mainmenu/version.txt", "GTNH 2.8.4");
        var vm = CreateViewModel(old, target, dialogs, notices);
        vm.SelectedInstance = vm.Instances[0];
        return vm;
    }
}

public sealed class GtnhUpgradePolicyTests
{
    /// <summary>版本使用数值比较，不使用名称和字符串排序，也不接受同版或未知版本。</summary>
    [Theory]
    [InlineData("2.8.4", "2.9.0", true)]
    [InlineData("2.9.0", "2.10.0", true)]
    [InlineData("2.9.0", "2.8.4", false)]
    [InlineData("2.9.0", "2.9.0", false)]
    [InlineData("2.9.0-beta-1", "2.9.0", false)]
    [InlineData("invalid", "2.9.0", false)]
    [InlineData("2.8.4", "待恢复", false)]
    public void NewerTargetRequiresHigherRecognizedBaseVersion(string oldVersion, string targetVersion, bool expected)
    {
        var source = new GtnhInstanceInfo(@"C:\instances\old", "GTNH 9.99.99", @"C:\instances\old\.minecraft", oldVersion);
        var target = new GtnhInstanceInfo(@"C:\instances\new", "GTNH 0.0.0", @"C:\instances\new\.minecraft", targetVersion);
        Assert.Equal(expected, GtnhUpgradePolicy.IsNewerTarget(source, target));
        Assert.False(GtnhUpgradePolicy.IsNewerTarget(source, target with { InstancePath = source.InstancePath }));
    }
}
