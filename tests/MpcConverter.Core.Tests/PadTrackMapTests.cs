using System.Collections.Generic;
using System.Linq;
using MpcConverter.Core.Model;
using Xunit;

namespace MpcConverter.Core.Tests;

public class PadTrackMapTests
{
    private static PadInfo Pad(int i, string sample) =>
        new(i, 36 + i, new[] { sample }, new[] { sample + ".wav" }, 1, true);

    private static List<PadInfo> ThreePads() => new()
    {
        Pad(0, "Kick"), Pad(1, "Snare"), Pad(2, "Hat"),
    };

    [Fact]
    public void OneTrackPerPad_GivesOneSlotEach_NoteAll36()
    {
        var map = PadTrackMap.OneTrackPerPad(ThreePads());
        Assert.Equal(3, map.Tracks.Count);
        Assert.All(map.Tracks, t => Assert.Single(t.PadIndices));
        Assert.All(new[] { 0, 1, 2 }, i => Assert.Equal(36, map.NoteOf(i)));
    }

    [Fact]
    public void AllToOne_GivesAscendingSlots()
    {
        var map = PadTrackMap.AllToOne(ThreePads(), "Drums");
        Assert.Single(map.Tracks);
        Assert.Equal(new[] { 0, 1, 2 }, map.Tracks[0].PadIndices);
        Assert.Equal(36, map.NoteOf(0));
        Assert.Equal(37, map.NoteOf(1));
        Assert.Equal(38, map.NoteOf(2));
    }

    [Fact]
    public void FromAssignments_CombinesAndSkips()
    {
        var pads = ThreePads();
        var map = PadTrackMap.FromAssignments(pads, new Dictionary<int, string?>
        {
            [0] = "Drums", [1] = "Drums", [2] = null, // pad 2 skipped
        });
        Assert.Single(map.Tracks);
        Assert.Equal(new[] { 0, 1 }, map.Tracks[0].PadIndices);
        Assert.Equal(36, map.NoteOf(0));
        Assert.Equal(37, map.NoteOf(1));
        Assert.Equal(-1, map.SlotOf(2)); // skipped
    }

    [Fact]
    public void Validate_ThrowsOnEmpty()
    {
        var map = PadTrackMap.FromAssignments(ThreePads(),
            new Dictionary<int, string?> { [0] = null, [1] = null, [2] = null });
        Assert.Throws<System.InvalidOperationException>(map.Validate);
    }

    [Fact]
    public void OneTrackPerPad_DuplicateSampleNames_AreUniquified()
    {
        var pads = new List<PadInfo> { Pad(0, "Kick"), Pad(1, "Kick") };
        var map = PadTrackMap.OneTrackPerPad(pads);
        Assert.Equal(2, map.Tracks.Select(t => t.Name).Distinct().Count());
    }

    [Fact]
    public void FromAssignments_ReservedSampleTrackName_IsNormalized()
    {
        var pads = ThreePads();
        var map = PadTrackMap.FromAssignments(pads, new Dictionary<int, string?>
        {
            [0] = "Sample", [1] = "Drums", [2] = "sample",
        });

        Assert.Contains(map.Tracks, t => t.Name == "Melodic");
        Assert.DoesNotContain(map.Tracks, t => t.Name == "Sample");
        Assert.Equal("Melodic", map.TrackOf(0));
        Assert.Equal("Melodic", map.TrackOf(2));
    }
}
