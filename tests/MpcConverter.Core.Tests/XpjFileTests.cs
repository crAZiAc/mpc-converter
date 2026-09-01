using System.IO;
using MpcConverter.Core.Acvs;
using Xunit;

namespace MpcConverter.Core.Tests;

public class XpjFileTests
{
    [Fact]
    public void Decompress_HouseXpj_EqualsInnerFile()
    {
        var gz = File.ReadAllBytes(FixturePaths.HouseXpj);
        var raw = XpjFile.Decompress(gz);
        Assert.Equal(File.ReadAllBytes(FixturePaths.HouseInnerFile), raw);
    }

    [Fact]
    public void CompressThenDecompress_RoundTrips()
    {
        var original = File.ReadAllBytes(FixturePaths.HouseInnerFile);
        var back = XpjFile.Decompress(XpjFile.Compress(original));
        Assert.Equal(original, back);
    }
}
