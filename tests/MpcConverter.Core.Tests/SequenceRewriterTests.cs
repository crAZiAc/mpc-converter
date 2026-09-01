using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using MpcConverter.Core.Analysis;
using MpcConverter.Core.Conversion;
using MpcConverter.Core.Model;
using MpcConverter.Core.ProjectIo;
using Xunit;

namespace MpcConverter.Core.Tests;

public class SequenceRewriterTests
{
    private const string DrumTrack = "Drum 001";

    private static int CountType3(JsonObject data, string? onlyKey = null)
    {
        int total = 0;
        foreach (var seq in data["sequences"]!.AsArray())
        {
            foreach (var (key, clip) in MpcJson.EnumerateClips(seq!["value"]!.AsObject()))
            {
                if (onlyKey is not null && key != onlyKey) continue;
                total += MpcJson.NoteEvents(clip).Count();
            }
        }
        return total;
    }

    private static IEnumerable<int> AllType3Notes(JsonObject data)
    {
        foreach (var seq in data["sequences"]!.AsArray())
            foreach (var (_, clip) in MpcJson.EnumerateClips(seq!["value"]!.AsObject()))
                foreach (var ev in MpcJson.NoteEvents(clip))
                    yield return MpcJson.NoteOf(ev);
    }

    [Fact]
    public void Rewrite_OneTrackPerPad_PreservesAllNoteEvents_AndRenormalizes()
    {
        var data = ProjectReader.Open(FixturePaths.HouseFolder).Data;
        var pads = PadAnalyzer.Analyze(data);
        var map = PadTrackMap.OneTrackPerPad(pads);
        var noteToPad = NoteToPad.From(data);

        int srcTotal = CountType3(data, DrumTrack);

        foreach (var seq in data["sequences"]!.AsArray())
            SequenceRewriter.RewriteSequence(seq!["value"]!.AsObject(), data, DrumTrack, map, noteToPad);

        int dstTotal = CountType3(data);
        Assert.Equal(srcTotal, dstTotal);
        // Every single-pad destination note is 36.
        Assert.All(AllType3Notes(data), n => Assert.Equal(36, n));
    }

    [Fact]
    public void Rewrite_CombineThreePads_NotesAre36_37_38_AndCountsPreserved()
    {
        var data = ProjectReader.Open(FixturePaths.HouseFolder).Data;
        var pads = PadAnalyzer.Analyze(data).Where(p => p.PadIndex is 0 or 1 or 2).ToList();

        // Count source events for notes 36,37,38 only.
        var noteToPad = NoteToPad.From(data);
        int srcSel = 0;
        foreach (var seq in data["sequences"]!.AsArray())
            foreach (var (key, clip) in MpcJson.EnumerateClips(seq!["value"]!.AsObject()))
                if (key == DrumTrack)
                    srcSel += MpcJson.NoteEvents(clip).Count(ev => MpcJson.NoteOf(ev) is 36 or 37 or 38);

        var map = PadTrackMap.AllToOne(pads, "Drums");
        foreach (var seq in data["sequences"]!.AsArray())
            SequenceRewriter.RewriteSequence(seq!["value"]!.AsObject(), data, DrumTrack, map, noteToPad);

        var notes = AllType3Notes(data).ToList();
        Assert.All(notes, n => Assert.Contains(n, new[] { 36, 37, 38 }));
        Assert.Equal(srcSel, notes.Count);
    }
}
