using EML.Core.Minecraft;
using EML.Core.Minecraft.Gtnh;
using EML.Core.Minecraft.Migration;
using EML.Core.Minecraft.Mods;
using EML.Infrastructure.Minecraft.CurseForge;
using EML.Infrastructure.Minecraft.Gtnh;
using EML.Infrastructure.Minecraft.Importing;
using EML.Infrastructure.Minecraft.Mods;

namespace EML.Infrastructure.Minecraft.Migration;

/// <summary>只读枚举可手动选择的额外模组，并保护新版已有模组；不猜测或下载模组版本。</summary>
internal static class GtnhExtraModCatalog
{
    internal sealed record Mod(string Path, long Size, string Sha256, IReadOnlyList<string> Ids);

    /// <summary>保留已声明但暂缺的模组位置，包括平台解析和此前迁入记录，不能将其当成空位。</summary>
    internal static IReadOnlyList<string> DeclaredPaths(string root, InstanceImportManifest manifest)
    {
        var prefix = manifest.GameDirectory == "." ? "" : manifest.GameDirectory + "/";
        var resolution = manifest.SourceFormat.Equals("CurseForge", StringComparison.OrdinalIgnoreCase)
            ? CurseForgeFileStateStore.Read(root, manifest) : null;
        var requirements = resolution is null ? manifest.Files : CurseForgeFileStateStore.EffectiveRequirements(manifest, resolution);
        return requirements.Select(file => file.Path).Concat(MigrationDataStore.Read(root, manifest).Files.Select(file => file.Path))
            .Concat(ModManagementStore.Read(root, manifest).Files.Select(record => record.Baseline.Path))
            .Where(path => path is not null && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(path => path![prefix.Length..]).Where(GtnhExtraModPolicy.Allows)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>只把明确登记或此前迁入的模组标记为额外添加；导入时间和目录收录不授予这一身份。</summary>
    internal static IReadOnlyList<MigrationGroup> Groups(GtnhInstanceInfo source, CancellationToken token)
    {
        var manifest = InstanceManifestStore.Read(Path.Combine(source.InstancePath, InstanceImportManifest.FileName));
        var prefix = manifest.GameDirectory == "." ? "" : manifest.GameDirectory + "/";
        var imported = manifest.Files.Where(file => file.Path is not null)
            .GroupBy(file => LocalModPathPolicy.EnabledPath(file.Path!), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var migrated = MigrationDataStore.Read(source.InstancePath, manifest).Files.Where(file => file.Rule == GtnhExtraModPolicy.Rule)
            .GroupBy(file => LocalModPathPolicy.EnabledPath(file.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var managed = ModManagementStore.Read(source.InstancePath, manifest).Files.ToDictionary(
            record => LocalModPathPolicy.EnabledPath(record.Baseline.Path!), StringComparer.OrdinalIgnoreCase);
        return Paths(source.GameDirectory, token).Select(path =>
        {
            var identity = LocalModPathPolicy.EnabledPath(prefix + path);
            managed.TryGetValue(identity, out var record);
            migrated.TryGetValue(identity, out var prior);
            imported.TryGetValue(identity, out var original);
            var registeredPath = record is null ? prior?.Path : ModManagementStore.CurrentPath(record);
            var confirmed = registeredPath is not null && registeredPath.Equals(prefix + path, StringComparison.OrdinalIgnoreCase);
            var externalCopy = record is not null || prior is not null;
            var label = externalCopy && !confirmed ? "已登记模组的外部副本，需先处理"
                : record is { Added: true } ? "EML 已确认额外添加"
                : prior is not null ? "此前手动迁入"
                : original is not null ? "导入时已存在，需确认是自己添加" : "导入后发现，来源待确认";
            var description = externalCopy && !confirmed
                ? "此文件与已登记模组的当前位置不一致，不能通过升级重新接受内容；请先在模组管理中处理。"
                : confirmed ? "有明确的玩家模组管理或此前迁入记录。仍需核对新版兼容性，默认不迁移。"
                : original is not null
                    ? "Prism 等成品实例可能已包含原包模组；导入记录不能区分原包与玩家添加，请只勾选自己确认添加的模组。"
                    : "仅能确认当前文件未在导入清单中声明，不能证明它由玩家添加；请自行核对旧版原包。";
            return new MigrationGroup(GtnhExtraModPolicy.GroupPrefix + path, path + "（" + label + "）", false)
            {
                ExtraModKind = confirmed ? GtnhExtraModCandidateKind.ConfirmedAddition : GtnhExtraModCandidateKind.NeedsConfirmation,
                Description = description,
                AddonName = KnownAddon(record?.Baseline ?? prior?.OnlineModBaseline ?? original)?.Name
            };
        }).ToArray();
    }

    /// <summary>只用已经保存的固定平台身份关联专区目录；不按文件名猜测项目或玩家来源。</summary>
    private static GtnhAddon? KnownAddon(InstanceFileRequirement? file) => file is null ? null :
        GtnhAddonCatalog.Items.FirstOrDefault(addon => addon.Kind is GtnhAddonKind.Mod or GtnhAddonKind.ShaderLoader &&
            (addon.Provider == GtnhAddonProvider.Github && file.GithubReleaseOrigin is { } github &&
                addon.Repository.Equals(github.Repository, StringComparison.OrdinalIgnoreCase) ||
             addon.Provider == GtnhAddonProvider.CurseForge && addon.CurseForgeProjectId is { } project &&
                project == (file.CurseForgeOrigin?.ProjectId ?? file.ProjectId) ||
             addon.Provider == GtnhAddonProvider.Modrinth && addon.ModrinthProjectId is { } modrinth &&
                modrinth.Equals(file.ModrinthOrigin?.ProjectId, StringComparison.Ordinal)));

    /// <summary>目标模组的内容和标识均参与预览指纹，防止预览后安装同模组另一版本。</summary>
    internal static async Task<IReadOnlyList<Mod>> SnapshotAsync(string game, CancellationToken token)
    {
        var result = new List<Mod>();
        foreach (var path in Paths(game, token)) result.Add(await ReadAsync(game, path, token));
        return result;
    }

    /// <summary>读取选中文件的稳定摘要和有限元数据，既不加载 Java 类也不执行模组代码。</summary>
    internal static async Task<Mod> ReadAsync(string game, string relative, CancellationToken token)
    {
        if (!GtnhExtraModPolicy.Allows(relative)) throw new InvalidDataException("不支持的额外模组路径。");
        var path = MigrationIO.SafePath(game, relative);
        var snapshot = await ModArchiveInspector.ReadAsync(path, relative, token);
        return new(relative, snapshot.Size, snapshot.Sha256, snapshot.Metadata.Ids);
    }

    /// <summary>按文件名（忽略禁用后缀）、相同内容和已知 modid 拦截重复，不用文件名推断版本兼容性。</summary>
    internal static string? Collision(Mod candidate, IEnumerable<Mod> others, IEnumerable<string> declaredPaths)
        => ModArchiveInspector.Collision(candidate.Path, candidate.Sha256, candidate.Ids,
            others.Select(other => (other.Path, (string?)other.Sha256, other.Ids)), declaredPaths);

    /// <summary>只遍历 Forge 当前游戏版本使用的位置，不遍历 mods 内的缓存或其他版本目录。</summary>
    private static IReadOnlyList<string> Paths(string game, CancellationToken token)
    {
        var result = new List<string>();
        foreach (var directory in new[] { "mods", "mods/1.7.10" })
        {
            var full = MigrationIO.SafePath(game, directory);
            if (File.Exists(full)) throw new IOException("模组目录被文件占用：" + directory);
            if (!Directory.Exists(full)) continue;
            foreach (var path in Directory.EnumerateFiles(full))
            {
                token.ThrowIfCancellationRequested();
                var relative = InstanceImportPaths.Relative(Path.GetRelativePath(game, path).Replace('\\', '/'));
                MigrationIO.SafePath(game, relative);
                if (GtnhExtraModPolicy.Allows(relative)) result.Add(relative);
                if (result.Count > 4096) throw new IOException("模组数量过多，请先整理实例。");
            }
        }
        return result.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

}
