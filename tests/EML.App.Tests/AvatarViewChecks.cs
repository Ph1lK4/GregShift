using System.Buffers.Binary;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EML.App.Services;
using EML.App.Views;
using EML.Core.Minecraft.Accounts;
using CommunityToolkit.Mvvm.Input;

namespace EML.App.Tests;

/// <summary>公开皮肤显示的离线回归；不读取凭据、不连接网络、不显示测试窗口。</summary>
internal static class AvatarViewChecks
{
    private const string FirstId = "11111111222233334444555555555555";
    private const string SecondId = "aaaaaaaa222233334444555555555555";

    /// <summary>复用现有 STA/Application，验证图片解码与异步头像的生命周期。</summary>
    internal static void Run(Border host)
    {
        VerifyRenderer();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try
        {
            // 隐藏的原生宿主只连接可视树，使生产控件经历真实 Loaded/Unloaded。
            using var source = new HwndSource(new HwndSourceParameters("EML avatar regression")
            { Width = 720, Height = 520, WindowStyle = unchecked((int)0x80000000) });
            source.RootVisual = host;
            try { VerifyAvatarLifecycle(host); VerifyProductionPreviews(host); }
            finally { host.Child = null; source.RootVisual = null; Flush(host); }
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    /// <summary>标准皮肤裁切、透明帽子、旧格式和非法图像都有明确边界。</summary>
    private static void VerifyRenderer()
    {
        var face = Color.FromRgb(30, 80, 130);
        var hat = Color.FromArgb(128, 230, 120, 10);
        var modern = Assert.IsType<byte[]>(MinecraftHeadRenderer.DecodeSkin(Skin(face, Colors.Transparent)));
        Assert.Equal(face, Pixel(Assert.IsAssignableFrom<BitmapSource>(MinecraftHeadRenderer.ReadHead(modern))));
        var composite = Assert.IsType<byte[]>(MinecraftHeadRenderer.DecodeSkin(Skin(face, hat)));
        Assert.Equal(Color.FromRgb(130, 100, 70), Pixel(Assert.IsAssignableFrom<BitmapSource>(MinecraftHeadRenderer.ReadHead(composite))));
        var legacy = Skin(face, Colors.Black, 32, opaqueLegacy: true);
        Assert.Equal(face, Pixel(MinecraftHeadRenderer.ReadHead(MinecraftHeadRenderer.DecodeSkin(legacy)!)!));
        Assert.Null(MinecraftHeadRenderer.DecodeSkin([]));
        Assert.Null(MinecraftHeadRenderer.DecodeSkin(new byte[1048577]));
        var enormous = Skin(face, Colors.Transparent);
        BinaryPrimitives.WriteInt32BigEndian(enormous.AsSpan(16, 4), int.MaxValue);
        Assert.Null(MinecraftHeadRenderer.DecodeSkin(enormous));
        Assert.Null(MinecraftHeadRenderer.ReadHead(Skin(face, Colors.Transparent)));
        Assert.True(MinecraftHeadRenderer.Steve.IsFrozen);
        Assert.Equal(8, MinecraftHeadRenderer.Steve.PixelWidth);
    }

    /// <summary>迟到结果、切离线、隐藏、失败和移出页面均不能串头像或改变身份。</summary>
    private static void VerifyAvatarLifecycle(Border host)
    {
        var first = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new ControlledCache(first.Task, second.Task);
        var avatar = new PlayerAvatar { AccountId = FirstId };
        PlayerAvatar.SetCache(host, cache);
        host.Child = avatar;
        Flush(host);
        Assert.True(avatar.IsLoaded && avatar.IsVisible);
        Assert.Same(cache, PlayerAvatar.GetCache(avatar));
        Assert.Contains(FirstId, cache.Calls);
        var old = avatar.AvatarTask;
        avatar.AccountId = SecondId;
        var current = avatar.AvatarTask;
        var secondHead = MinecraftHeadRenderer.DecodeSkin(Skin(Colors.DodgerBlue, Colors.Transparent))!;
        second.SetResult(secondHead);
        Wait(current);
        Assert.Equal(Colors.DodgerBlue, Pixel(Head(avatar)));
        first.SetResult(MinecraftHeadRenderer.DecodeSkin(Skin(Colors.Orange, Colors.Transparent)));
        Wait(old);
        Flush(host);
        Assert.Equal(Colors.DodgerBlue, Pixel(Head(avatar)));

        var calls = cache.Calls.Count;
        avatar.IsOnline = false;
        Assert.Same(MinecraftHeadRenderer.Steve, Head(avatar));
        Assert.Equal(calls, cache.Calls.Count);
        avatar.AccountId = FirstId;
        Assert.Equal(calls, cache.Calls.Count);
        avatar.IsOnline = true;
        Wait(avatar.AvatarTask);
        Assert.Equal(Colors.Orange, Pixel(Head(avatar)));
        avatar.Visibility = Visibility.Collapsed;
        Flush(host);
        calls = cache.Calls.Count;
        avatar.AccountId = SecondId;
        Assert.Equal(calls, cache.Calls.Count);
        avatar.Visibility = Visibility.Visible;
        Flush(host);
        Wait(avatar.AvatarTask);
        Assert.Equal(Colors.DodgerBlue, Pixel(Head(avatar)));
        Assert.Equal(BitmapScalingMode.NearestNeighbor, RenderOptions.GetBitmapScalingMode((Image)avatar.FindName("HeadImage")));

        PlayerAvatar.SetCache(host, new BrokenCache());
        Wait(avatar.AvatarTask);
        Assert.Same(MinecraftHeadRenderer.Steve, Head(avatar));
        PlayerAvatar.SetCache(host, null);
        avatar.AccountId = "not-a-uuid";
        Assert.Same(MinecraftHeadRenderer.Steve, Head(avatar));
        avatar.AccountId = null;
        Assert.Same(MinecraftHeadRenderer.Steve, Head(avatar));
        host.Child = null;
        PlayerAvatar.SetCache(host, null);
        Flush(host);
    }

    /// <summary>读取实际生产 Image，验证可见内容而不是仅检查任务是否完成。</summary>
    private static BitmapSource Head(PlayerAvatar avatar) => Assert.IsAssignableFrom<BitmapSource>(((Image)avatar.FindName("HeadImage")).Source);

    /// <summary>真实页面使用公开测试账号；覆盖深浅主题与两倍像素取样，不访问本机档案。</summary>
    private static void VerifyProductionPreviews(Border host)
    {
        var resources = Application.Current.Resources;
        var paletteKeys = AppearancePalette.Create(true).Keys.Append("WallpaperPanelBrush").ToArray();
        var previous = paletteKeys.ToDictionary(key => key, key => resources.Contains(key) ? resources[key] : null);
        try
        {
            foreach (var dark in new[] { true, false })
            {
                var palette = AppearancePalette.Create(dark);
                foreach (var color in palette) resources[color.Key] = new SolidColorBrush(color.Value);
                resources["WallpaperPanelBrush"] = new SolidColorBrush(palette["SurfaceBrush"]);
                PlayerAvatar.SetCache(host, new ImmediateCache());
                var accounts = new PreviewAccounts();
                var page = new AccountsView { DataContext = accounts };
                var panel = new StackPanel { Margin = new Thickness(24) };
                panel.SetResourceReference(Panel.BackgroundProperty, "WindowBackgroundBrush");
                var onlineTitle = new TextBlock { Text = "本地实例 · 正版模式", Margin = new Thickness(0, 0, 0, 12) };
                onlineTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush"); panel.Children.Add(onlineTitle);
                var online = new LaunchIdentitySelector { DataContext = new PreviewIdentity(true) };
                panel.Children.Add(online);
                var offlineTitle = new TextBlock { Text = "启动设置 · 离线模式", Margin = new Thickness(0, 32, 0, 12) };
                offlineTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush"); panel.Children.Add(offlineTitle);
                var offline = new LaunchIdentitySelector { DataContext = new PreviewIdentity(false) };
                panel.Children.Add(offline);
                foreach (var (view, label) in new (FrameworkElement, string)[] { (page, "accounts"), (panel, "launch-modes") })
                {
                    host.Child = view; Flush(host);
                    foreach (var avatar in Descendants(view).OfType<PlayerAvatar>()) Wait(avatar.AvatarTask);
                    if (view == panel)
                    {
                        var onlineAvatar = Assert.Single(Descendants(online).OfType<PlayerAvatar>(), item => item.IsVisible);
                        Assert.Equal(Colors.DodgerBlue, Pixel(Head(onlineAvatar)));
                        Assert.Equal(24, onlineAvatar.ActualWidth);
                        Assert.Equal(24, onlineAvatar.ActualHeight);
                        Assert.Same(MinecraftHeadRenderer.Steve, Head(Assert.IsType<PlayerAvatar>(offline.FindName("SelectedPlayerAvatar")))) ;
                        // Popup 不显示，只创建内容检查继承，保证选项内的头像也复用同一个缓存。
                        var combo = Assert.IsType<ComboBox>(online.FindName("MicrosoftAccountSelector"));
                        var popup = Assert.IsType<Popup>(combo.Template.FindName("PART_Popup", combo));
                        var dropdown = Assert.IsAssignableFrom<FrameworkElement>(popup.Child);
                        dropdown.Measure(new Size(400, 200)); dropdown.Arrange(new Rect(dropdown.DesiredSize)); dropdown.UpdateLayout();
                        foreach (var avatar in Descendants(dropdown).OfType<PlayerAvatar>()) Assert.Same(PlayerAvatar.GetCache(host), PlayerAvatar.GetCache(avatar));
                        Assert.False(popup.IsOpen);
                    }
                    foreach (var scale in new[] { 1, 2 }) Save(host, $"avatars-{(dark ? "dark" : "light")}-{label}-{scale}x.png", scale);
                }
            }
        }
        finally
        {
            host.Child = null; PlayerAvatar.SetCache(host, null);
            foreach (var item in previous) if (item.Value is null) resources.Remove(item.Key); else resources[item.Key] = item.Value;
        }
    }

    /// <summary>有环境变量时才写入头像局部截图，截图数据均为内存测试档案。</summary>
    private static void Save(Border host, string filename, int scale)
    {
        var directory = Environment.GetEnvironmentVariable("EML_TEXTBOX_PREVIEW_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        host.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
        Flush(host);
        var bitmap = new RenderTargetBitmap(720 * scale, 520 * scale, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(host); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, filename)); encoder.Save(output);
    }

    /// <summary>展开控件模板，但不打开下拉框、不执行任何命令。</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        if (root is Control control) control.ApplyTemplate();
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private sealed class PreviewAccounts
    {
        public MinecraftAccount[] SavedAccounts { get; } = [new(FirstId, "FirstPlayer"), new(SecondId, "SecondPlayer")];
        public MinecraftAccount SelectedAccount { get => SavedAccounts[0]; set { } }
        public string AccountId => FirstId;
        public string AccountName => "FirstPlayer";
        public string AccountListStatus => "已保存 2 个正版账号。";
        public string StatusText => "已读取本机登录记录。";
        public string ConfigurationStatus => "";
        public string VerificationAddress => "";
        public string UserCode => "";
        public RelayCommand LoginCommand { get; } = new(() => { });
        public RelayCommand LoadCommand { get; } = new(() => { });
        public RelayCommand SelectAccountCommand { get; } = new(() => { });
        public RelayCommand RemoveAccountCommand { get; } = new(() => { });
    }

    private sealed class PreviewIdentity(bool online)
    {
        public bool CanEditLaunchSettings => true;
        public bool CanEditOfflinePlayerName => !online;
        public bool UseMicrosoftAccount => online;
        public string[] LaunchModes { get; } = ["离线模式", "正版模式"];
        public string SelectedLaunchMode { get => online ? "正版模式" : "离线模式"; set { } }
        public string PlayerName { get; set; } = "OfflinePlayer";
        public MinecraftAccount[] AvailableMicrosoftAccounts { get; } = [new(FirstId, "FirstPlayer"), new(SecondId, "SecondPlayer")];
        public MinecraftAccount SelectedMicrosoftAccount { get => AvailableMicrosoftAccounts[0]; set { } }
        public bool CanUseCurrentMicrosoftAccount => true;
        public string LaunchIdentityHint => online ? "此实例使用所选正版账号。" : "离线模式不使用微软账号；正版服务器需要正版登录。";
        public RelayCommand UseCurrentMicrosoftAccountCommand { get; } = new(() => { });
    }

    private sealed class ImmediateCache : IMinecraftAvatarCache
    {
        public Task<byte[]?> GetAsync(string? accountId, CancellationToken token = default) => Task.FromResult(
            MinecraftHeadRenderer.DecodeSkin(Skin(accountId == FirstId ? Colors.DodgerBlue : Colors.Orange, Colors.Transparent)));
    }

    /// <summary>构造有已知脸部和帽子像素的标准皮肤，不使用真实账号数据。</summary>
    internal static byte[] Skin(Color face, Color hat, int height = 64, bool opaqueLegacy = false)
    {
        var bytes = new byte[64 * height * 4];
        if (opaqueLegacy) for (var index = 3; index < bytes.Length; index += 4) bytes[index] = 255;
        for (var y = 8; y < 16; ++y)
        for (var x = 0; x < 8; ++x)
        {
            Put(bytes, (y * 64 + x + 8) * 4, face);
            Put(bytes, (y * 64 + x + 40) * 4, hat);
        }
        var bitmap = BitmapSource.Create(64, height, 96, 96, PixelFormats.Bgra32, null, bytes, 64 * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream(); encoder.Save(output); return output.ToArray();
    }

    /// <summary>按 BGRA 写入完整像素，保留透明帽子层供合成测试。</summary>
    private static void Put(byte[] data, int offset, Color color)
    { data[offset] = color.B; data[offset + 1] = color.G; data[offset + 2] = color.R; data[offset + 3] = color.A; }

    /// <summary>统一格式后读取头像首像素，避免 PNG 编码器选择调色板影响断言。</summary>
    private static Color Pixel(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var bytes = new byte[8 * 8 * 4]; converted.CopyPixels(bytes, 32, 0);
        return Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
    }

    /// <summary>运行绑定和加载事件，不引入固定等待。</summary>
    private static void Flush(Border host)
    {
        host.Measure(new Size(720, 520)); host.Arrange(new Rect(0, 0, 720, 520)); host.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));
    }

    /// <summary>泵送 UI continuation，且任何异步回归超时都应明确失败。</summary>
    private static void Wait(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var dispatcher = Dispatcher.CurrentDispatcher;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) => frame.Continue = false;
            task.GetAwaiter().OnCompleted(() => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
            timer.Start();
            try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        }
        Assert.True(task.IsCompleted, "等待头像展示超时。"); task.GetAwaiter().GetResult();
    }

    /// <summary>故意忽略取消，确保控件自身拒绝迟到的旧账号头像。</summary>
    private sealed class ControlledCache(Task<byte[]?> first, Task<byte[]?> second) : IMinecraftAvatarCache
    {
        internal List<string?> Calls { get; } = [];
        public Task<byte[]?> GetAsync(string? accountId, CancellationToken token = default)
        { Calls.Add(accountId); return accountId == FirstId ? first : second; }
    }

    /// <summary>读取失败必须静默回退，而不是中断页面或弹出登录错误。</summary>
    private sealed class BrokenCache : IMinecraftAvatarCache
    {
        public Task<byte[]?> GetAsync(string? accountId, CancellationToken token = default) => Task.FromException<byte[]?>(new IOException("测试读取失败"));
    }
}
