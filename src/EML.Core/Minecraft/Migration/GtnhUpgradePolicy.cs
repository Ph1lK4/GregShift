namespace EML.Core.Minecraft.Migration;

/// <summary>升级只选择基础版本更高的独立 GTNH；不从实例名称猜测版本或兼容性。</summary>
public static class GtnhUpgradePolicy
{
    public static bool IsNewerTarget(GtnhInstanceInfo source, GtnhInstanceInfo target) =>
        !string.Equals(source.InstancePath, target.InstancePath, StringComparison.OrdinalIgnoreCase) &&
        TryBaseVersion(source.Version, out var oldVersion) && TryBaseVersion(target.Version, out var newVersion) &&
        newVersion > oldVersion;

    private static bool TryBaseVersion(string value, out Version? version) =>
        Version.TryParse(string.Join('.', value.Split('.', '-').Take(3)), out version);
}
