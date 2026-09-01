using System.IO;
using System.Text.Json.Nodes;
using MpcConverter.Core.Acvs;
using Xunit;

namespace MpcConverter.Core.Tests;

public class AcvsDocumentTests
{
    [Fact]
    public void Parse_House_ReadsHeaderAndData()
    {
        var bytes = File.ReadAllBytes(FixturePaths.HouseInnerFile);
        var doc = AcvsDocument.Parse(bytes);
        Assert.Equal("1.3.0.12", doc.FormatVersion);
        Assert.Equal("SerialisableProjectData", doc.Payload);
        Assert.Equal("json", doc.Encoding);
        Assert.Equal("Linux", doc.Platform);
        Assert.Equal(28, (int)doc.Root["data"]!["version"]!);
        Assert.Equal("C Major", (string)doc.Root["data"]!["key"]!);
    }

    [Fact]
    public void Parse_NonAcvs_Throws()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("not an acvs file\n{}");
        Assert.Throws<InvalidDataException>(() => AcvsDocument.Parse(bytes));
    }

    [Fact]
    public void RoundTrip_House_IsSemanticallyStable()
    {
        var bytes = File.ReadAllBytes(FixturePaths.HouseInnerFile);
        var doc = AcvsDocument.Parse(bytes);
        var outBytes = doc.ToBytes();
        // Re-parse and compare structurally (byte-stability is attempted but not required).
        var reparsed = AcvsDocument.Parse(outBytes);
        Assert.Equal(doc.FormatVersion, reparsed.FormatVersion);
        Assert.Equal(
            doc.Root.ToJsonString(),
            reparsed.Root.ToJsonString());
    }

    [Fact]
    public void RoundTrip_House_IsByteStable()
    {
        // The writer aims to reproduce MPC's exact formatting (newline-delimited,
        // zero-indent). If this ever drifts (e.g. float rendering), the semantic
        // round-trip test above is the real acceptance bar.
        var bytes = File.ReadAllBytes(FixturePaths.HouseInnerFile);
        var outBytes = AcvsDocument.Parse(bytes).ToBytes();
        Assert.Equal(bytes, outBytes);
    }

    [Fact]
    public void WithFormatVersion_ChangesHeaderOnly()
    {
        var doc = AcvsDocument.Parse(File.ReadAllBytes(FixturePaths.HouseInnerFile));
        doc.FormatVersion = "3.10.0.23";
        var reparsed = AcvsDocument.Parse(doc.ToBytes());
        Assert.Equal("3.10.0.23", reparsed.FormatVersion);
        Assert.Equal(28, (int)reparsed.Root["data"]!["version"]!);
    }
}
