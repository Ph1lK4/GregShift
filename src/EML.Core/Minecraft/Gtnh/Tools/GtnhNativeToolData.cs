using System.Globalization;
using System.Text.Json;

namespace EML.Core.Minecraft.Gtnh.Tools;

/// <summary>随程序交付的声明式快照；原生工具无需联网、登录或读取实例。</summary>
public static class GtnhNativeToolData
{
    public const string SnapshotLabel = "GTNH 2.9.0-RC-1 · 数据快照 2026-11-22";
    public const string OreSourceLabel = SnapshotLabel + " · GTNH 中文维基 CC0 数据";
    public const string ResearchSourceLabel = "GTNH 要素关系与中文名 · 权重：中文维基 CC0 快照 2026-11-22";

    public static GtnhOreCatalog LoadOres()
    {
        var ores = TabularJson(ReadTable("ore-routing.tsv"));
        return GtnhOreCatalog.Parse(ores, ores, TabularJson(ReadTable("ore-geology.tsv")));
    }

    public static string LoadResearchJson() => JsonSerializer.Serialize(ReadTable("research-aspects.tsv").Skip(1).Select(row => new
    {
        id = row[0].ToLowerInvariant(), name = row[1], weight = double.Parse(row[2], CultureInfo.InvariantCulture),
        components = row.Skip(3).Where(part => part.Length > 0).Select(part => part.ToLowerInvariant()).ToArray()
    }));

    private static string[][] ReadTable(string name)
    {
        using var stream = typeof(GtnhNativeToolData).Assembly.GetManifestResourceStream("EML.Core.Minecraft.Gtnh.Tools.Data." + name)
            ?? throw new InvalidDataException("内置工具数据缺失：" + name);
        using var reader = new StreamReader(stream);
        var rows = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r').Split('\t')).ToArray();
        if (rows.Length < 2 || rows.Any(row => row.Length != rows[0].Length)) throw new InvalidDataException("内置工具数据行列不一致：" + name);
        return rows;
    }

    private static string TabularJson(string[][] rows) => JsonSerializer.Serialize(new
    { schema = new { fields = rows[0].Select(name => new { name }) }, data = rows.Skip(1) });
}
