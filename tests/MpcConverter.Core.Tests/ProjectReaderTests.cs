using System.IO;
using MpcConverter.Core.ProjectIo;
using Xunit;

namespace MpcConverter.Core.Tests;

public class ProjectReaderTests
{
    [Fact]
    public void Open_House_ResolvesInnerFileAndSamples()
    {
        var p = ProjectReader.Open(FixturePaths.HouseFolder);
        Assert.Equal("House os 01", p.Name);
        Assert.Equal("1.3.0.12", p.Document.FormatVersion);
        Assert.True(Directory.Exists(p.ProjectDataDir));
        Assert.Equal(20, p.Data["samples"]!.AsArray().Count);
    }

    [Fact]
    public void Open_Keys_ResolvesTargetFormat()
    {
        var p = ProjectReader.Open(FixturePaths.KeysFolder);
        Assert.Equal("Keys v1", p.Name);
        Assert.Equal("3.10.0.23", p.Document.FormatVersion);
        Assert.Equal(30, (int)p.Data["version"]!);
    }

    [Fact]
    public void Open_InnerFileDirectly_Works()
    {
        var p = ProjectReader.Open(FixturePaths.HouseInnerFile);
        Assert.Equal("House os 01", p.Name);
    }

    [Fact]
    public void Open_XpjFileDirectly_Works()
    {
        var p = ProjectReader.Open(FixturePaths.HouseXpj);
        Assert.Equal("House os 01", p.Name);
        Assert.Equal("1.3.0.12", p.Document.FormatVersion);
        Assert.Equal(20, p.Data["samples"]!.AsArray().Count);
    }

    [Fact]
    public void Open_FolderWithOnlyXpjAndSamples_Works()
    {
        // Reproduces the layout where a folder holds only the gzipped .xpj plus
        // the "<Name>_[ProjectData]" sample folder (no uncompressed inner file).
        var dir = TestUtil.TempDir();
        var projDir = System.IO.Path.Combine(dir, "house");
        System.IO.Directory.CreateDirectory(projDir);
        System.IO.File.Copy(FixturePaths.HouseXpj,
            System.IO.Path.Combine(projDir, "House os 01.xpj"));
        var pdSrc = FixturePaths.HouseProjectData;
        var pdDst = System.IO.Path.Combine(projDir, "House os 01_[ProjectData]");
        System.IO.Directory.CreateDirectory(pdDst);
        foreach (var f in System.IO.Directory.GetFiles(pdSrc))
            System.IO.File.Copy(f, System.IO.Path.Combine(pdDst, System.IO.Path.GetFileName(f)));

        var p = ProjectReader.Open(projDir);
        Assert.Equal("House os 01", p.Name);
        Assert.True(System.IO.Directory.Exists(p.ProjectDataDir));
    }
}
