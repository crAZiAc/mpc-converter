using MpcConverter.Core.Conversion;
using Xunit;

namespace MpcConverter.Core.Tests;

public class PadColouringTests
{
    [Theory]
    [InlineData("BDRUM12", PadColouring.Red)]
    [InlineData("Kick_01", PadColouring.Red)]
    [InlineData("BoomBap-Kik-KitB1", PadColouring.Red)]
    [InlineData("SNARE1", PadColouring.Green)]
    [InlineData("BoomBap-Snr-KitB1", PadColouring.Green)]
    [InlineData("HHCLOSE3", PadColouring.Yellow)]
    [InlineData("HHOPEN3", PadColouring.Yellow)]
    [InlineData("Acoustic-Hat-HipHop3", PadColouring.Yellow)]
    [InlineData("FloorTom_LowerEastSide", PadColouring.Blue)]
    [InlineData("Tom2_Kit", PadColouring.Blue)]
    [InlineData("CRASH1", PadColouring.Purple)]
    [InlineData("Ride_Kit", PadColouring.Purple)]
    [InlineData("CLAP1", PadColouring.Purple)]
    [InlineData("Some Vocal Chop", PadColouring.Purple)]
    public void ColourForSample_ClassifiesDrumType(string sample, int expected)
    {
        Assert.Equal(expected, PadColouring.ColourForSample(new[] { sample }));
    }

    [Fact]
    public void IsDrumKitTrack_MatchesDrumsCaseInsensitive()
    {
        Assert.True(PadColouring.IsDrumKitTrack("Drums"));
        Assert.True(PadColouring.IsDrumKitTrack("drums"));
        Assert.False(PadColouring.IsDrumKitTrack("Bass"));
    }
}
