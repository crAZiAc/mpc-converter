using System.Text.Json.Nodes;
using MpcConverter.Core.Conversion;
using Xunit;

namespace MpcConverter.Core.Tests;

public class JsonMergeTests
{
    [Fact]
    public void Upgrade_KeepsTemplateOnlyKeys_AndTakesSharedFromSource()
    {
        var tmpl = (JsonObject)JsonNode.Parse("""{"version":29,"a":1,"newField":true}""")!;
        var src = (JsonObject)JsonNode.Parse("""{"version":28,"a":42,"extra":7}""")!;
        var r = JsonMerge.UpgradeOnto(tmpl, src);
        Assert.Equal(42, (int)r["a"]!);       // shared → source
        Assert.Equal(28, (int)r["version"]!); // shared → source (caller bumps after)
        Assert.True((bool)r["newField"]!);    // template-only kept
        Assert.False(r.ContainsKey("extra")); // source-only dropped
    }

    [Fact]
    public void Upgrade_RecursesIntoObjects()
    {
        var tmpl = (JsonObject)JsonNode.Parse("""{"o":{"x":0,"newX":9}}""")!;
        var src = (JsonObject)JsonNode.Parse("""{"o":{"x":5,"gone":1}}""")!;
        var r = JsonMerge.UpgradeOnto(tmpl, src);
        Assert.Equal(5, (int)r["o"]!["x"]!);
        Assert.Equal(9, (int)r["o"]!["newX"]!);
        Assert.False(r["o"]!.AsObject().ContainsKey("gone"));
    }

    [Fact]
    public void Upgrade_DoesNotMutateInputs()
    {
        var tmpl = (JsonObject)JsonNode.Parse("""{"a":1}""")!;
        var src = (JsonObject)JsonNode.Parse("""{"a":2}""")!;
        JsonMerge.UpgradeOnto(tmpl, src);
        Assert.Equal(1, (int)tmpl["a"]!); // template untouched
    }

    [Fact]
    public void Upgrade_SharedArray_TakenFromSource()
    {
        var tmpl = (JsonObject)JsonNode.Parse("""{"arr":[1,2,3]}""")!;
        var src = (JsonObject)JsonNode.Parse("""{"arr":[9]}""")!;
        var r = JsonMerge.UpgradeOnto(tmpl, src);
        Assert.Single(r["arr"]!.AsArray());
        Assert.Equal(9, (int)r["arr"]![0]!);
    }
}
