using System.Linq;
using System.Text.Json.Nodes;
using MpcConverter.Core.Analysis;
using MpcConverter.Core.Conversion;
using MpcConverter.Core.Model;
using MpcConverter.Core.ProjectIo;
using Xunit;

namespace MpcConverter.Core.Tests;

public class ProgramBuilderTests
{
    private static (JsonObject data, JsonObject drumProgram) LoadHouse()
    {
        var data = ProjectReader.Open(FixturePaths.HouseFolder).Data;
        var drumProgram = MpcJson.FindDrumTrack(data)!["program"]!.AsObject();
        return (data, drumProgram);
    }

    [Fact]
    public void BuildDrumTrack_SinglePad_RenormalizesToNote36()
    {
        var (data, prog) = LoadHouse();
        var dest = new DestTrack("Kick", new[] { 0 }); // pad 0 = BDRUM12

        var track = ProgramBuilder.BuildDrumTrack(data, prog, dest);

        Assert.Equal("Kick", (string?)track["name"]);
        var program = track["program"]!.AsObject();
        Assert.Equal(0, (int)program["type"]!);

        // Instrument at slot 0 is v29 and references BDRUM12.
        var instr0 = program["drum"]!["instruments"]![0]!.AsObject();
        Assert.Equal(29, (int)instr0["version"]!);
        var layer0 = instr0["layersv"]![0]!.AsObject();
        Assert.Equal("BDRUM12", (string?)layer0["sampleName"]);
        Assert.True(layer0.AsObject().ContainsKey("OscillatorType")); // 3.10 field present

        // padNoteMap slot 0 → note 36.
        Assert.Equal(36, (int)program["padNoteMap"]!["noteForPad"]!["value0"]!);

        // program.samples contains the kick sample.
        Assert.Contains(program["samples"]!.AsArray(),
            s => (string?)s!["name"] == "BDRUM12");
    }

    [Fact]
    public void BuildDrumTrack_CombineThreePads_SlotsAscend()
    {
        var (data, prog) = LoadHouse();
        var dest = new DestTrack("Drums", new[] { 0, 1, 2 });

        var track = ProgramBuilder.BuildDrumTrack(data, prog, dest);
        var program = track["program"]!.AsObject();
        var noteForPad = program["padNoteMap"]!["noteForPad"]!.AsObject();

        Assert.Equal(36, (int)noteForPad["value0"]!);
        Assert.Equal(37, (int)noteForPad["value1"]!);
        Assert.Equal(38, (int)noteForPad["value2"]!);

        // Three distinct samples collected.
        Assert.Equal(3, program["samples"]!.AsArray().Count);
    }
}
