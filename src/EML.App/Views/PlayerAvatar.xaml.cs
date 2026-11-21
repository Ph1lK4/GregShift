using System.Windows;
using System.Windows.Controls;
using EML.App.Services;

namespace EML.App.Views;

/// <summary>只展示公开皮肤；账号切换、卸载或离线选择会使迟到的头像请求失效。</summary>
public partial class PlayerAvatar : UserControl
{
    public static readonly DependencyProperty AccountIdProperty = DependencyProperty.Register(nameof(AccountId), typeof(string),
        typeof(PlayerAvatar), new PropertyMetadata(null, OnIdentityChanged));
    public static readonly DependencyProperty IsOnlineProperty = DependencyProperty.Register(nameof(IsOnline), typeof(bool),
        typeof(PlayerAvatar), new PropertyMetadata(true, OnIdentityChanged));
    public static readonly DependencyProperty CacheProperty = DependencyProperty.RegisterAttached("Cache", typeof(IMinecraftAvatarCache),
        typeof(PlayerAvatar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits, OnIdentityChanged));
    private CancellationTokenSource? _request;
    private int _revision;
    internal Task AvatarTask { get; private set; } = Task.CompletedTask;

    /// <summary>默认大小使用元数据，不能覆盖列表模板为头像指定的紧凑尺寸。</summary>
    static PlayerAvatar()
    {
        WidthProperty.OverrideMetadata(typeof(PlayerAvatar), new FrameworkPropertyMetadata(40d));
        HeightProperty.OverrideMetadata(typeof(PlayerAvatar), new FrameworkPropertyMetadata(40d));
    }

    public string? AccountId { get => (string?)GetValue(AccountIdProperty); set => SetValue(AccountIdProperty, value); }
    public bool IsOnline { get => (bool)GetValue(IsOnlineProperty); set => SetValue(IsOnlineProperty, value); }

    /// <summary>服务沿视觉/逻辑树继承，两个启动入口与下拉项目共用同一缓存，不触碰账号令牌。</summary>
    public static void SetCache(DependencyObject target, IMinecraftAvatarCache? value) => target.SetValue(CacheProperty, value);
    public static IMinecraftAvatarCache? GetCache(DependencyObject target) => (IMinecraftAvatarCache?)target.GetValue(CacheProperty);

    /// <summary>首次显示立即使用内置头像，加载失败不弹窗、不更改任何身份状态。</summary>
    public PlayerAvatar()
    {
        InitializeComponent(); HeadImage.Source = MinecraftHeadRenderer.Steve;
        Loaded += (_, _) => Refresh();
        IsVisibleChanged += (_, _) => { if (IsLoaded) Refresh(); };
        Unloaded += (_, _) => { ++_revision; _request?.Cancel(); _request = null; };
    }

    /// <summary>仅已加载控件发起请求；隐藏或未创建的页面不会预取远端身份。</summary>
    private static void OnIdentityChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is PlayerAvatar avatar && avatar.IsLoaded) avatar.Refresh();
    }

    /// <summary>身份变化立即清除旧头像，离线模式无论玩家名如何都使用 Steve。</summary>
    private void Refresh()
    {
        ++_revision; _request?.Cancel(); _request = null;
        HeadImage.Source = MinecraftHeadRenderer.Steve;
        if (!IsVisible || !IsOnline || !Guid.TryParse(AccountId, out var id) || id == Guid.Empty || GetCache(this) is not { } cache)
        { AvatarTask = Task.CompletedTask; return; }
        var cancellation = new CancellationTokenSource(); _request = cancellation;
        AvatarTask = ReadAsync(cache, id.ToString("N"), _revision, cancellation);
    }

    /// <summary>异步结果只应用于仍在显示的同一身份；网络、缓存与图片错误不影响启动。</summary>
    private async Task ReadAsync(IMinecraftAvatarCache cache, string id, int revision, CancellationTokenSource cancellation)
    {
        try
        {
            var bytes = await cache.GetAsync(id, cancellation.Token);
            if (bytes is null || revision != _revision || cancellation.IsCancellationRequested || !IsLoaded) return;
            var head = MinecraftHeadRenderer.ReadHead(bytes);
            if (head is not null) HeadImage.Source = head;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OutOfMemoryException) { /* 展示失败保留 Steve，不输出皮肤响应。 */ }
        finally
        {
            if (ReferenceEquals(_request, cancellation)) _request = null;
            cancellation.Dispose();
        }
    }
}
