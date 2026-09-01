using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;

namespace MpcConverter.Core.Templates;

/// <summary>
/// Loads the embedded 3.10 default JSON fragments (extracted once from a real
/// MPC 3.9 project) used as the base for the superset field-merge. Each call
/// returns a fresh deep clone so callers can mutate freely.
/// </summary>
public static class TemplateStore
{
    private static readonly Assembly Asm = typeof(TemplateStore).Assembly;
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    // Known template names: document, track, drumProgram, instrument, layer,
    // clip, submixTrack, returnTrack, outTrack.
    public static JsonObject Get(string name)
    {
        var json = Cache.GetOrAdd(name, Load);
        return (JsonObject)JsonNode.Parse(json)!;
    }

    /// <summary>Loads a template whose root is a JSON array (e.g. "mixerTracks").</summary>
    public static JsonArray GetArray(string name)
    {
        var json = Cache.GetOrAdd(name, Load);
        return (JsonArray)JsonNode.Parse(json)!;
    }

    private static string Load(string name)
    {
        // Resource names are "<RootNamespace>.Templates.<name>.json".
        var resourceName = $"{Asm.GetName().Name}.Templates.{name}.json";
        using var stream = Asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded template not found: {resourceName}. " +
                $"Available: {string.Join(", ", Asm.GetManifestResourceNames())}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
