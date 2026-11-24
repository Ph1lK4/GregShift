using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EML.App.Services;
using EML.App.ViewModels;
using EML.Core.Downloads;
using EML.Core.Minecraft.Catalog;
using EML.Core.Minecraft.Gtnh;
using EML.Core.Minecraft.Migration;

namespace EML.App.Tests;

/// <summary>检查真实弹窗的绑定、明确选择、筛选和取消；不下载文件或打开系统窗口。</summary>
internal static class GtnhUpgradeVersionDialogChecks
{
    internal static void Run(Action<DialogWindow, string> render)
    {
        var owner = new Window();
        try
        {
            foreach (var empty in new[] { true, false })
            {
                using var model = Model(empty);
                var dialogs = new InstanceMigrationDialogs(owner, window =>
                {
                    try
                    {
                        Assert.True(window.CancelButton.IsDefault);
                        Assert.True(window.CancelButton.IsCancel);
                        var accept = Accept(window);
                        Assert.False(accept.IsEnabled);
                        Assert.Null(model.SelectedRelease);
                        var list = Assert.Single(window.Body.Children.OfType<ListBox>());
                        render(window, empty ? "dialog-upgrade-version-empty" : "dialog-upgrade-version-choose");
                        Assert.Same(model.Releases, list.ItemsSource);
                        Assert.Equal(empty ? 0 : 2, list.Items.Count);
                        if (empty) return false;
                        Assert.Contains(Descendants(list).OfType<TextBlock>(), text =>
                            text.Text == model.Releases[0].DisplayName && text.ActualWidth > 100 && text.ActualHeight > 0);
                        var modernJava = model.Releases.Single(release => release.JavaVersion == "17-26");
                        list.SelectedItem = modernJava;
                        Assert.Same(modernJava, model.SelectedRelease);
                        Assert.True(accept.IsEnabled);
                        render(window, "dialog-upgrade-version-selected");
                        var filters = Assert.Single(window.Body.Children.OfType<WrapPanel>());
                        Assert.Single(filters.Children.OfType<ComboBox>()).SelectedIndex = 2;
                        Assert.Single(model.Releases);
                        Assert.Null(model.SelectedRelease);
                        Assert.False(accept.IsEnabled);
                        list.SelectedItem = model.Releases[0];
                        Assert.True(accept.IsEnabled);
                        return true;
                    }
                    finally { window.Close(); }
                });
                var result = dialogs.ChooseUpgradeVersionAsync(model).GetAwaiter().GetResult();
                if (empty) Assert.Null(result);
                else { Assert.NotNull(result); Assert.Equal("8", result.JavaVersion); }
            }
            using var cancelledModel = Model(false);
            using var cancellation = new CancellationTokenSource();
            var cancelled = new InstanceMigrationDialogs(owner, window =>
            {
                cancelledModel.SelectedRelease = cancelledModel.Releases[0];
                cancellation.Cancel();
                window.Close();
                return true;
            });
            Assert.Throws<OperationCanceledException>(() => { cancelled.ChooseUpgradeVersionAsync(cancelledModel, cancellation.Token).GetAwaiter().GetResult(); });
            var showCount = 0;
            var closed = new InstanceMigrationDialogs(owner, window =>
            {
                showCount++; window.Close(); return false;
            });
            Assert.Throws<OperationCanceledException>(() => { closed.ChooseUpgradeVersionAsync(cancelledModel, cancellation.Token).GetAwaiter().GetResult(); });
            Assert.Equal(0, showCount);
            VerifyAppearance(owner);
        }
        finally { owner.Close(); }
    }

    private static Button Accept(DialogWindow window) => Assert.Single(window.Actions.Children.OfType<Button>(), button => !button.IsCancel);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    /// <summary>深浅主题及两种 DPI 下，长正文可滚动且固定确认栏始终可达。</summary>
    private static void VerifyAppearance(Window owner)
    {
        var application = Application.Current;
        var dictionaries = application.Resources.MergedDictionaries.ToArray();
        var saved = application.Resources.Keys.Cast<object>().ToDictionary(key => key, key => application.Resources[key]);
        var directory = Path.Combine(Path.GetTempPath(), "EML-upgrade-dialog-" + Guid.NewGuid().ToString("N"));
        using var appearance = new AppearanceService(directory, watchSystemTheme: false);
        try
        {
            foreach (var dark in new[] { true, false })
            foreach (var scale in new[] { 1d, 2d })
            foreach (var compact in new[] { false, true })
            {
                appearance.Apply(appearance.Settings with { ThemeMode = dark ? AppearanceThemeMode.Dark : AppearanceThemeMode.Light });
                using var model = Model(false);
                var dialogs = new InstanceMigrationDialogs(owner, window =>
                {
                    try
                    {
                        model.SelectedRelease = model.Releases[0];
                        Assert.True(Accept(window).IsEnabled);
                        DialogAppearanceChecks.RenderScaled(window,
                            $"dialog-upgrade-{(dark ? "dark" : "light")}-{(compact ? "compact" : "normal")}-{(int)(96 * scale)}dpi",
                            compact ? 480 : 780, compact ? 420 : 710, scale);
                        return false;
                    }
                    finally { window.Close(); }
                });
                Assert.Null(dialogs.ChooseUpgradeVersionAsync(model).GetAwaiter().GetResult());
            }
        }
        finally
        {
            application.Resources.Clear();
            application.Resources.MergedDictionaries.Clear();
            foreach (var dictionary in dictionaries) application.Resources.MergedDictionaries.Add(dictionary);
            foreach (var (key, value) in saved) application.Resources[key] = value;
        }
    }

    private static GtnhPackDownloadViewModel Model(bool empty)
    {
        var model = new GtnhPackDownloadViewModel(new Versions(empty),
            (_, _, _) => throw new InvalidOperationException("选择弹窗不得下载。"), () => true);
        model.ConfigureUpgrade(new(@"C:\preview\old", "GTNH 2.8.4 · 旧存档", @"C:\preview\old\.minecraft", "2.8.4"));
        model.SetActive(true);
        model.ReleasesLoadTask.GetAwaiter().GetResult();
        return model;
    }

    private sealed class Versions(bool empty) : IGtnhPackService
    {
        public Task<IReadOnlyList<GtnhPackRelease>> GetReleasesAsync(bool includePrereleases = false, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GtnhPackRelease>>(empty ? [] : [Pack("8"), Pack("17-26")]);
        private static GtnhPackRelease Pack(string java) => new()
        { Id = "new-" + java, Version = "2.9.0", JavaVersion = java, FileName = "GTNH-2.9.0-prism-java" + java + ".zip", Size = 123456789 };
        public Task<PreparedInstanceArchive> PrepareAsync(string releaseId, IProgress<FileDownloadProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("选择弹窗不得准备原包。");
    }
}
