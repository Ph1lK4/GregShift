using System.Text;
using EML.Core.Minecraft.Gtnh.Tools;

namespace EML.Tests;

public sealed class GtnhPlannerPlanTests
{
    [Fact]
    public void AcceptsVersionOnePlanAndUtf8Bom()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(ValidPlan)).ToArray();

        Assert.Equal("GTNH 2.9.0", GtnhPlannerPlan.Validate(bytes));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"id\":\"p\",\"name\":\"N\",\"recipes\":[],\"nodes\":[],\"edges\":[],\"fuelProfiles\":[]}")]
    [InlineData("{\"schemaVersion\":\"1\",\"id\":\"p\",\"name\":\"N\",\"recipes\":[],\"nodes\":[],\"edges\":[],\"fuelProfiles\":[]}")]
    [InlineData("{\"schemaVersion\":null,\"id\":\"p\",\"name\":\"N\",\"recipes\":[],\"nodes\":[],\"edges\":[],\"fuelProfiles\":[]}")]
    [InlineData("{\"schemaVersion\":1.5,\"id\":\"p\",\"name\":\"N\",\"recipes\":[],\"nodes\":[],\"edges\":[],\"fuelProfiles\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"p\",\"name\":\"N\",\"recipes\":[],\"nodes\":[],\"edges\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"p\",\"name\":\"N\",\"recipes\":[],\"nodes\":[],\"edges\":[],\"fuelProfiles\":[],\"annotations\":[\"invalid\"]}")]
    public void RejectsUnsupportedOrMalformedPlan(string json) =>
        Assert.Throws<InvalidDataException>(() => GtnhPlannerPlan.Validate(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void RejectsEmptyAndOversizedInput()
    {
        Assert.Throws<InvalidDataException>(() => GtnhPlannerPlan.Validate(ReadOnlyMemory<byte>.Empty));
        Assert.Throws<InvalidDataException>(() => GtnhPlannerPlan.Validate(new byte[GtnhPlannerPlan.MaximumBytes + 1]));
    }

    [Fact]
    public void RejectsExcessiveArrayEntries()
    {
        var items = string.Join(',', Enumerable.Repeat("{}", GtnhPlannerPlan.MaximumEntries + 1));
        var json = ValidPlan.Replace("\"recipes\":[]", "\"recipes\":[" + items + "]", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => GtnhPlannerPlan.Validate(Encoding.UTF8.GetBytes(json)));
    }

    private const string ValidPlan = "{\"schemaVersion\":1,\"id\":\"plan-1\",\"name\":\"GTNH 2.9.0\",\"recipes\":[],\"nodes\":[],\"edges\":[],\"fuelProfiles\":[]}";
}
