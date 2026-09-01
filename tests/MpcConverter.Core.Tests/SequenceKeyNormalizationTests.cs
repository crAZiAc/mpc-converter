using System.Linq;
using System.Text.Json.Nodes;
using MpcConverter.Core.Conversion;
using Xunit;

namespace MpcConverter.Core.Tests;

public class SequenceKeyNormalizationTests
{
    private static JsonObject Data(int[] keys, int currentSequence)
    {
        var seqs = new JsonArray();
        foreach (var k in keys)
            seqs.Add(new JsonObject { ["key"] = k, ["value"] = new JsonObject { ["name"] = $"S{k}" } });
        return new JsonObject { ["sequences"] = seqs, ["currentSequence"] = currentSequence };
    }

    [Fact]
    public void Normalize_GappySlots_BecomeContiguous()
    {
        var data = Data(new[] { 0, 1, 4, 5 }, currentSequence: 4);
        SequenceRewriter.NormalizeSequenceKeys(data);

        var keys = data["sequences"]!.AsArray().Select(s => (int)s!["key"]!).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { 0, 1, 2, 3 }, keys);
        // slot 4 (3rd by ascending order) → new key 2
        Assert.Equal(2, (int)data["currentSequence"]!);
    }

    [Fact]
    public void Normalize_PreservesEachSequencesValueByAscendingOrder()
    {
        var data = Data(new[] { 5, 0, 4, 1 }, currentSequence: 5);
        SequenceRewriter.NormalizeSequenceKeys(data);
        // Build oldName -> newKey
        var map = data["sequences"]!.AsArray()
            .ToDictionary(s => (string)s!["value"]!["name"]!, s => (int)s!["key"]!);
        Assert.Equal(0, map["S0"]);
        Assert.Equal(1, map["S1"]);
        Assert.Equal(2, map["S4"]);
        Assert.Equal(3, map["S5"]);
        Assert.Equal(3, (int)data["currentSequence"]!); // was slot 5 → new key 3
    }

    [Fact]
    public void Normalize_AlreadyContiguous_Unchanged()
    {
        var data = Data(new[] { 0, 1, 2 }, currentSequence: 1);
        SequenceRewriter.NormalizeSequenceKeys(data);
        var keys = data["sequences"]!.AsArray().Select(s => (int)s!["key"]!).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { 0, 1, 2 }, keys);
        Assert.Equal(1, (int)data["currentSequence"]!);
    }
}
