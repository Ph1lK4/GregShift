using System.Windows.Threading;
using EML.App.Controls;

namespace EML.App.Tests;

public sealed class GtnhToolBrowserTests
{
    [Theory]
    [InlineData("https://gtnh.huijiwiki.com/wiki/模板:FlowChart/轻燃油", true)]
    [InlineData("https://gtnhquestsbook.top/", true)]
    [InlineData("https://GTNHQUESTSBOOK.TOP/#chapter", true)]
    [InlineData("https://chunkbasenh.beyomoes.com/", true)]
    [InlineData("https://chunkbasenh.beyomoes.com.evil.example/", false)]
    [InlineData("http://gtnhquestsbook.top/", false)]
    [InlineData("https://gtnhquestsbook.top:444/", false)]
    [InlineData("https://player@gtnhquestsbook.top/", false)]
    [InlineData("https://gtnhquestsbook.top.evil.example/", false)]
    [InlineData("https://127.0.0.1/", false)]
    [InlineData("file:///C:/Users/player/token.json", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,hello", false)]
    [InlineData("ms-settings:", false)]
    [InlineData(null, false)]
    public void EmbeddedNavigationDoesNotBecomeAnArbitraryBrowser(string? address, bool expected) =>
        Assert.Equal(expected, GtnhToolBrowser.IsAllowedAddress(address));

    [Fact]
    public async Task ConstructingOrBindingHiddenReaderDoesNotCreateBrowserOrReadFiles()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var reader = new GtnhToolBrowser
                {
                    RequestedAddress = "https://gtnhquestsbook.top/",
                    UserDataFolder = @"C:\NeverCreatedByGtnhToolBrowserTests"
                };
                Assert.False(reader.IsBrowserCreated);
                Assert.True(reader.IsPermittedAddress("https://gtnhquestsbook.top/#chapter"));
                Assert.False(reader.IsPermittedAddress("https://chunkbasenh.beyomoes.com/"));
                reader.RequestedAddress = "https://chunkbasenh.beyomoes.com/";
                Assert.True(reader.IsPermittedAddress("https://chunkbasenh.beyomoes.com/?seed=0"));
                Assert.False(reader.IsPermittedAddress("https://gtnhquestsbook.top/"));
                Assert.False(reader.IsPermittedAddress("https://gtnhplanner.com/"));
                reader.RequestedAddress = "https://gtnh.huijiwiki.com/wiki/研究#研究一览";
                Assert.True(reader.IsPermittedAddress("https://gtnh.huijiwiki.com/wiki/研究"));
                Assert.False(reader.IsPermittedAddress("https://chunkbasenh.beyomoes.com/"));
                reader.IsActive = true; // A bound but unhosted control still does not initialize.
                Assert.False(reader.IsBrowserCreated);
                reader.IsActive = false;
                reader.RequestedAddress = null;
                reader.Reload(); reader.GoBack(); reader.GoForward();
                Assert.False(reader.IsBrowserCreated);
                reader.Dispose(); reader.Dispose();
                completed.SetResult();
            }
            catch (Exception ex) { completed.SetException(ex); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true, Name = "GTNH embedded reader lazy initialization" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }
}
