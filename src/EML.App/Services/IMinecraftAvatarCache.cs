namespace EML.App.Services;

/// <summary>只按公开 UUID 读取头像 PNG，不接收登录令牌，也不参与启动身份选择。</summary>
public interface IMinecraftAvatarCache
{
    Task<byte[]?> GetAsync(string? accountId, CancellationToken token = default);
}
