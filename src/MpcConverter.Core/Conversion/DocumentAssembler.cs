using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using MpcConverter.Core.Templates;

namespace MpcConverter.Core.Conversion;

/// <summary>
/// Finalizes a converted document to the 3.10 top-level shape: bumps versions,
/// adds 3.10-only top-level fields, appends the standard mixer track set, and
/// rebuilds the top-level sample list as the union of what the new tracks use.
/// </summary>
public static class DocumentAssembler
{
    // A native MPC 3.9 project identifies its creator/last-saver as "ACVS" and runs
    // in "Main Mode" (the track-based engine). The old Sample-format source uses
    // "AC50" / "Sequence" (the pads-on-one-track paradigm); left unchanged, MPC 3.9
    // loads the project in that legacy mode and shows only one track and one
    // sequence. Forcing these makes it load as a native track-based project.
    private const string NativeProductIdentifier = "ACVS";
    private const string NativeEngineMode = "Main Mode";

    public static void FinalizeDocument(
        JsonObject data, IReadOnlyList<JsonObject> drumTracks, ConversionReportBuilder report)
    {
        data["version"] = 30;
        if (data["mixer"]?["input"] is JsonObject input)
            input["version"] = 6;

        data["originalCreatorProductIdentifier"] = NativeProductIdentifier;
        data["lastSavedProductIdentifier"] = NativeProductIdentifier;
        data["engineMode"] = NativeEngineMode;

        AddMissingTopLevelFields(data);

        // Rebuild tracks: musical drum tracks first, then the full mixer tree.
        var tracks = new JsonArray();
        foreach (var t in drumTracks) tracks.Add(t);
        var mixer = TemplateStore.GetArray("mixerTracks");
        foreach (var m in mixer) tracks.Add(m!.DeepClone());
        data["tracks"] = tracks;

        RebuildTopLevelSamples(data, drumTracks);

        // Songs/arrangements: House-style sources have empty songs; a full multi-track
        // song remap is out of scope for v1.
        if (HasNonEmptySongs(data))
            report.Decide("Songs contain arrangement items; track references were left " +
                          "pointing at the original track name (multi-track song remap is out of scope).");
    }

    private static void AddMissingTopLevelFields(JsonObject data)
    {
        var template = TemplateStore.Get("document");
        foreach (var kvp in template)
        {
            if (!data.ContainsKey(kvp.Key))
                data[kvp.Key] = kvp.Value?.DeepClone();
        }
    }

    private static void RebuildTopLevelSamples(JsonObject data, IReadOnlyList<JsonObject> drumTracks)
    {
        var union = new JsonArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var track in drumTracks)
        {
            if (track["program"]?["samples"] is not JsonArray samples) continue;
            foreach (var s in samples)
            {
                if (s is not JsonObject so) continue;
                var path = (string?)so["path"] ?? (string?)so["name"];
                if (path is null || !seen.Add(path)) continue;
                union.Add(so.DeepClone());
            }
        }
        data["samples"] = union;
    }

    private static bool HasNonEmptySongs(JsonObject data)
    {
        if (data["songs"] is not JsonArray songs) return false;
        foreach (var s in songs)
            if (s?["items"] is JsonArray items && items.Count > 0)
                return true;
        return false;
    }
}
