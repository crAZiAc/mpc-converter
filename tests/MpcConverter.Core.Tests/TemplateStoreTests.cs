using System.Text.Json.Nodes;
using MpcConverter.Core.Templates;
using Xunit;

namespace MpcConverter.Core.Tests;

public class TemplateStoreTests
{
    [Fact]
    public void Get_Instrument_IsVersion29WithNewFields()
    {
        var ins = TemplateStore.Get("instrument");
        Assert.Equal(29, (int)ins["version"]!);
        Assert.True(ins.ContainsKey("emulationProfile"));
        Assert.True(ins.ContainsKey("velocityScale"));
    }

    [Fact]
    public void Get_Layer_HasNewOscillatorFields()
    {
        var layer = TemplateStore.Get("layer");
        Assert.True(layer.ContainsKey("OscillatorType"));
        Assert.True(layer.ContainsKey("quadrantEnabled"));
    }

    [Fact]
    public void Get_DrumProgram_IsEmptyProgramType0()
    {
        var dp = TemplateStore.Get("drumProgram");
        Assert.Equal(0, (int)dp["type"]!);
        Assert.Equal(128, dp["drum"]!["instruments"]!.AsArray().Count);
        // All instruments are blank (no sampled layers).
        Assert.Empty(dp["samples"]!.AsArray());
    }

    [Fact]
    public void Get_Document_HasTopLevelMementosButNoContent()
    {
        var doc = TemplateStore.Get("document");
        Assert.Equal(30, (int)doc["version"]!);
        Assert.Empty(doc["tracks"]!.AsArray());
        Assert.Empty(doc["sequences"]!.AsArray());
        Assert.True(doc.ContainsKey("guiNormalTracksMemento"));
    }

    [Fact]
    public void Get_ReturnsFreshClones()
    {
        var a = TemplateStore.Get("clip");
        a["name"] = "mutated";
        var b = TemplateStore.Get("clip");
        Assert.NotEqual("mutated", (string?)b["name"]);
    }
}
