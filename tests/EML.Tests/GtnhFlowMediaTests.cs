using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using EML.Core.Minecraft.Gtnh.Tools;
using EML.Infrastructure.Minecraft.Gtnh;

namespace EML.Tests;

public sealed class GtnhFlowMediaTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "EML-FlowMedia-" + Guid.NewGuid().ToString("N"));
    private static readonly GtnhFlowChart Chart = new("media", "配方测试", "工艺流程图", "模板:FlowChart/配方测试", true);
    private const string Address = "https://huiji-thumb.huijistatic.com/gtnh/uploads/thumb/a/ab/Icon.png/32px-Icon.png";
    private const string Payload = """
        {"views":{"zh":{"schemaVersion":1,"lang":"zh","title":"配方测试","nodes":[
        {"id":"i1","label":"原料","class":"start","x":0,"y":0},{"id":"r1","label":"蒸馏室","class":"recipe","x":1,"y":0}],"edges":[],"groups":[]}}}
        """;
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a5wwAAAAASUVORK5CYII=");

    [Theory]
    [InlineData("https://huiji-public.huijistatic.com/gtnh/uploads/a/a1/icon.png", true)]
    [InlineData(Address, true)]
    [InlineData("https://huiji-thumb.huijistatic.com/other/icon.png", false)]
    [InlineData("http://huiji-public.huijistatic.com/gtnh/uploads/icon.png", false)]
    [InlineData("file:///C:/Users/player/icon.png", false)]
    [InlineData("https://huiji-public.huijistatic.com.evil.test/gtnh/uploads/icon.png", false)]
    [InlineData("https://player@huiji-public.huijistatic.com/gtnh/uploads/icon.png", false)]
    [InlineData("https://huiji-public.huijistatic.com:8443/gtnh/uploads/icon.png", false)]
    [InlineData("https://huiji-public.huijistatic.com/gtnh/uploads/icon.png?q=1", false)]
    [InlineData("https://huiji-public.huijistatic.com/gtnh/uploads/icon.svg", false)]
    [InlineData("https://huiji-public.huijistatic.com/gtnh/uploads/icon.png#fragment", false)]
    [InlineData("data:image/png;base64,AAAA", false)]
    public void OnlyFixedPngCdnAddressesAreAllowed(string value, bool expected) =>
        Assert.Equal(expected, GtnhFlowMediaParser.IsAllowedIconAddress(value));

    [Fact]
    public void MediaBindsByLanguageAndNodeWithoutInferringRecipes()
    {
        var json = MediaPayload();
        var view = GtnhFlowDocumentParser.ParsePayload(json.ToJsonString()).Views[0];
        Assert.Equal(Address, view.Nodes[0].Icon?.Address);
        Assert.Null(view.Nodes[0].Recipes);
        var recipe = Assert.Single(view.Nodes[1].Recipes!);
        Assert.Equal("蒸馏室", recipe.Title);
        var item = Assert.Single(Assert.Single(recipe.Slots).Items);
        Assert.Equal("编程电路2", item.Name); Assert.Equal("NC", item.Annotation);
        Assert.Equal(["使用：24 EU/t (LV)", "时间：1.5 秒"], recipe.Information);
        Assert.Equal(100, recipe.Slots[0].X); Assert.Equal(40, recipe.Slots[0].Y);
    }

    [Theory]
    [InlineData("address")]
    [InlineData("reference")]
    [InlineData("language")]
    [InlineData("frames")]
    [InlineData("speed")]
    [InlineData("x")]
    [InlineData("width")]
    [InlineData("version")]
    [InlineData("nonrecipe")]
    [InlineData("duplicateicon")]
    public void InvalidMediaNeverBecomesTrusted(string kind)
    {
        var root = MediaPayload(); var media = root["gregShiftMedia"]!;
        var view = media["views"]!["zh"]!; var icon = view["icons"]![0]!;
        switch (kind)
        {
            case "address": icon["icon"]!["address"] = "https://evil.test/icon.png"; break;
            case "reference": icon["nodeId"] = "missing"; break;
            case "language": media["views"]!["xx"] = view.DeepClone(); break;
            case "frames": icon["icon"]!["frames"] = 513; break;
            case "speed": icon["icon"]!["frameSeconds"] = .001; break;
            case "x": view["recipes"]![0]!["slots"]![0]!["x"] = 99999; break;
            case "width": view["recipes"]![0]!["width"] = 99999; break;
            case "version": media["version"] = 2; break;
            case "nonrecipe": view["recipes"]![0]!["nodeId"] = "i1"; break;
            default: view["icons"]!.AsArray().Add(icon.DeepClone()); break;
        }
        Assert.Throws<InvalidDataException>(() => GtnhFlowDocumentParser.ParsePayload(root.ToJsonString()));
    }

    [Fact]
    public async Task HtmlExtractionPreservesSlotsNumbersAndInformationAndCachesPngForOffline()
    {
        using var handler = new Handler(request => request.RequestUri == Chart.Address ? Html(Page()) : Image(Png));
        using var http = new HttpClient(handler); using var service = new GtnhFlowChartService(_root, http);
        var content = await service.LoadAsync(Chart);
        Assert.True(content.HasMedia); Assert.Null(content.Warning);
        Assert.Single(content.IconData); Assert.Equal(Png, content.IconData[Address]);
        var recipe = Assert.Single(content.Document.Views[0].Nodes[1].Recipes!);
        Assert.Equal("编程电路2", recipe.Slots[0].Items[0].Name);
        Assert.Equal("NC", recipe.Slots[0].Items[0].Annotation);
        Assert.Equal("50L", recipe.Slots[1].Items[0].Annotation);
        Assert.Equal("时间：1.5 秒", recipe.Information[1]);
        Assert.Equal(2, handler.RequestCount);
        using var offlineHandler = new Handler(_ => throw new HttpRequestException("offline"));
        using var offlineHttp = new HttpClient(offlineHandler); using var offline = new GtnhFlowChartService(_root, offlineHttp);
        var loaded = await offline.LoadAsync(Chart);
        Assert.True(loaded.FromCache); Assert.True(loaded.HasMedia); Assert.Null(loaded.Warning);
        Assert.Equal(Png, loaded.IconData[Address]); Assert.Equal(0, offlineHandler.RequestCount);
    }

    [Theory]
    [InlineData("html")]
    [InlineData("redirect")]
    [InlineData("dimensions")]
    [InlineData("oversize")]
    public async Task BadImagesRetainRecipeAndTextWithoutCachingThem(string kind)
    {
        using var handler = new Handler(request =>
        {
            if (request.RequestUri == Chart.Address) return Html(Page());
            if (kind == "html") return Html("<html>untrusted</html>");
            if (kind == "redirect") return new(HttpStatusCode.Redirect) { Headers = { Location = new("https://evil.test/a.png") } };
            var data = (byte[])Png.Clone();
            if (kind == "dimensions") data[16] = 255;
            var response = Image(data);
            if (kind == "oversize") response.Content.Headers.ContentLength = 2 * 1024 * 1024 + 1;
            return response;
        });
        using var http = new HttpClient(handler); using var service = new GtnhFlowChartService(_root, http);
        var content = await service.LoadAsync(Chart);
        Assert.Empty(content.IconData); Assert.Contains("部分图标", content.Warning);
        Assert.Single(content.Document.Views[0].Nodes[1].Recipes!);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "cache"), "*.png", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task BrowserImportAcceptsOnlyValidatedMediaAndCancellationWins()
    {
        using var handler = new Handler(_ => Image(Png)); using var http = new HttpClient(handler);
        using var service = new GtnhFlowChartService(_root, http);
        var imported = await service.ImportPageDataAsync(Chart, MediaPayload().ToJsonString(), 5);
        Assert.True(imported.HasMedia); Assert.Single(imported.IconData);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ImportPageDataAsync(Chart, MediaPayload().ToJsonString(), 6, cancellation.Token));
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HtmlReadsRealSpriteFrameCountWithoutExecutingCss(bool background)
    {
        var tag = "<img src='" + Address + "'>";
        var sprite = background ? "<span class='grid-sprite' style='background-image:url(" + Address + ");animation:gridSprite 3.2s steps(64) infinite'></span>" :
            "<span class='grid-sprite-file' style='--grid-sprite-frames:64;--grid-sprite-speed:3.2s'>" + tag + "</span>";
        var html = Page().Replace(tag, sprite);
        using var handler = new Handler(request => request.RequestUri == Chart.Address ? Html(html) : new(HttpStatusCode.ServiceUnavailable));
        using var http = new HttpClient(handler); using var service = new GtnhFlowChartService(_root, http);
        var loaded = await service.LoadAsync(Chart);
        var icon = loaded.Document.Views[0].Nodes[0].Icon;
        Assert.NotNull(icon); Assert.Equal(64, icon.Frames); Assert.Equal(.05, icon.FrameSeconds);
        Assert.Single(loaded.Document.Views[0].Nodes[1].Recipes!);
    }

    [Fact]
    public async Task ScriptAndCommentLiteralsAreNotMediaSources()
    {
        var fake = "<div class='fcd3-icon-source' data-node='i1'><img src='" + Address + "'></div>";
        var html = "<script>const a='</scripture>" + fake + "';</script><!--" + fake + "-->" +
            "<span class='fcd3-payload'>" + WebUtility.HtmlEncode(Payload) + "</span>";
        using var handler = new Handler(_ => Html(html)); using var http = new HttpClient(handler);
        using var service = new GtnhFlowChartService(_root, http);
        var loaded = await service.LoadAsync(Chart);
        Assert.False(loaded.HasMedia); Assert.Null(loaded.Document.Views[0].Nodes[0].Icon); Assert.Empty(loaded.IconData);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task HtmlMediaStaysInsideItsChartAndLanguage()
    {
        const string englishAddress = "https://huiji-public.huijistatic.com/gtnh/uploads/e/en/english.png";
        var root = JsonNode.Parse(Payload)!;
        root["views"]!["en"] = root["views"]!["zh"]!.DeepClone();
        root["views"]!["en"]!["lang"] = "en";
        var outside = "<div class='fcd3-icon-source' data-node='i1'><img src='" + englishAddress + "'></div>";
        var localized = "<div class='fcd3-language-sources' data-lang='en'>" + outside + "</div>";
        var page = outside + Page().Replace(WebUtility.HtmlEncode(Payload), WebUtility.HtmlEncode(root.ToJsonString()))
            .Replace("<div class='fcd3-sources'>", localized + "<div class='fcd3-sources'>");
        using var handler = new Handler(request => request.RequestUri == Chart.Address ? Html(page) : Image(Png));
        using var http = new HttpClient(handler); using var service = new GtnhFlowChartService(_root, http);
        var loaded = await service.LoadAsync(Chart);
        Assert.Equal(Address, loaded.Document.Views.Single(view => view.Language == "zh").Nodes[0].Icon?.Address);
        Assert.Equal(englishAddress, loaded.Document.Views.Single(view => view.Language == "en").Nodes[0].Icon?.Address);
        Assert.Null(loaded.Document.Views.Single(view => view.Language == "en").Nodes[1].Recipes);
    }

    private static JsonNode MediaPayload()
    {
        var root = JsonNode.Parse(Payload)!;
        root["gregShiftMedia"] = JsonNode.Parse("""
            {"version":1,"views":{"zh":{"icons":[{"nodeId":"i1","icon":{"address":"ADDRESS","frames":1,"frameSeconds":0.05}}],"recipes":[
            {"nodeId":"r1","title":"蒸馏室","width":348,"height":168,"slots":[{"x":100,"y":40,"items":[{"name":"编程电路2","annotation":"NC","icon":{"address":"ADDRESS","frames":1,"frameSeconds":0.05}}]}],"information":["使用：24 EU/t (LV)","时间：1.5 秒"]}]}}}
            """.Replace("ADDRESS", Address));
        return root;
    }
    private static string Page() => "<html><div class='gtnh-flowchart-d3'><span class='fcd3-payload'>" + WebUtility.HtmlEncode(Payload) + "</span>" +
        "<div class='fcd3-icon-source' data-node='i1'><img src='" + Address + "'></div>" +
        "<div class='fcd3-sources'><div class='fcd3-language-sources' data-lang='zh'><div class='fcd3-tooltip-source' data-node='r1'>" +
        "<div class='grid-recipe'><div class='grid-bg-title'>蒸馏室</div><div class='grid-bg-board' style='height:168px;width:348px'>" +
        "<div class='grid-item-component' style='top:40px;left:100px'><span class='nei-tt'><img src='" + Address + "'><div class='item-annotation up-left'>NC</div>" +
        "<span class='nei-data' data-json='{&quot;name&quot;:&quot;编程电路2&quot;}'></span></span></div>" +
        "<div class='grid-item-component' style='top:118px;left:208px'><span class='nei-tt'><img src='" + Address + "'><div class='item-annotation liquid'>50L</div>" +
        "<span class='nei-data' data-json='{&quot;name&quot;:&quot;石油&quot;}'></span></span></div></div>" +
        "<div class='recipe-information-text'>使用：24 EU/t (LV)<br>时间：1.5 秒</div></div></div></div></div></div></html>";
    private static HttpResponseMessage Html(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "text/html") };
    private static HttpResponseMessage Image(byte[] value)
    { var content = new ByteArrayContent(value); content.Headers.ContentType = new("image/png"); return new(HttpStatusCode.OK) { Content = content }; }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        private int _requests;
        internal int RequestCount => _requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Interlocked.Increment(ref _requests); return Task.FromResult(response(request)); }
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
