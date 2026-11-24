using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EML.Core.Minecraft.Gtnh.Tools;
using EML.Infrastructure.Minecraft.Gtnh;

namespace EML.Tests;

/// <summary>以合成图表覆盖不可信数据、离线复用与缓存往返，不依赖站点在线状态。</summary>
public sealed class GtnhFlowChartServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "EML-FlowChartTests-" + Guid.NewGuid().ToString("N"));
    private static readonly GtnhFlowChart Chart = new("sample", "合成流程", "工艺流程图", "模板:FlowChart/合成流程", true);
    private const string Payload = """
        {"views":{"zh":{"schemaVersion":1,"lang":"zh","title":"合成流程","nodes":[
          {"id":"i1","label":"原料","class":"start","x":-1,"y":0},
          {"id":"r1","label":"测试机器\nLV","class":"recipe","x":0,"y":0.5},
          {"id":"i2","label":"产物","class":"end","x":1,"y":0}],
          "edges":[{"id":"e1","source":"i1","target":"r1","class":"prime","curveness":0},
                   {"id":"e2","source":"r1","target":"i2","class":"default","curveness":0.25}],
          "groups":[{"id":"route:prime","label":"主线","kind":"route","nodes":["i1","r1"],"edges":["e1"]}]}}}
        """;

    [Fact]
    public void ParsesCoordinatesEdgesGroupsAndLanguagesWithoutExecutingCode()
    {
        var root = JsonNode.Parse(Payload)!;
        root["views"]!["en"] = root["views"]!["zh"]!.DeepClone();
        root["views"]!["en"]!["title"] = "Synthetic Flow";
        root["views"]!["en"]!["lang"] = "en";
        var document = GtnhFlowDocumentParser.ParsePayload(root.ToJsonString());
        Assert.Equal(2, document.Views.Count);
        var view = document.Views[0];
        Assert.Equal("zh", view.Language); Assert.Equal("合成流程", view.Title);
        Assert.Equal(3, view.Nodes.Count); Assert.Equal(-1, view.Nodes[0].X);
        Assert.Equal(0.5, view.Nodes[1].Y); Assert.Equal("测试机器\nLV", view.Nodes[1].Label);
        Assert.Equal("r1", view.Edges[0].Target); Assert.Equal(0.25, view.Edges[1].Curveness);
        Assert.Equal(["i1", "r1"], view.Groups[0].Nodes); Assert.Equal(["e1"], view.Groups[0].Edges);
    }

    [Theory]
    [InlineData("class='fcd3-payload'")]
    [InlineData("class=\"fcd3-payload\" style='display:none'")]
    [InlineData("style='display:none' class='extra fcd3-payload more'")]
    [InlineData("CLASS='fcd3-payload'")]
    public void HtmlPayloadExtractionAllowsAttributeOrderAndEntityEncoding(string attributes)
    {
        var html = "<main><span class='other'>not JSON</span><SPAN " + attributes + ">" + WebUtility.HtmlEncode(Payload) + "</SPAN></main>";
        Assert.Equal(Payload, GtnhFlowDocumentParser.ExtractPayload(html));
    }

    [Theory]
    [InlineData("<span class='fcd3-payloadevil'>{}</span>")]
    [InlineData("<span data-class='fcd3-payload'>{}</span>")]
    [InlineData("<script class='fcd3-payload'>{}</script>")]
    [InlineData("<span class='fcd3-payload'>{}</span><span class='fcd3-payload'>{}</span>")]
    [InlineData("<html>请完成浏览器验证</html>")]
    public void UnknownOrAmbiguousMarkupCannotBecomeAChart(string html) =>
        Assert.Throws<InvalidDataException>(() => GtnhFlowDocumentParser.ExtractPayload(html));

    [Theory]
    [InlineData("<!--", "-->")]
    [InlineData("<script>", "</script>")]
    [InlineData("<SCRIPT data-example='>'>", "</SCRIPT>")]
    [InlineData("<script type='application/json'>", "</script>")]
    [InlineData("<style>", "</style>")]
    [InlineData("<STYLE media='all > screen'>", "</STYLE>")]
    [InlineData("<div title='", "'></div>")]
    public void SpanLiteralsOutsideElementContentCannotBecomeCharts(string prefix, string suffix)
    {
        var html = prefix + "<span class=\"fcd3-payload\">" + Payload + "</span>" + suffix;
        Assert.Throws<InvalidDataException>(() => GtnhFlowDocumentParser.ExtractPayload(html));
    }

    [Theory]
    [InlineData("<!--", "-->")]
    [InlineData("<script>", "</script>")]
    [InlineData("<SCRIPT data-example='>'>", "</SCRIPT>")]
    [InlineData("<style>", "</style>")]
    [InlineData("<div title='", "'></div>")]
    public void RealPayloadSurvivesScriptsAndCommentsBeforeAndAfterIt(string prefix, string suffix)
    {
        var noise = prefix + "<span class=\"fcd3-payload\">" + Payload + "</span>" + suffix;
        var html = "<main>" + noise + "<span class='fcd3-payload'>" + WebUtility.HtmlEncode(Payload) + "</span>" + noise + "</main>";
        Assert.Equal(Payload, GtnhFlowDocumentParser.ExtractPayload(html));
    }

    [Theory]
    [InlineData("<!--")]
    [InlineData("<script>")]
    [InlineData("<style>")]
    [InlineData("<script data-value='")]
    [InlineData("<style data-value='")]
    public void UnclosedIgnoredContextIsRejectedEvenWhenItContainsAPayload(string prefix)
    {
        var html = prefix + "<span class=\"fcd3-payload\">" + Payload + "</span>";
        Assert.Throws<InvalidDataException>(() => GtnhFlowDocumentParser.ExtractPayload(html));
    }

    [Fact]
    public void RawTextClosingTagMustMatchTheExactTagName()
    {
        var html = "<script>const text='</scripture><span class=\"fcd3-payload\">" + Payload + "</span>';</script>";
        Assert.Throws<InvalidDataException>(() => GtnhFlowDocumentParser.ExtractPayload(html));
        Assert.Equal(Payload, GtnhFlowDocumentParser.ExtractPayload("<scripture><span class='fcd3-payload'>" +
            WebUtility.HtmlEncode(Payload) + "</span></scripture>"));
    }

    [Fact]
    public void LabelsRemainInertText()
    {
        var json = Payload.Replace("原料", "<script>alert(1)</script>");
        Assert.Equal("<script>alert(1)</script>", GtnhFlowDocumentParser.ParsePayload(json).Views[0].Nodes[0].Label);
    }

    [Theory]
    [InlineData("\"schemaVersion\":1", "\"schemaVersion\":2")]
    [InlineData("\"id\":\"r1\"", "\"id\":\"i1\"")]
    [InlineData("\"target\":\"r1\"", "\"target\":\"absent\"")]
    [InlineData("\"id\":\"e2\"", "\"id\":\"e1\"")]
    [InlineData("\"nodes\":[\"i1\",\"r1\"]", "\"nodes\":[\"absent\"]")]
    [InlineData("\"edges\":[\"e1\"]", "\"edges\":[\"absent\"]")]
    [InlineData("\"x\":-1", "\"x\":1001")]
    [InlineData("\"x\":-1", "\"x\":1e999")]
    [InlineData("\"x\":-1", "\"x\":\"NaN\"")]
    [InlineData("\"x\":-1", "\"x\":NaN")]
    [InlineData("\"curveness\":0.25", "\"curveness\":11")]
    [InlineData("\"x\":-1", "\"x\":null")]
    [InlineData("\"x\":-1", "\"x\":1,\"x\":2")]
    [InlineData("\"label\":\"原料\"", "\"label\":\"\"")]
    [InlineData("\"zh\":", "\"en\":")]
    public void InvalidGraphNeverReturnsAPartialDocument(string oldValue, string newValue) =>
        Assert.Throws<InvalidDataException>(() => GtnhFlowDocumentParser.ParsePayload(Payload.Replace(oldValue, newValue, StringComparison.Ordinal)));

    [Fact]
    public void BoundsPayloadCountDepthAndText()
    {
        Assert.Throws<InvalidDataException>(() => GtnhFlowDocumentParser.ParsePayload(new string(' ', GtnhFlowDocumentParser.MaximumPayloadCharacters + 1)));
        Assert.Throws<InvalidDataException>(() => GtnhFlowDocumentParser.ParsePayload(new string('[', 40) + new string(']', 40)));
        Assert.Throws<InvalidDataException>(() => GtnhFlowDocumentParser.ParsePayload(Payload.Replace("原料", new string('字', 2049))));
        var root = JsonNode.Parse(Payload)!;
        var nodes = root["views"]!["zh"]!["nodes"]!.AsArray();
        for (var index = 3; index <= 2048; index++) nodes.Add(new JsonObject { ["id"] = "n" + index, ["label"] = "node", ["class"] = "default", ["x"] = 0, ["y"] = 0 });
        Assert.Throws<InvalidDataException>(() => GtnhFlowDocumentParser.ParsePayload(root.ToJsonString()));
    }

    [Theory]
    [InlineData("{\"wgRevisionId\":716003}", 716003L)]
    [InlineData("{'wgRevisionId':42}", 42L)]
    [InlineData("{\"wgRevisionId\":0}", null)]
    [InlineData("<html></html>", null)]
    public void ReadsRevisionWithoutRunningBootstrapCode(string html, long? expected) =>
        Assert.Equal(expected, GtnhFlowDocumentParser.ReadRevisionId(html));

    [Fact]
    public async Task PayloadAndMetadataRoundTripAcrossServiceInstancesWithoutNetwork()
    {
        using var firstHandler = new Handler(_ => Html(Page())); using var firstHttp = new HttpClient(firstHandler);
        using (var first = new GtnhFlowChartService(_root, firstHttp))
        {
            var result = await first.LoadAsync(Chart);
            Assert.False(result.FromCache); Assert.Equal(716003, result.RevisionId);
            Assert.Equal(Chart.Address.AbsoluteUri, result.SourceAddress); Assert.Null(result.Warning);
        }
        var files = Directory.GetFiles(CacheRoot());
        Assert.Single(files); Assert.Matches("^[0-9a-f]{64}\\.json$", Path.GetFileName(files[0]));
        Assert.DoesNotContain("<script>", await File.ReadAllTextAsync(files[0]));
        using var offlineHandler = new Handler(_ => throw new HttpRequestException("offline")); using var offlineHttp = new HttpClient(offlineHandler);
        using var second = new GtnhFlowChartService(_root, offlineHttp);
        var restored = await second.LoadAsync(Chart);
        Assert.True(restored.FromCache); Assert.Equal(Payload, restored.PayloadJson); Assert.Equal(716003, restored.RevisionId);
        Assert.Equal(3, restored.Document.Views[0].Nodes.Count); Assert.Empty(offlineHandler.Requests);
        Assert.Contains("贡献者", restored.Attribution); Assert.Contains("action=history", restored.HistoryAddress);
        Assert.Equal(GtnhFlowChartContent.LicenseName, JsonNode.Parse(await File.ReadAllTextAsync(files[0]))!["License"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("http")]
    [InlineData("html")]
    [InlineData("schema")]
    public async Task FailedRefreshKeepsValidOldCache(string failure)
    {
        var fail = false;
        using var handler = new Handler(_ => !fail ? Html(Page()) : failure switch
        {
            "http" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            "schema" => Html(Page(Payload.Replace("\"schemaVersion\":1", "\"schemaVersion\":99"))),
            _ => Html("<html>站点维护</html>")
        });
        using var http = new HttpClient(handler); using var service = new GtnhFlowChartService(_root, http);
        var initial = await service.LoadAsync(Chart); var path = Assert.Single(Directory.GetFiles(CacheRoot()));
        var before = await File.ReadAllBytesAsync(path); fail = true;
        var result = await service.LoadAsync(Chart, refresh: true);
        Assert.True(result.FromCache); Assert.Contains("继续使用本地缓存", result.Warning);
        Assert.Equal(initial.PayloadJson, result.PayloadJson); Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ForbiddenWithoutCacheRequiresBrowserWithoutWritingToDisk()
    {
        using var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var http = new HttpClient(handler); using var service = new GtnhFlowChartService(_root, http);
        var exception = await Assert.ThrowsAsync<GtnhFlowBrowserRequiredException>(() => service.LoadAsync(Chart));
        Assert.Null(exception.CachedContent);
        Assert.Equal(HttpStatusCode.Forbidden, Assert.IsType<HttpRequestException>(exception.InnerException).StatusCode);
        Assert.Contains("浏览器", exception.Message);
        Assert.False(Directory.Exists(_root)); Assert.Equal(Chart.Address, Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task ForbiddenRefreshRequiresBrowserAndPreservesValidatedCache()
    {
        var forbidden = false;
        using var handler = new Handler(_ => forbidden ? new HttpResponseMessage(HttpStatusCode.Forbidden) : Html(Page()));
        using var http = new HttpClient(handler); using var service = new GtnhFlowChartService(_root, http);
        var initial = await service.LoadAsync(Chart);
        var path = Assert.Single(Directory.GetFiles(CacheRoot())); var before = await File.ReadAllBytesAsync(path);
        forbidden = true;
        var exception = await Assert.ThrowsAsync<GtnhFlowBrowserRequiredException>(() => service.LoadAsync(Chart, refresh: true));
        var cached = Assert.IsType<GtnhFlowChartContent>(exception.CachedContent);
        Assert.True(cached.FromCache); Assert.Null(cached.Warning);
        Assert.Equal(initial.PayloadJson, cached.PayloadJson); Assert.Equal(initial.RevisionId, cached.RevisionId);
        Assert.Equal(initial.SourceAddress, cached.SourceAddress); Assert.Equal(initial.RetrievedAt, cached.RetrievedAt);
        Assert.Equal(3, cached.Document.Views[0].Nodes.Count);
        Assert.Equal(HttpStatusCode.Forbidden, Assert.IsType<HttpRequestException>(exception.InnerException).StatusCode);
        Assert.Equal(before, await File.ReadAllBytesAsync(path)); Assert.Single(Directory.GetFiles(CacheRoot()));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellationTakesPriorityOverForbiddenBrowserHandoff(bool hasCache)
    {
        using var cancellation = new CancellationTokenSource(); var cancel = false;
        using var handler = new Handler(_ =>
        {
            if (!cancel) return Html(Page());
            cancellation.Cancel();
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        });
        using var http = new HttpClient(handler); using var service = new GtnhFlowChartService(_root, http);
        string? path = null; byte[]? before = null;
        if (hasCache)
        {
            await service.LoadAsync(Chart); path = Assert.Single(Directory.GetFiles(CacheRoot()));
            before = await File.ReadAllBytesAsync(path);
        }
        cancel = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LoadAsync(Chart, refresh: true, cancellationToken: cancellation.Token));
        if (hasCache)
        {
            Assert.Equal(before, await File.ReadAllBytesAsync(path!)); Assert.Single(Directory.GetFiles(CacheRoot()));
        }
        else Assert.False(Directory.Exists(_root));
        Assert.Equal(hasCache ? 2 : 1, handler.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task OtherHttpFailuresDoNotRequireBrowser(HttpStatusCode status)
    {
        using var handler = new Handler(_ => new HttpResponseMessage(status));
        using var http = new HttpClient(handler); using var service = new GtnhFlowChartService(_root, http);
        var exception = await Assert.ThrowsAsync<IOException>(() => service.LoadAsync(Chart));
        Assert.Equal(status, Assert.IsType<HttpRequestException>(exception.InnerException).StatusCode);
        Assert.False(Directory.Exists(_root)); Assert.Equal(Chart.Address, Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task InvalidOrMismatchedCacheIsNotTrustedAndNotDeleted()
    {
        using var handler = new Handler(_ => Html(Page())); using var http = new HttpClient(handler);
        using var service = new GtnhFlowChartService(_root, http);
        await service.LoadAsync(Chart); var path = Assert.Single(Directory.GetFiles(CacheRoot()));
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        root["SourceAddress"] = "https://evil.test/";
        var corrupted = root.ToJsonString(); await File.WriteAllTextAsync(path, corrupted);
        using var offlineHandler = new Handler(_ => throw new HttpRequestException("offline")); using var offlineHttp = new HttpClient(offlineHandler);
        using var offline = new GtnhFlowChartService(_root, offlineHttp);
        await Assert.ThrowsAsync<IOException>(() => offline.LoadAsync(Chart));
        Assert.Equal(corrupted, await File.ReadAllTextAsync(path));
        var recovered = await service.LoadAsync(Chart);
        Assert.False(recovered.FromCache); Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task CacheWriteFailureStillReturnsGraphWithWarning()
    {
        Directory.CreateDirectory(_root); await File.WriteAllTextAsync(Path.Combine(_root, "cache"), "user file retained");
        using var handler = new Handler(_ => Html(Page())); using var http = new HttpClient(handler);
        using var service = new GtnhFlowChartService(_root, http);
        var result = await service.LoadAsync(Chart);
        Assert.Equal(3, result.Document.Views[0].Nodes.Count); Assert.Contains("未能保存离线缓存", result.Warning);
        Assert.Equal("user file retained", await File.ReadAllTextAsync(Path.Combine(_root, "cache")));
    }

    [Fact]
    public async Task CancellationDoesNotFallbackToCacheOrReplaceIt()
    {
        using var cancellation = new CancellationTokenSource(); var cancel = false;
        using var handler = new Handler(_ => { if (cancel) cancellation.Cancel(); return Html(Page()); });
        using var http = new HttpClient(handler); using var service = new GtnhFlowChartService(_root, http);
        await service.LoadAsync(Chart); var path = Assert.Single(Directory.GetFiles(CacheRoot())); var before = await File.ReadAllBytesAsync(path);
        cancel = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LoadAsync(Chart, true, cancellation.Token));
        Assert.Equal(before, await File.ReadAllBytesAsync(path)); Assert.Single(Directory.GetFiles(CacheRoot()));
    }

    [Fact]
    public async Task MissingAndInvalidChartHaveNoNetworkOrDiskSideEffects()
    {
        using var handler = new Handler(_ => Html(Page())); using var http = new HttpClient(handler);
        using var service = new GtnhFlowChartService(_root, http);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.LoadAsync(Chart with { IsCreated = false }));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.LoadAsync(Chart with { PageTitle = "其他页面" }));
        Assert.Empty(handler.Requests); Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData("redirect")]
    [InlineData("followed")]
    [InlineData("media")]
    [InlineData("length")]
    public async Task RejectsUnexpectedHttpResponsesWithoutSaving(string kind)
    {
        using var handler = new Handler(_ =>
        {
            var response = Html(Page());
            if (kind == "redirect") { response.StatusCode = HttpStatusCode.Redirect; response.Headers.Location = new("https://evil.test/"); }
            if (kind == "followed") response.RequestMessage = new(HttpMethod.Get, "https://evil.test/");
            if (kind == "media") response.Content.Headers.ContentType = new("application/javascript");
            if (kind == "length") response.Content.Headers.ContentLength = 8 * 1024 * 1024 + 1;
            return response;
        });
        using var http = new HttpClient(handler); using var service = new GtnhFlowChartService(_root, http);
        await Assert.ThrowsAsync<IOException>(() => service.LoadAsync(Chart));
        Assert.False(Directory.Exists(_root)); Assert.Equal(Chart.Address, Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task CancelledBeforeLoadingHasNoSideEffects()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var handler = new Handler(_ => Html(Page())); using var http = new HttpClient(handler);
        using var service = new GtnhFlowChartService(_root, http);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LoadAsync(Chart, cancellationToken: cancellation.Token));
        Assert.Empty(handler.Requests); Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task ConcurrentReadsShareOneValidatedCacheFill()
    {
        using var handler = new Handler(_ => Html(Page())); using var http = new HttpClient(handler);
        using var service = new GtnhFlowChartService(_root, http);
        var results = await Task.WhenAll(service.LoadAsync(Chart), service.LoadAsync(Chart));
        Assert.Single(handler.Requests); Assert.Single(results, item => item.FromCache); Assert.Single(Directory.GetFiles(CacheRoot()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(812334L)]
    public async Task PageDataImportRoundTripsAcrossServiceInstancesWithoutNetwork(long? revisionId)
    {
        using var handler = new Handler(_ => throw new HttpRequestException("no network expected"));
        using var http = new HttpClient(handler);
        var before = DateTimeOffset.UtcNow;
        using (var first = new GtnhFlowChartService(_root, http))
        {
            var imported = await first.ImportPageDataAsync(Chart, Payload, revisionId);
            Assert.False(imported.FromCache); Assert.Null(imported.Warning);
            Assert.Equal(Chart.Address.AbsoluteUri, imported.SourceAddress); Assert.Equal(revisionId, imported.RevisionId);
            Assert.InRange(imported.RetrievedAt, before, DateTimeOffset.UtcNow);
        }
        using var second = new GtnhFlowChartService(_root, http);
        var restored = await second.LoadAsync(Chart);
        Assert.True(restored.FromCache); Assert.Equal(Payload, restored.PayloadJson); Assert.Equal(revisionId, restored.RevisionId);
        Assert.Equal(3, restored.Document.Views[0].Nodes.Count); Assert.Empty(handler.Requests);
        var stored = JsonNode.Parse(await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(CacheRoot()))))!;
        Assert.Equal(GtnhFlowChartContent.LicenseName, stored["License"]!.GetValue<string>());
        Assert.Contains("action=history", restored.HistoryAddress);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("schema")]
    [InlineData("reference")]
    [InlineData("duplicate")]
    public async Task InvalidPageDataCannotReplaceExistingCache(string kind)
    {
        using var handler = new Handler(_ => throw new HttpRequestException("no network expected"));
        using var http = new HttpClient(handler); using var service = new GtnhFlowChartService(_root, http);
        await service.ImportPageDataAsync(Chart, Payload, 12);
        var path = Assert.Single(Directory.GetFiles(CacheRoot())); var before = await File.ReadAllBytesAsync(path);
        var invalid = kind switch
        {
            "empty" => "",
            "schema" => Payload.Replace("\"schemaVersion\":1", "\"schemaVersion\":2"),
            "reference" => Payload.Replace("\"target\":\"r1\"", "\"target\":\"absent\""),
            _ => Payload.Replace("\"id\":\"r1\"", "\"id\":\"i1\"")
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportPageDataAsync(Chart, invalid, 13));
        Assert.Equal(before, await File.ReadAllBytesAsync(path)); Assert.Single(Directory.GetFiles(CacheRoot()));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("untrusted")]
    [InlineData("traversal")]
    [InlineData("query")]
    public async Task PageDataImportRejectsUntrustedOrPendingChartWithoutSideEffects(string kind)
    {
        using var handler = new Handler(_ => throw new HttpRequestException("no network expected"));
        using var http = new HttpClient(handler); using var service = new GtnhFlowChartService(_root, http);
        var chart = kind switch
        {
            "missing" => Chart with { IsCreated = false },
            "untrusted" => Chart with { PageTitle = "https://evil.test/not-a-chart" },
            "traversal" => Chart with { PageTitle = "模板:FlowChart/../其他页面" },
            _ => Chart with { PageTitle = "模板:FlowChart/合成流程?other=1" }
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportPageDataAsync(chart, Payload, 1));
        Assert.Empty(handler.Requests); Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task PageDataImportRejectsInvalidRevisionAndPreservesCache(long revisionId)
    {
        using var handler = new Handler(_ => throw new HttpRequestException("no network expected"));
        using var http = new HttpClient(handler); using var service = new GtnhFlowChartService(_root, http);
        await service.ImportPageDataAsync(Chart, Payload, null);
        var path = Assert.Single(Directory.GetFiles(CacheRoot())); var before = await File.ReadAllBytesAsync(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportPageDataAsync(Chart, Payload, revisionId));
        Assert.Equal(before, await File.ReadAllBytesAsync(path)); Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PageDataImportReturnsGraphWarningWhenCacheCannotBeWritten()
    {
        Directory.CreateDirectory(_root); await File.WriteAllTextAsync(Path.Combine(_root, "cache"), "retained user file");
        using var handler = new Handler(_ => throw new HttpRequestException("no network expected"));
        using var http = new HttpClient(handler); using var service = new GtnhFlowChartService(_root, http);
        var result = await service.ImportPageDataAsync(Chart, Payload, 1);
        Assert.Equal(3, result.Document.Views[0].Nodes.Count); Assert.Contains("未能保存离线缓存", result.Warning);
        Assert.Equal("retained user file", await File.ReadAllTextAsync(Path.Combine(_root, "cache")));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CancelledPageDataImportPreservesCacheWithoutNetwork()
    {
        using var handler = new Handler(_ => throw new HttpRequestException("no network expected"));
        using var http = new HttpClient(handler); using var service = new GtnhFlowChartService(_root, http);
        await service.ImportPageDataAsync(Chart, Payload, 1);
        var path = Assert.Single(Directory.GetFiles(CacheRoot())); var before = await File.ReadAllBytesAsync(path);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ImportPageDataAsync(Chart,
            Payload.Replace("原料", "另一个原料"), 2, cancellation.Token));
        Assert.Equal(before, await File.ReadAllBytesAsync(path)); Assert.Single(Directory.GetFiles(CacheRoot()));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PageDataImportSharesLoadGateAndCancellationDoesNotWritePartialCache()
    {
        using var handler = new WaitingHandler(); using var http = new HttpClient(handler);
        using var service = new GtnhFlowChartService(_root, http);
        await service.ImportPageDataAsync(Chart, Payload, 1);
        var path = Assert.Single(Directory.GetFiles(CacheRoot())); var before = await File.ReadAllBytesAsync(path);
        var loading = service.LoadAsync(Chart, refresh: true);
        try
        {
            await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var cancellation = new CancellationTokenSource();
            var importing = service.ImportPageDataAsync(Chart, Payload.Replace("原料", "另一个原料"), 2, cancellation.Token);
            Assert.False(importing.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => importing);
            Assert.Equal(before, await File.ReadAllBytesAsync(path)); Assert.Single(Directory.GetFiles(CacheRoot()));
        }
        finally { handler.Release.TrySetResult(); await loading; }
        Assert.Equal(Payload, (await service.LoadAsync(Chart)).PayloadJson);
    }

    private string CacheRoot() => Path.Combine(_root, "cache", "gtnh-tools", "flowcharts");
    private static string Page(string? payload = null) => "<html><script>ignored();mw.config.set({\"wgRevisionId\":716003})</script><span class='fcd3-payload' style='display:none'>" + WebUtility.HtmlEncode(payload ?? Payload) + "</span></html>";
    private static HttpResponseMessage Html(string html) => new(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Requests.Add(request.RequestUri!); return Task.FromResult(response(request)); }
    }
    private sealed class WaitingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return Html(Page());
        }
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
