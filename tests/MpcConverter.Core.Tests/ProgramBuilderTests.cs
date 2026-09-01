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

        // Pad output routes to Program (0), not directly to Out 1/2 (2).
        Assert.Equal(0, (int)instr0["mixable"]!["audioRoute"]!["destination"]!);

        // Nested schema versions come from the 3.10 template, not the old source
        // (stale versions make MPC reject warp-enabled pads and load blank).
        var tmplInstr = MpcConverter.Core.Templates.TemplateStore.Get("instrument");
        var tmplLayer = MpcConverter.Core.Templates.TemplateStore.Get("layer");
        Assert.Equal((int)tmplInstr["synthSection"]!["version"]!, (int)instr0["synthSection"]!["version"]!);
        Assert.Equal((int)tmplLayer["version"]!, (int)layer0["version"]!);

        // Layer oscillator must be inert (all-zero params). A stray non-zero param in
        // the template made MPC reject drum pads with stacked (multi) sample layers.
        Assert.All(layer0["oscillatorParams"]!.AsArray(), v => Assert.Equal(0.0, (double)v!, 6));
    }

    [Fact]
    public void BuildDrumTrack_DrumsTrack_ColoursPadsByType()
    {
        var (data, prog) = LoadHouse();
        // House pads: 0=BDRUM12(kick) 1=SNARE1(snare) 2=HHCLOSE3(hat) 4=CRASH1(other)
        var dest = new DestTrack("Drums", new[] { 0, 1, 2, 4 });
        var track = ProgramBuilder.BuildDrumTrack(data, prog, dest);
        var program = track["program"]!.AsObject();
        var pads = program["programPads"]!["pads"]!.AsObject();

        Assert.Equal(PadColouring.Red, (int)pads["value0"]!);    // kick
        Assert.Equal(PadColouring.Green, (int)pads["value1"]!);  // snare
        Assert.Equal(PadColouring.Yellow, (int)pads["value2"]!); // hat
        Assert.Equal(PadColouring.Purple, (int)pads["value3"]!); // crash → other

        // Drum kit shows per-pad colours (does NOT follow track colour).
        Assert.False((bool)program["programPads"]!["PadsFollowTrackColour"]!["value0"]!);
        Assert.False((bool)track["padsFollowTrackColour"]!);
    }

    [Fact]
    public void BuildDrumTrack_NonDrumsTrack_PadsFollowTrackColour()
    {
        var (data, prog) = LoadHouse();
        var track = ProgramBuilder.BuildDrumTrack(data, prog, new DestTrack("Bass", new[] { 12 }));
        var program = track["program"]!.AsObject();
        Assert.True((bool)program["programPads"]!["PadsFollowTrackColour"]!["value0"]!);
        Assert.True((bool)track["padsFollowTrackColour"]!);
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
