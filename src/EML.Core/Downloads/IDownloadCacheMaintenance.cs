namespace EML.Core.Downloads;

/// <summary>仅管理下载器对象缓存，不处理原始整合包、实例、设置或备份。</summary>
public interface IDownloadCacheMaintenance
{
    Task<long> GetCacheSizeAsync(CancellationToken token = default);
    Task<DownloadCacheCleanupResult> ClearCompletedCacheAsync(CancellationToken token = default);
}

public sealed record DownloadCacheCleanupResult(long RemovedBytes, long RemainingBytes);
