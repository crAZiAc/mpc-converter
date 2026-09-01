using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MpcConverter.Core.Model;

namespace MpcConverter.Core.Classification;

/// <summary>
/// Classifies pads into instrument-type tracks by matching keywords against each
/// pad's sample name(s). Offline, deterministic, free — the default suggester and
/// the fallback when AI is unavailable.
/// </summary>
public sealed class RuleBasedClassifier : IPadClassifier
{
    public const string FallbackBucket = "Melodic";

    // Order matters: the first bucket whose keyword appears in a sample name wins.
    private static readonly (string Bucket, string[] Keywords)[] DefaultBuckets =
    {
        ("Drums", new[] { "kick", "bdrum", "drum", "snare", "hh", "hat", "clap", "rim",
                          "crash", "ride", "tom", "perc", "break", "clhat", "clave", "shaker", "cowbell" }),
        ("Bass",  new[] { "bass", "bas", "sub", "808" }),
        ("Keys",  new[] { "key", "ep", "rhodes", "piano", "organ", "mkt" }),
        ("Synth", new[] { "synth", "lead", "pad", "arp", "saw", "retro", "magic", "deeper", "pluck" }),
        ("Melodic", new[] { "melodic", "orng", "chord", "string", "violin", "brass", "vox", "voice" }),
    };

    private readonly (string Bucket, string[] Keywords)[] _buckets;

    public RuleBasedClassifier(IReadOnlyDictionary<string, string[]>? buckets = null)
    {
        _buckets = buckets is null
            ? DefaultBuckets
            : buckets.Select(kv => (kv.Key, kv.Value)).ToArray();
    }

    public Task<IReadOnlyList<PadSuggestion>> SuggestAsync(
        IReadOnlyList<PadInfo> pads, CancellationToken ct = default)
    {
        var result = new List<PadSuggestion>(pads.Count);
        foreach (var pad in pads)
        {
            var (bucket, keyword) = Classify(pad.SampleNames);
            if (bucket is null)
                result.Add(new PadSuggestion(pad.PadIndex, FallbackBucket, 0.3,
                    "No keyword matched; placed in fallback bucket."));
            else
                result.Add(new PadSuggestion(pad.PadIndex, bucket, 0.9,
                    $"Matched '{keyword}'."));
        }
        return Task.FromResult<IReadOnlyList<PadSuggestion>>(result);
    }

    private (string? Bucket, string? Keyword) Classify(IReadOnlyList<string> sampleNames)
    {
        // Tokenize on non-letter boundaries and match a keyword as a token prefix.
        // This avoids false positives from substring matches (e.g. "rim" inside
        // "Trimmed", or "ep" inside "Deeper").
        var tokens = sampleNames
            .SelectMany(Tokenize)
            .ToList();

        foreach (var (bucket, keywords) in _buckets)
            foreach (var token in tokens)
                foreach (var kw in keywords)
                    if (token.StartsWith(kw, StringComparison.Ordinal))
                        return (bucket, kw);

        return (null, null);
    }

    private static IEnumerable<string> Tokenize(string name)
    {
        var current = new System.Text.StringBuilder();
        foreach (var ch in name.ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z')
            {
                current.Append(ch);
            }
            else if (current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
            }
        }
        if (current.Length > 0) yield return current.ToString();
    }
}
