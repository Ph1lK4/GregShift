using EML.Core.Downloads;
using EML.Infrastructure.Downloads;

namespace EML.Tests;

public sealed class DownloadCacheMaintenanceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "EML-cache-maintenance-" + Guid.NewGuid().ToString("N"));
    private string Cache => Path.Combine(_root, "cache", "downloads");
    private static string Key(char value) => "sha256-" + new string(value, 64);

    [Fact]
    public async Task ManualCleanupPreservesActiveObjectsResumeAndOriginalArchives()
    {
        var old = Write(Key('a') + ".blob", 7);
        var invalid = Write(Key('a') + ".blob.invalid-" + new string('b', 32), 5);
        var active = Write(Key('b') + ".blob", 11);
        var resume = Write(Key('c') + ".part", 3);
        var unrelated = Write("readme.txt", 2);
        var archive = Path.Combine(_root, "cache", "instance-archives", "pack.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        File.WriteAllText(archive, "original package");
        using var activeLock = new FileStream(Path.Combine(Cache, Key('b') + ".lock"), FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None);
        using var downloader = new HttpFileDownloader(new FileDownloadOptions { CacheDirectory = Cache });

        Assert.Equal(26, await downloader.GetCacheSizeAsync());
        var result = await downloader.ClearCompletedCacheAsync();

        Assert.Equal(new DownloadCacheCleanupResult(12, 14), result);
        Assert.False(File.Exists(old));
        Assert.False(File.Exists(invalid));
        foreach (var preserved in new[] { active, resume, unrelated, archive }) Assert.True(File.Exists(preserved));
    }

    [Fact]
    public void AutomaticCleanupTrimsOldestObjectsAndOnlyExpiredResumePairs()
    {
        var oldest = Write(Key('a') + ".blob", 7, daysOld: 2);
        var newer = Write(Key('b') + ".blob", 7);
        var expiredPart = Write(Key('c') + ".part", 2, daysOld: 9);
        var expiredMetadata = Write(Key('c') + ".resume.json", 2, daysOld: 9);
        var activePart = Write(Key('d') + ".part", 3);
        var olderMetadata = Write(Key('d') + ".resume.json", 1, daysOld: 9);
        using var downloader = new HttpFileDownloader(new FileDownloadOptions { CacheDirectory = Cache });

        var result = downloader.CleanCache(11, manual: false, default);

        Assert.Equal(new DownloadCacheCleanupResult(11, 11), result);
        foreach (var removed in new[] { oldest, expiredPart, expiredMetadata }) Assert.False(File.Exists(removed));
        foreach (var preserved in new[] { newer, activePart, olderMetadata }) Assert.True(File.Exists(preserved));
    }

    private string Write(string name, int bytes, int daysOld = 0)
    {
        Directory.CreateDirectory(Cache);
        var path = Path.Combine(Cache, name);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-daysOld));
        return path;
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
