using EML.App.Controls;

namespace EML.App.Tests;

/// <summary>手册精简仅适用于研究页，不扩大内嵌浏览器权限或替换原站交互组件。</summary>
public sealed class GtnhHandbookFocusTests
{
    [Theory]
    [InlineData("https://gtnh.huijiwiki.com/wiki/研究", true)]
    [InlineData("https://gtnh.huijiwiki.com/wiki/研究#研究一览", true)]
    [InlineData("https://gtnh.huijiwiki.com/wiki/研究/", true)]
    [InlineData("https://GTNH.HUIJIWIKI.COM/wiki/研究", true)]
    [InlineData("https://gtnh.huijiwiki.com:443/wiki/研究", true)]
    [InlineData("https://gtnh.huijiwiki.com/wiki/%E7%A0%94%E7%A9%B6#%E7%A0%94%E7%A9%B6%E4%B8%80%E8%A7%88", true)]
    [InlineData("https://gtnh.huijiwiki.com/wiki/研究助手", false)]
    [InlineData("https://gtnh.huijiwiki.com/wiki/神秘时代研究助手", false)]
    [InlineData("https://gtnh.huijiwiki.com/wiki/研究/其他", false)]
    [InlineData("https://gtnh.huijiwiki.com/wiki/模板:FlowChart", false)]
    [InlineData("https://gtnh.huijiwiki.com/wiki/模板:FlowChart/轻燃油", false)]
    [InlineData("https://gtnh.huijiwiki.com/wiki/%25E7%25A0%2594%25E7%25A9%25B6", false)]
    [InlineData("https://chunkbasenh.beyomoes.com/", false)]
    [InlineData("https://gtnhquestsbook.top/", false)]
    [InlineData("https://gtnhplanner.com/", false)]
    [InlineData("http://gtnh.huijiwiki.com/wiki/研究", false)]
    [InlineData("https://gtnh.huijiwiki.com:444/wiki/研究", false)]
    [InlineData("https://player@gtnh.huijiwiki.com/wiki/研究", false)]
    [InlineData("https://player:password@gtnh.huijiwiki.com/wiki/研究", false)]
    [InlineData("https://gtnh.huijiwiki.com.evil.example/wiki/研究", false)]
    [InlineData("https://evil.example/gtnh.huijiwiki.com/wiki/研究", false)]
    [InlineData("file:///C:/wiki/研究", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,研究", false)]
    [InlineData("/wiki/研究", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void FocusIsLimitedToTheExactTrustedResearchPage(string? address, bool expected) =>
        Assert.Equal(expected, GtnhHandbookFocus.IsPage(address));

    [Theory]
    [InlineData("fetch(")]
    [InlineData("XMLHttpRequest")]
    [InlineData("indexedDB")]
    [InlineData("localStorage")]
    [InlineData("sessionStorage")]
    [InlineData("document.cookie")]
    [InlineData("chrome.webview")]
    [InlineData("hostObjects")]
    [InlineData("cloneNode")]
    [InlineData("innerHTML")]
    [InlineData("outerHTML")]
    public void FocusDoesNotAddDataAccessOrReplaceInteractiveNodes(string forbidden) =>
        Assert.DoesNotContain(forbidden, GtnhHandbookFocus.Script);

    [Fact]
    public void FocusRetainsTheHandbookAndItsExternalDetailLayer()
    {
        Assert.Contains(".gtnh-thaumonomicon-d3", GtnhHandbookFocus.Script);
        Assert.Contains(".qbd3-tooltip", GtnhHandbookFocus.Script);
    }

    [Fact]
    public void DelayedComponentsUseBoundedObservation()
    {
        Assert.Contains("MutationObserver", GtnhHandbookFocus.Script);
        Assert.Contains("disconnect()", GtnhHandbookFocus.Script);
        Assert.Contains("setTimeout", GtnhHandbookFocus.Script);
        Assert.Contains("clearTimeout", GtnhHandbookFocus.Script);
    }
}
