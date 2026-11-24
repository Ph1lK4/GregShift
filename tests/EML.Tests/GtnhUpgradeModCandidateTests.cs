using System.Security.Cryptography;
using EML.Core.Minecraft;
using EML.Core.Minecraft.Migration;
using EML.Infrastructure.Minecraft.Importing;
using EML.Infrastructure.Minecraft.Mods;

namespace EML.Tests;

public sealed partial class GtnhMigrationTests
{
    /// <summary>目录识别只说明项目身份；成品 Prism 原包中的同一项目仍不能自动视为玩家添加。</summary>
    [Fact]
    public async Task ImportedKnownAddonStillNeedsPlayerConfirmation()
    {
        var old = await CreateAsync("old");
        Write(old.Game, "mods/bundled.jar", "bundled");
        var official = await AddOfficialBaselineAsync(old, "mods/bundled.jar", "bundled");
        var manifestPath = Path.Combine(old.Path, InstanceImportManifest.FileName);
        var manifest = InstanceManifestStore.Read(manifestPath);
        await InstanceManifestStore.WriteAsync(manifestPath,
            manifest with { Files = [official with { ProjectId = 280294 }] }, default);

        var group = Assert.Single(await Service.GetGroupsAsync(old.Path, GtnhMigrationMode.FullClient));
        Assert.Equal(GtnhExtraModCandidateKind.NeedsConfirmation, group.ExtraModKind);
        Assert.Equal("FPS Reducer · 后台帧率调节", group.AddonName);
        Assert.Contains("不能区分", group.Description);
        Assert.False(group.Recommended);
    }

    /// <summary>看似已知可添加模组的文件名不能证明项目身份，也不能证明是玩家自行添加。</summary>
    [Fact]
    public async Task UntrackedAddonFilenameIsNotIdentityOrAdditionEvidence()
    {
        var old = await CreateAsync("old");
        Write(old.Game, "mods/worldedit-gtnh.jar", "unknown");
        var group = Assert.Single(await Service.GetGroupsAsync(old.Path, GtnhMigrationMode.FullClient));
        Assert.Equal(GtnhExtraModCandidateKind.NeedsConfirmation, group.ExtraModKind);
        Assert.Null(group.AddonName);
        Assert.Contains("不能证明", group.Description);
        Assert.False(group.Recommended);
    }

    /// <summary>只有玩家经模组管理明确添加或接管的非原包文件，才被归入确认的额外模组。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitLocalModManagementConfirmsExtraAddition(bool adopt)
    {
        var old = await CreateAsync("old");
        await DeclareModLoaderAsync(old);
        var manager = new LocalModManager(_root);
        if (adopt)
        {
            WriteMod(old.Game, "mods/local.jar", "local", "one");
            await manager.AdoptAsync(old.Path, ".minecraft/mods/local.jar");
        }
        else
        {
            WriteMod(_root, "incoming/local.jar", "local", "one");
            await manager.AddAsync(old.Path, Path.Combine(_root, "incoming/local.jar"));
        }
        var group = Assert.Single(await Service.GetGroupsAsync(old.Path, GtnhMigrationMode.FullClient));
        Assert.Equal(GtnhExtraModCandidateKind.ConfirmedAddition, group.ExtraModKind);
        Assert.Null(group.AddonName);
        Assert.False(group.Recommended);
    }

    /// <summary>保存的 GitHub 固定身份可匹配专区项目，即使安装文件名没有任何项目名称。</summary>
    [Fact]
    public async Task RegisteredGithubOriginIdentifiesAddonWithoutFilenameGuessing()
    {
        var old = await CreateAsync("old");
        await DeclareModLoaderAsync(old);
        WriteMod(_root, "incoming/unnamed.jar", "worldedit", "one");
        var source = Path.Combine(_root, "incoming/unnamed.jar");
        var bytes = File.ReadAllBytes(source);
        var github = new GithubReleaseFileOrigin
        {
            Repository = "GTNewHorizons/worldedit-gtnh", Tag = "test-one", ReleaseId = 1, AssetId = 2, FileName = "unnamed.jar"
        };
        var baseline = new InstanceFileRequirement
        {
            Path = ".minecraft/mods/unnamed.jar", Source = InstanceFileSource.DirectDownload,
            Origin = InstanceFileOrigin.GithubReleaseDownload, Role = InstanceFileRole.Content,
            Size = bytes.LongLength, Hashes = new Dictionary<string, string> { ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes)) },
            GithubReleaseOrigin = github, Downloads = [InstanceFileIntegrity.GithubDownloadUrl(github)]
        };
        await new LocalModManager(_root).RegisterDownloadedAsync(old.Path, source, baseline);

        var group = Assert.Single(await Service.GetGroupsAsync(old.Path, GtnhMigrationMode.FullClient));
        Assert.Equal(GtnhExtraModCandidateKind.ConfirmedAddition, group.ExtraModKind);
        Assert.Equal("WorldEdit · 建筑编辑", group.AddonName);
        Assert.False(group.Recommended);
    }

    /// <summary>已移除记录旁重新出现的外部副本不能伪装为已确认的额外模组。</summary>
    [Fact]
    public async Task RemovedModExternalCopyNeedsRepairNotAutomaticTrust()
    {
        var old = await CreateAsync("old");
        await DeclareModLoaderAsync(old);
        WriteMod(_root, "incoming/local.jar", "local", "one");
        var manager = new LocalModManager(_root);
        var source = Path.Combine(_root, "incoming/local.jar");
        await manager.AddAsync(old.Path, source);
        await manager.RemoveAsync(old.Path, ".minecraft/mods/local.jar");
        File.Copy(source, Path.Combine(old.Game, "mods/local.jar"));

        var group = Assert.Single(await Service.GetGroupsAsync(old.Path, GtnhMigrationMode.FullClient));
        Assert.Equal(GtnhExtraModCandidateKind.NeedsConfirmation, group.ExtraModKind);
        Assert.Contains("外部副本", group.Name);
        Assert.Contains("不能通过升级", group.Description);
        Assert.False(group.Recommended);
    }

    /// <summary>管理写操作使用真实支持的旧 Forge 声明，不放宽生产代码的加载器保护。</summary>
    private static async Task DeclareModLoaderAsync(Instance instance)
    {
        var path = Path.Combine(instance.Path, InstanceImportManifest.FileName);
        var manifest = InstanceManifestStore.Read(path);
        await InstanceManifestStore.WriteAsync(path,
            manifest with { Components = [new("net.minecraftforge", "10.13.4.1614")] }, default);
    }
}
