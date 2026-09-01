using System.Linq;
using MpcConverter.Core.Analysis;
using MpcConverter.Core.ProjectIo;
using Xunit;

namespace MpcConverter.Core.Tests;

public class PadAnalyzerTests
{
    [Fact]
    public void Analyze_House_Finds20SampledPads()
    {
        var data = ProjectReader.Open(FixturePaths.HouseFolder).Data;
        var pads = PadAnalyzer.Analyze(data);
        Assert.Equal(20, pads.Count);

        var p0 = pads.First(p => p.PadIndex == 0);
        Assert.Equal(36, p0.SourceNote);
        Assert.Contains("BDRUM12", p0.SampleNames);
        Assert.True(p0.EventCount > 0);
    }

    [Fact]
    public void Analyze_House_PadNotesAscendFrom36()
    {
        var data = ProjectReader.Open(FixturePaths.HouseFolder).Data;
        var pads = PadAnalyzer.Analyze(data);
        // Pad index i maps to note 36+i in the source padNoteMap.
        Assert.All(pads, p => Assert.Equal(36 + p.PadIndex, p.SourceNote));
    }
}
