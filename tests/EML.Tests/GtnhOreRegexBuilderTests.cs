using System.Text.RegularExpressions;
using EML.Core.Minecraft.Gtnh.Tools;

namespace EML.Tests;

public sealed class GtnhOreRegexBuilderTests
{
    [Fact]
    public void BuildsExactDeterministicMatchingWithoutCrossProducts()
    {
        string[] tokens = ["crushedIron", "crushedGold", "dustImpureIron", "dustIron", "dustIronOxide"];
        var expression = GtnhOreRegexBuilder.Build(tokens);
        var regex = new Regex(expression);

        Assert.All(tokens, value => Assert.Matches(regex, value));
        Assert.DoesNotMatch(regex, "dustImpureGold");
        Assert.DoesNotMatch(regex, "crushedIronOxide");
        Assert.DoesNotMatch(regex, "dustIronOxideExtra");
        Assert.DoesNotMatch(regex, "dust");
        Assert.DoesNotMatch(regex, "prefixdustIron");
        Assert.DoesNotMatch(regex, "dustIron\n");
        Assert.Equal(expression, GtnhOreRegexBuilder.Build(tokens.Reverse().Concat(tokens)));
    }

    [Fact]
    public void EmptySetNeverBecomesMatchEverything()
    {
        var regex = new Regex(GtnhOreRegexBuilder.Build([]));
        Assert.DoesNotMatch(regex, "");
        Assert.DoesNotMatch(regex, "oreIron");
    }

    [Theory]
    [InlineData("oreIron|.*")]
    [InlineData("dust(Iron)")]
    [InlineData("")]
    [InlineData("ore铁")]
    [InlineData("oreIron\n")]
    public void RejectsUntrustedRegexSyntax(string token) =>
        Assert.Throws<InvalidDataException>(() => GtnhOreRegexBuilder.Build([token]));

    [Fact]
    public void ReportsLengthAndKeepsExpressionWhenOverLimit()
    {
        var result = new GtnhOreRegexResult("macerator", "研磨机", "^(?:oreIron)$", 5, 1);
        Assert.True(result.IsOverLimit);
        Assert.Equal(13, result.Length);
        Assert.Equal("^(?:oreIron)$", result.Expression);
    }
}
