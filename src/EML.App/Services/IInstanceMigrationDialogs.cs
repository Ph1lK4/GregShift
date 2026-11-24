using EML.App.ViewModels;
using EML.Core.Minecraft.Gtnh;
using EML.Core.Minecraft.Migration;

namespace EML.App.Services;

public sealed record MigrationSourceChoice(GtnhInstanceInfo Source, GtnhMigrationMode Mode);
public enum UpgradeTargetAction { SelectInstance, DownloadNewInstance, ImportNewInstance }
public sealed record MigrationTargetChoice(GtnhInstanceInfo? Target, GtnhMigrationMode Mode,
    UpgradeTargetAction Action = UpgradeTargetAction.SelectInstance);
public sealed record MigrationSelection(IReadOnlyList<string> Groups, bool TargetTested, bool GamesClosed)
{
    public bool ExtraModsRiskAccepted { get; init; }
}

/// <summary>迁移确认与磁盘操作分离，取消任何一步都不修改实例。</summary>
public interface IInstanceMigrationDialogs
{
    /// <summary>在独立弹窗选择远端新版；仅返回选择，不授权下载或迁移，关闭即取消。</summary>
    Task<GtnhPackRelease?> ChooseUpgradeVersionAsync(GtnhPackDownloadViewModel versions, CancellationToken token = default) =>
        Task.FromResult<GtnhPackRelease?>(null);
    /// <summary>从固定旧实例选择新版，或引导准备新版；选择准备操作不会授权复制。</summary>
    MigrationTargetChoice? ChooseUpgradeTarget(GtnhInstanceInfo source, IReadOnlyList<GtnhInstanceInfo> targets);
    /// <summary>明确旧实例和迁移模式，当前选中实例始终是接收方。</summary>
    MigrationSourceChoice? ChooseSource(GtnhInstanceInfo target, IReadOnlyList<GtnhInstanceInfo> sources);
    /// <summary>选择实际存在的数据组，并确认新版实测成功、双方游戏已退出。</summary>
    MigrationSelection? ChooseGroups(GtnhInstanceInfo source, GtnhInstanceInfo target, IReadOnlyList<MigrationGroup> groups);
    /// <summary>完整预览后返回冲突覆盖选择；null 表示取消，false 表示保留新版。</summary>
    bool? ConfirmMigration(InstanceMigrationPlan plan);
    /// <summary>恢复前确认游戏已关闭，并说明后续改动不会被覆盖。</summary>
    bool ConfirmRestore(string instanceName, MigrationRecoveryInfo recovery);
}
