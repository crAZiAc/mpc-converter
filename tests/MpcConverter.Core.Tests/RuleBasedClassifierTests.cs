using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MpcConverter.Core.Analysis;
using MpcConverter.Core.Classification;
using MpcConverter.Core.Model;
using MpcConverter.Core.ProjectIo;
using Xunit;

namespace MpcConverter.Core.Tests;

public class RuleBasedClassifierTests
{
    private static PadInfo Pad(int i, string sample) =>
        new(i, 36 + i, new[] { sample }, new[] { sample + ".wav" }, 1, true);

    [Theory]
    [InlineData("BDRUM12", "Drums")]
    [InlineData("SNARE1", "Drums")]
    [InlineData("HHCLOSE3", "Drums")]
    [InlineData("Break-KitA16 97 SLab", "Drums")]
    [InlineData("AfHouse-Bas-Fundi A EH", "Bass")]
    [InlineData("A72_BassLp2_eLAB_Phonky_080", "Bass")]
    [InlineData("Synth-Deeper1 Em EH", "Synth")]
    [InlineData("Synth-RetroLead C", "Synth")]
    [InlineData("Keys-MKt3 Gm MD", "Keys")]
    [InlineData("Melodic-Orng1 Bb JW", "Melodic")]
    public async Task Classify_KnownNames_GoesToExpectedBucket(string sample, string expected)
    {
        var c = new RuleBasedClassifier();
        var s = await c.SuggestAsync(new[] { Pad(0, sample) });
        Assert.Equal(expected, s[0].TrackName);
    }

    [Fact]
    public async Task Classify_UnknownName_GoesToFallback()
    {
        var c = new RuleBasedClassifier();
        var s = await c.SuggestAsync(new[] { Pad(0, "Sample 016-Normalized-Trimmed") });
        Assert.Equal(RuleBasedClassifier.FallbackBucket, s[0].TrackName);
        Assert.Equal(0.3, s[0].Confidence);
    }

    [Fact]
    public async Task Classify_House_ProducesSeveralBuckets()
    {
        var data = ProjectReader.Open(FixturePaths.HouseFolder).Data;
        var pads = PadAnalyzer.Analyze(data);
        var c = new RuleBasedClassifier();
        var s = await c.SuggestAsync(pads);
        var buckets = s.Select(x => x.TrackName).Distinct().ToList();
        Assert.Contains("Drums", buckets);
        Assert.Contains("Bass", buckets);
        Assert.Contains("Synth", buckets);
        Assert.Equal(pads.Count, s.Count);
    }
}
