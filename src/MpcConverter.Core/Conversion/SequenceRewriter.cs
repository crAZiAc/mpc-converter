using System.Collections.Generic;
using System.Text.Json.Nodes;
using MpcConverter.Core.Model;
using MpcConverter.Core.Templates;

namespace MpcConverter.Core.Conversion;

/// <summary>
/// Rewrites one sequence: replaces the single source Drum clip with one clip per
/// destination track. Each event is routed to the destination track of its source
/// pad and its note is renormalized to <c>36 + slot</c>. Events for skipped pads
/// are dropped. Destination clips with no events are omitted (matching MPC, which
/// stores a clip per track only where the track plays).
/// </summary>
public static class SequenceRewriter
{
    /// <returns>The number of type-3 note events written across all destination clips.</returns>
    public static int RewriteSequence(
        JsonObject sequenceValue, JsonObject sourceData,
        string sourceDrumTrackName, PadTrackMap map, NoteToPad noteToPad)
    {
        // Locate the source drum clip and capture its shell + events.
        JsonObject? sourceClip = null;
        foreach (var (key, clip) in MpcJson.EnumerateClips(sequenceValue))
        {
            if (key == sourceDrumTrackName) { sourceClip = clip; break; }
        }

        // Prepare one destination clip per track (created lazily as events land).
        var destClips = new Dictionary<string, JsonObject>();
        var destEvents = new Dictionary<string, JsonArray>();
        int noteEventsWritten = 0;

        if (sourceClip is not null && sourceClip["eventList"]?["events"] is JsonArray events)
        {
            foreach (var evNode in events)
            {
                if (evNode is not JsonObject ev) continue;
                int srcNote = ReadNote(ev, out bool isTyped3, out JsonObject? noteHolder);
                if (srcNote < 0) continue;
                if (!noteToPad.TryPad(srcNote, out int padIndex)) continue;
                if (!map.TryGet(padIndex, out string trackName, out int slot)) continue; // skipped pad

                if (!destClips.TryGetValue(trackName, out var destClip))
                {
                    destClip = BuildClipShell(sourceClip, trackName);
                    destClips[trackName] = destClip;
                    destEvents[trackName] = destClip["eventList"]!["events"]!.AsArray();
                }

                var clone = (JsonObject)ev.DeepClone();
                WriteNote(clone, MpcJson.BaseNote + slot);
                destEvents[trackName].Add(clone);
                if (isTyped3) noteEventsWritten++;
            }
        }

        // Replace the sequence's trackClipMaps with a single group of the new clips,
        // ordered to follow the map's track order.
        var group = new JsonArray();
        foreach (var t in map.Tracks)
        {
            if (!destClips.TryGetValue(t.Name, out var clip)) continue; // no events → no clip
            var entry = new JsonObject
            {
                ["key"] = t.Name,
                ["value"] = clip,
            };
            group.Add(entry);
        }
        sequenceValue["trackClipMaps"] = new JsonArray(group);
        return noteEventsWritten;
    }

    /// <summary>
    /// Ensures every sequence's <c>trackClipMaps</c> contains a clip entry for every
    /// track (in <paramref name="trackNames"/> order), reusing existing clips and
    /// inserting empty clips for tracks that don't play in that sequence. Native MPC
    /// projects list ALL tracks — including mixer tracks — in every sequence; MPC
    /// builds its track view from this, so a track missing here is not shown.
    /// </summary>
    public static void EnsureClipsForAllTracks(JsonObject data, IReadOnlyList<string> trackNames)
    {
        if (data["sequences"] is not JsonArray sequences) return;
        foreach (var seqNode in sequences)
        {
            if (seqNode is not JsonObject seq || seq["value"] is not JsonObject value) continue;

            var existing = new Dictionary<string, JsonObject>();
            foreach (var (key, clip) in MpcJson.EnumerateClips(value))
                existing[key] = clip;

            var group = new JsonArray();
            foreach (var name in trackNames)
            {
                JsonObject clip = existing.TryGetValue(name, out var found)
                    ? (JsonObject)found.DeepClone()
                    : BuildEmptyClip(name);
                group.Add(new JsonObject { ["key"] = name, ["value"] = clip });
            }
            value["trackClipMaps"] = new JsonArray(group);
        }
    }

    private static JsonObject BuildEmptyClip(string name)
    {
        var clip = TemplateStore.Get("clip");
        clip["version"] = 3;
        clip["name"] = name;
        if (clip["eventList"] is JsonObject el)
            el["events"] = new JsonArray();
        return clip;
    }

    private static JsonObject BuildClipShell(JsonObject sourceClip, string name)
    {
        var clip = JsonMerge.UpgradeOnto(TemplateStore.Get("clip"), sourceClip);
        clip["version"] = 3;
        clip["name"] = name;
        // Start with an empty event list; preserve the merged eventList metadata.
        if (clip["eventList"] is JsonObject el)
            el["events"] = new JsonArray();
        else
            clip["eventList"] = new JsonObject { ["events"] = new JsonArray() };
        return clip;
    }

    /// <summary>Reads a note event's MIDI note; sets the holder for later rewrite.</summary>
    private static int ReadNote(JsonObject ev, out bool isType3, out JsonObject? holder)
    {
        isType3 = false;
        holder = null;
        if (ev["note"] is JsonObject noteObj && noteObj["note"] is JsonValue nv &&
            nv.TryGetValue(out int n))
        {
            isType3 = true;
            holder = noteObj;
            return n;
        }
        if (ev["automation"] is JsonObject autoObj && autoObj["note"] is JsonValue av &&
            av.TryGetValue(out int an))
        {
            holder = autoObj;
            return an;
        }
        return -1;
    }

    private static void WriteNote(JsonObject ev, int note)
    {
        if (ev["note"] is JsonObject noteObj && noteObj.ContainsKey("note"))
            noteObj["note"] = note;
        else if (ev["automation"] is JsonObject autoObj && autoObj.ContainsKey("note"))
            autoObj["note"] = note;
    }
}
