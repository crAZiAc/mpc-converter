using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MpcConverter.Core.Model;

namespace MpcConverter.Core.Classification;

/// <summary>Thrown when the AI classifier cannot produce a result; callers fall back to rules.</summary>
public sealed class ClassifierUnavailableException : Exception
{
    public ClassifierUnavailableException(string message, Exception? inner = null)
        : base(message, inner) { }
}

/// <summary>
/// Abstracts the single Claude API call so the classification logic can be unit
/// tested without a network. Returns the model's raw JSON text response.
/// </summary>
public interface IAnthropicInvoker
{
    Task<string> GetGroupingJsonAsync(string model, string prompt, CancellationToken ct);
}

/// <summary>
/// Suggests a pad→track grouping using Claude. Only pad sample names are sent
/// (never audio). Parses the model's JSON array of suggestions.
/// </summary>
public sealed class ClaudeClassifier : IPadClassifier
{
    private readonly string _model;
    private readonly IAnthropicInvoker _invoker;

    public ClaudeClassifier(string apiKey, string model = "claude-opus-5", IAnthropicInvoker? invoker = null)
    {
        if (invoker is null)
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _model = model;
        _invoker = invoker ?? new AnthropicSdkInvoker(apiKey!);
    }

    public async Task<IReadOnlyList<PadSuggestion>> SuggestAsync(
        IReadOnlyList<PadInfo> pads, CancellationToken ct = default)
    {
        var prompt = BuildPrompt(pads);
        string json;
        try
        {
            json = await _invoker.GetGroupingJsonAsync(_model, prompt, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ClassifierUnavailableException("Claude classification failed.", ex);
        }

        return Parse(json, pads);
    }

    internal static string BuildPrompt(IReadOnlyList<PadInfo> pads)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are grouping AKAI MPC drum pads into instrument tracks.");
        sb.AppendLine("Each pad has one or more sample names. Assign every pad to a short,");
        sb.AppendLine("human-readable destination track name (e.g. \"Drums\", \"Bass\", \"Keys\",");
        sb.AppendLine("\"Synth\", \"Melodic\", \"FX\"). Group pads that belong to the same instrument");
        sb.AppendLine("family onto the same track name.");
        sb.AppendLine();
        sb.AppendLine("Pads:");
        foreach (var p in pads)
            sb.AppendLine($"  {p.PadIndex}: {string.Join(", ", p.SampleNames)}");
        sb.AppendLine();
        sb.AppendLine("Respond with ONLY a JSON array, no prose, of objects:");
        sb.AppendLine("[{\"padIndex\": <int>, \"trackName\": <string>, \"confidence\": <0..1>, \"reason\": <string>}]");
        return sb.ToString();
    }

    private static IReadOnlyList<PadSuggestion> Parse(string json, IReadOnlyList<PadInfo> pads)
    {
        var text = StripCodeFence(json).Trim();
        JsonNode? node;
        try { node = JsonNode.Parse(text); }
        catch (JsonException ex) { throw new ClassifierUnavailableException("Claude returned invalid JSON.", ex); }

        if (node is not JsonArray arr)
            throw new ClassifierUnavailableException("Claude response was not a JSON array.");

        var validPads = pads.Select(p => p.PadIndex).ToHashSet();
        var result = new List<PadSuggestion>();
        foreach (var item in arr)
        {
            if (item is not JsonObject o) continue;
            if (o["padIndex"] is not JsonValue pv || !pv.TryGetValue(out int padIndex)) continue;
            if (!validPads.Contains(padIndex)) continue;
            var track = (string?)o["trackName"];
            if (string.IsNullOrWhiteSpace(track)) continue;
            double confidence = o["confidence"] is JsonValue cv && cv.TryGetValue(out double c) ? c : 0.8;
            var reason = (string?)o["reason"];
            result.Add(new PadSuggestion(padIndex, track!, confidence, reason));
        }

        if (result.Count == 0)
            throw new ClassifierUnavailableException("Claude response contained no usable suggestions.");
        return result;
    }

    private static string StripCodeFence(string s)
    {
        s = s.Trim();
        if (!s.StartsWith("```")) return s;
        int firstNewline = s.IndexOf('\n');
        if (firstNewline < 0) return s;
        var body = s[(firstNewline + 1)..];
        int lastFence = body.LastIndexOf("```", StringComparison.Ordinal);
        return lastFence >= 0 ? body[..lastFence] : body;
    }
}
