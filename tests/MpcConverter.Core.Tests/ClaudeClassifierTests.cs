using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MpcConverter.Core.Classification;
using MpcConverter.Core.Model;
using Xunit;

namespace MpcConverter.Core.Tests;

public class ClaudeClassifierTests
{
    private static PadInfo Pad(int i, string sample) =>
        new(i, 36 + i, new[] { sample }, new[] { sample + ".wav" }, 1, true);

    private sealed class FakeInvoker : IAnthropicInvoker
    {
        private readonly string? _json;
        private readonly Exception? _throw;
        public FakeInvoker(string json) => _json = json;
        public FakeInvoker(Exception ex) => _throw = ex;

        public Task<string> GetGroupingJsonAsync(string model, string prompt, CancellationToken ct)
            => _throw is not null ? Task.FromException<string>(_throw) : Task.FromResult(_json!);
    }

    [Fact]
    public async Task Suggest_ParsesJsonArray()
    {
        var json = """[{"padIndex":0,"trackName":"Drums","confidence":0.95,"reason":"kick"}]""";
        var c = new ClaudeClassifier("key", "claude-opus-5", new FakeInvoker(json));
        var s = await c.SuggestAsync(new[] { Pad(0, "BDRUM12") });
        Assert.Single(s);
        Assert.Equal("Drums", s[0].TrackName);
        Assert.Equal(0.95, s[0].Confidence);
        Assert.Equal("kick", s[0].Reason);
    }

    [Fact]
    public async Task Suggest_StripsCodeFence()
    {
        var json = "```json\n[{\"padIndex\":0,\"trackName\":\"Bass\"}]\n```";
        var c = new ClaudeClassifier("key", "claude-opus-5", new FakeInvoker(json));
        var s = await c.SuggestAsync(new[] { Pad(0, "AfHouse-Bas") });
        Assert.Equal("Bass", s[0].TrackName);
    }

    [Fact]
    public async Task Suggest_InvokerThrows_WrappedAsUnavailable()
    {
        var c = new ClaudeClassifier("key", "claude-opus-5", new FakeInvoker(new InvalidOperationException("boom")));
        await Assert.ThrowsAsync<ClassifierUnavailableException>(
            () => c.SuggestAsync(new[] { Pad(0, "x") }));
    }

    [Fact]
    public async Task Suggest_InvalidJson_WrappedAsUnavailable()
    {
        var c = new ClaudeClassifier("key", "claude-opus-5", new FakeInvoker("not json"));
        await Assert.ThrowsAsync<ClassifierUnavailableException>(
            () => c.SuggestAsync(new[] { Pad(0, "x") }));
    }
}
