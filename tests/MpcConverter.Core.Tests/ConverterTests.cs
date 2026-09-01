using System.Linq;
using System.Text.Json.Nodes;
using MpcConverter.Core.Analysis;
using MpcConverter.Core.Conversion;
using MpcConverter.Core.Model;
using MpcConverter.Core.ProjectIo;
using Xunit;

namespace MpcConverter.Core.Tests;

public class ConverterTests
{
    private static int CountSourceType3(JsonObject data, string drumTrack)
    {
        int total = 0;
        foreach (var seq in data["sequences"]!.AsArray())
            foreach (var (key, clip) in MpcJson.EnumerateClips(seq!["value"]!.AsObject()))
                if (key == drumTrack)
                    total += MpcJson.NoteEvents(clip).Count();
        return total;
    }

    [Fact]
    public void Convert_OneTrackPerPad_EndToEnd_WritesValid310Project()
    {
        var source = ProjectReader.Open(FixturePaths.HouseFolder);
        int srcEvents = CountSourceType3(source.Data, "Drum 001");
        var pads = PadAnalyzer.Analyze(source.Data);
        var map = PadTrackMap.OneTrackPerPad(pads);

        var (project, report) = Converter.Convert(source, map);

        Assert.Equal("3.10.0.23", project.Document.FormatVersion);
        Assert.Equal(30, (int)project.Data["version"]!);
        // Must present as a native 3.9 project, not the legacy "AC50"/"Sequence" mode,
        // or MPC shows only one track and one sequence.
        Assert.Equal("ACVS", (string?)project.Data["originalCreatorProductIdentifier"]);
        Assert.Equal("ACVS", (string?)project.Data["lastSavedProductIdentifier"]);
        Assert.Equal("Main Mode", (string?)project.Data["engineMode"]);
        Assert.Equal(20, report.TracksCreated);
        Assert.Equal(20, report.PadsPlaced);
        Assert.Equal(srcEvents, report.EventsMoved);

        // Tracks = 20 drum + 28 mixer.
        Assert.Equal(48, project.Data["tracks"]!.AsArray().Count);

        // Native mixer config (drives the output/submix level meters).
        var mixer = project.Data["mixer"]!.AsObject();
        Assert.Equal(16, mixer["outputs"]!.AsArray().Count);
        Assert.Equal(8, mixer["submixes"]!.AsArray().Count);
        Assert.Equal(4, mixer["sends"]!.AsArray().Count);

        // Drum tracks are coloured from MPC's 16-colour palette, cycling (reused)
        // beyond 16 tracks; the first 16 are distinct.
        var palette = Converter.Palette.ToHashSet();
        var drumColours = project.Data["tracks"]!.AsArray()
            .Where(t => (int?)t!["program"]?["type"] == 0)
            .Select(t => (int)t!["colour"]!)
            .ToList();
        Assert.All(drumColours, c => Assert.Contains(c, palette));
        Assert.Equal(System.Math.Min(drumColours.Count, 16), drumColours.Take(16).Distinct().Count());

        // Every sequence must list EVERY track (empty clips for non-playing tracks),
        // like a native MPC project — otherwise MPC shows only the tracks that play.
        var trackNames = project.Data["tracks"]!.AsArray()
            .Select(t => (string?)t!["name"]).Where(n => n is not null).ToHashSet();
        foreach (var seq in project.Data["sequences"]!.AsArray())
        {
            var clipKeys = MpcJson.EnumerateClips(seq!["value"]!.AsObject())
                .Select(c => c.Key).ToHashSet();
            Assert.Equal(trackNames, clipKeys);
        }

        // Write and self-check.
        var tmp = TestUtil.TempDir();
        var folder = ProjectWriter.Write(project, tmp, "House 3.9",
            Converter.ReferencedSampleFiles(project), overwrite: true);
        Converter.SelfCheck(folder, map, srcEvents);
    }

    [Fact]
    public void Convert_SourceIsUnmodified()
    {
        var source = ProjectReader.Open(FixturePaths.HouseFolder);
        var before = source.Document.FormatVersion;
        int tracksBefore = source.Data["tracks"]!.AsArray().Count;

        var pads = PadAnalyzer.Analyze(source.Data);
        Converter.Convert(source, PadTrackMap.OneTrackPerPad(pads));

        Assert.Equal(before, source.Document.FormatVersion); // still 1.3.0.12
        Assert.Equal(tracksBefore, source.Data["tracks"]!.AsArray().Count);
    }

    [Fact]
    public void Convert_GoldenCompare_DrumTrackMatchesKeysStructure()
    {
        var source = ProjectReader.Open(FixturePaths.HouseFolder);
        var pads = PadAnalyzer.Analyze(source.Data);
        var (project, _) = Converter.Convert(source, PadTrackMap.OneTrackPerPad(pads));

        var keys = ProjectReader.Open(FixturePaths.KeysFolder);
        var keysDrum = MpcJson.FindDrumTrack(keys.Data)!;
        var convDrum = MpcJson.FindDrumTrack(project.Data)!;

        // Same program type.
        Assert.Equal((int)keysDrum["program"]!["type"]!, (int)convDrum["program"]!["type"]!);

        // Instrument versions match (29) and 3.10-only fields exist.
        var keysInstr = keysDrum["program"]!["drum"]!["instruments"]![0]!.AsObject();
        var convInstr = convDrum["program"]!["drum"]!["instruments"]![0]!.AsObject();
        Assert.Equal((int)keysInstr["version"]!, (int)convInstr["version"]!);
        foreach (var key in keysInstr)
            Assert.True(convInstr.ContainsKey(key.Key),
                $"Converted instrument missing 3.10 key '{key.Key}'.");

        // Layer 3.10-only fields exist.
        var keysLayer = keysInstr["layersv"]![0]!.AsObject();
        var convLayer = convInstr["layersv"]![0]!.AsObject();
        foreach (var key in keysLayer)
            Assert.True(convLayer.ContainsKey(key.Key),
                $"Converted layer missing 3.10 key '{key.Key}'.");
    }
}
