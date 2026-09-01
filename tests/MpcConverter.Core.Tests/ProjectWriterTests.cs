using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using MpcConverter.Core.ProjectIo;
using Xunit;

namespace MpcConverter.Core.Tests;

public class ProjectWriterTests
{
    [Fact]
    public void Write_ThenReopen_PreservesData()
    {
        var src = ProjectReader.Open(FixturePaths.HouseFolder);
        var tmp = TestUtil.TempDir();
        var samples = src.Data["samples"]!.AsArray().Select(s => (string)s!["path"]!);

        var folder = ProjectWriter.Write(src, tmp, "House copy", samples, overwrite: true);

        // Packaged layout: folder contains <name>.xpj + <name>_[ProjectData], no inner file.
        Assert.True(File.Exists(Path.Combine(folder, "House copy.xpj")));
        Assert.False(File.Exists(Path.Combine(folder, "House copy")));

        var back = ProjectReader.Open(folder);
        Assert.Equal(20, back.Data["samples"]!.AsArray().Count);
        Assert.True(Directory.Exists(back.ProjectDataDir));
        Assert.Equal(20, Directory.GetFiles(back.ProjectDataDir!).Length);
    }

    [Fact]
    public void Write_Unpacked_AlsoWritesInnerFile()
    {
        var src = ProjectReader.Open(FixturePaths.HouseFolder);
        var tmp = TestUtil.TempDir();
        var samples = src.Data["samples"]!.AsArray().Select(s => (string)s!["path"]!);

        var folder = ProjectWriter.Write(src, tmp, "House copy", samples,
            overwrite: true, warnings: null, packageAsXpj: false);

        Assert.True(File.Exists(Path.Combine(folder, "House copy")));       // inner
        Assert.True(File.Exists(Path.Combine(folder, "House copy.xpj")));   // xpj
    }

    [Fact]
    public void Write_MissingSample_IsWarnedNotThrown()
    {
        var src = ProjectReader.Open(FixturePaths.HouseFolder);
        var tmp = TestUtil.TempDir();
        var warnings = new System.Collections.Generic.List<string>();

        ProjectWriter.Write(src, tmp, "House copy",
            new[] { "does-not-exist.wav" }, overwrite: true, warnings);

        Assert.Contains(warnings, w => w.Contains("does-not-exist.wav"));
    }

    [Fact]
    public void Write_FlatOutput_WritesXpjAndProjectDataIntoOutputFolder()
    {
        var src = ProjectReader.Open(FixturePaths.HouseFolder);
        var tmp = TestUtil.TempDir();
        var samples = src.Data["samples"]!.AsArray().Select(s => (string)s!["path"]!);

        var written = ProjectWriter.Write(src, tmp, "House copy", samples,
            overwrite: true, warnings: null, packageAsXpj: true, flatOutput: true);

        Assert.Equal(Path.Combine(tmp, "House copy.xpj"), written);
        Assert.True(File.Exists(Path.Combine(tmp, "House copy.xpj")));
        Assert.True(Directory.Exists(Path.Combine(tmp, "House copy_[ProjectData]")));
        Assert.False(Directory.Exists(Path.Combine(tmp, "House copy")));
    }
}
