using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using MpcConverter.Core.Model;
using MpcConverter.Core.Templates;

namespace MpcConverter.Core.Conversion;

/// <summary>
/// Builds a 3.10 Drum-program track for one <see cref="DestTrack"/>, placing each
/// assigned source pad at a canonical slot (renormalized to note 36+slot) and
/// upgrading its instrument/layers via the superset merge.
/// </summary>
public static class ProgramBuilder
{
    private const int InstrumentVersion = 29;

    public static JsonObject BuildDrumTrack(
        JsonObject sourceData, JsonObject sourceDrumProgram, DestTrack dest)
    {
        var track = TemplateStore.Get("track");
        track["name"] = dest.Name;

        // A drum program must keep the full 128 pad slots (128 instruments + the
        // 128-entry pad-indexed maps). MPC rejects a program with fewer and loads a
        // blank default project instead, so we never shrink it.
        var program = TemplateStore.Get("drumProgram");
        program["name"] = dest.Name;

        var instruments = program["drum"]!["instruments"]!.AsArray();
        var noteForPad = program["padNoteMap"]!["noteForPad"]!.AsObject();
        var programPads = program["programPads"]!.AsObject();
        var padColours = programPads["pads"]!.AsObject();
        var sampleIndex = IndexSamples(sourceData);
        var programSamples = new JsonArray();
        var seenSamplePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sourceInstruments = sourceDrumProgram["drum"]!["instruments"]!.AsArray();
        bool isDrumKit = PadColouring.IsDrumKitTrack(dest.Name);

        for (int slot = 0; slot < dest.PadIndices.Count; slot++)
        {
            int padIndex = dest.PadIndices[slot];
            if (padIndex < 0 || padIndex >= sourceInstruments.Count) continue;
            var srcInstr = sourceInstruments[padIndex]!.AsObject();

            var built = UpgradeInstrument(srcInstr);
            instruments[slot] = built;

            // Renormalize: this slot is triggered by note 36+slot.
            noteForPad[$"value{slot}"] = MpcJson.BaseNote + slot;

            // Collect the pad's referenced samples; note names for pad colouring.
            var padSampleNames = new List<string>();
            if (built["layersv"] is JsonArray layers)
            {
                foreach (var layer in layers)
                {
                    var file = (string?)layer?["sampleFile"];
                    var name = (string?)layer?["sampleName"];
                    if (string.IsNullOrEmpty(name)) continue;
                    padSampleNames.Add(name);
                    var key = file ?? name;
                    if (!seenSamplePaths.Add(key)) continue;
                    var sampleObj = LookupSample(sampleIndex, name, file);
                    if (sampleObj is not null)
                        programSamples.Add(sampleObj.DeepClone());
                }
            }

            // On a "Drums" kit track, colour each pad by its drum type.
            if (isDrumKit)
                padColours[$"value{slot}"] = PadColouring.ColourForSample(padSampleNames);
        }

        program["samples"] = programSamples;

        // Drum-kit tracks show per-pad colours; every other track's pads follow the
        // track colour.
        bool follow = !isDrumKit;
        if (programPads["PadsFollowTrackColour"] is JsonObject pf)
            pf["value0"] = follow;
        track["padsFollowTrackColour"] = follow;

        track["program"] = program;
        return track;
    }

    // Pad output routing: destination 0 = "Program" (the track), 2 = "Out 1/2".
    // Source Sample projects route each pad direct to Out 1/2, which bypasses the
    // track's mixer channel (and its meter); native projects route pads to Program.
    private const int RouteToProgram = 0;

    /// <summary>Upgrades a v28 drum instrument (and each of its layers) to v29 schema.</summary>
    public static JsonObject UpgradeInstrument(JsonObject srcInstr)
    {
        var merged = JsonMerge.UpgradeOnto(TemplateStore.Get("instrument"), srcInstr);
        merged["version"] = InstrumentVersion;

        // Route the pad's output to the Program (Track), not directly to an output.
        if (merged["mixable"]?["audioRoute"] is JsonObject audioRoute)
            audioRoute["destination"] = RouteToProgram;

        // The plain merge copies the source layersv array wholesale (v28 schema);
        // re-upgrade each layer onto the 3.10 layer template so new fields exist.
        var upgradedLayers = new JsonArray();
        if (srcInstr["layersv"] is JsonArray srcLayers)
        {
            foreach (var srcLayer in srcLayers)
            {
                if (srcLayer is JsonObject lo)
                    upgradedLayers.Add(JsonMerge.UpgradeOnto(TemplateStore.Get("layer"), lo));
            }
        }
        merged["layersv"] = upgradedLayers;
        return merged;
    }

    private static Dictionary<string, JsonObject> IndexSamples(JsonObject sourceData)
    {
        var index = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        if (sourceData["samples"] is JsonArray samples)
        {
            foreach (var s in samples)
            {
                if (s is not JsonObject so) continue;
                var name = (string?)so["name"];
                var path = (string?)so["path"];
                if (!string.IsNullOrEmpty(name)) index.TryAdd(name, so);
                if (!string.IsNullOrEmpty(path)) index.TryAdd(path, so);
            }
        }
        return index;
    }

    private static JsonObject? LookupSample(
        Dictionary<string, JsonObject> index, string? name, string? file)
    {
        if (file is not null && index.TryGetValue(file, out var byFile)) return byFile;
        if (name is not null && index.TryGetValue(name, out var byName)) return byName;
        return null;
    }
}
