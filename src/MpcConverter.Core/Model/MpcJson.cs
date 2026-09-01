using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace MpcConverter.Core.Model;

/// <summary>
/// Small helpers for navigating the MPC JSON shape (which uses nested
/// key/value list wrappers rather than plain objects in several places).
/// </summary>
public static class MpcJson
{
    public const int DrumProgramType = 0;
    public const int BaseNote = 36; // canonical pad-0 MIDI note

    /// <summary>Returns the first track whose program.type == 0 (Drum), or null.</summary>
    public static JsonObject? FindDrumTrack(JsonObject data)
    {
        if (data["tracks"] is not JsonArray tracks) return null;
        foreach (var t in tracks)
        {
            if (t is JsonObject to &&
                to["program"] is JsonObject prog &&
                prog["type"] is JsonValue tv &&
                tv.TryGetValue(out int type) &&
                type == DrumProgramType)
            {
                return to;
            }
        }
        return null;
    }

    /// <summary>The pad→note map: <c>program.padNoteMap.noteForPad["value{i}"]</c>.</summary>
    public static int NoteForPad(JsonObject program, int padIndex)
    {
        var map = program["padNoteMap"]?["noteForPad"]?[$"value{padIndex}"];
        return map is JsonValue v && v.TryGetValue(out int n) ? n : BaseNote + padIndex;
    }

    /// <summary>
    /// Enumerates the (trackName, clipObject) pairs stored in a sequence's
    /// <c>trackClipMaps</c>. The structure is a list of groups; each group is a
    /// list of <c>{ "key": name, "value": clip }</c> wrappers.
    /// </summary>
    public static IEnumerable<(string Key, JsonObject Clip)> EnumerateClips(JsonObject sequenceValue)
    {
        if (sequenceValue["trackClipMaps"] is not JsonArray groups) yield break;
        foreach (var group in groups)
        {
            if (group is not JsonArray entries) continue;
            foreach (var entry in entries)
            {
                if (entry is JsonObject eo &&
                    eo["key"] is JsonValue kv && kv.TryGetValue(out string? key) && key is not null &&
                    eo["value"] is JsonObject clip)
                {
                    yield return (key, clip);
                }
            }
        }
    }

    /// <summary>Enumerates note events (type == 3) within a clip's eventList.</summary>
    public static IEnumerable<JsonObject> NoteEvents(JsonObject clip)
    {
        if (clip["eventList"]?["events"] is not JsonArray events) yield break;
        foreach (var e in events)
        {
            if (e is JsonObject eo && eo["type"] is JsonValue tv &&
                tv.TryGetValue(out int type) && type == 3)
            {
                yield return eo;
            }
        }
    }

    /// <summary>Reads <c>event.note.note</c> for a type-3 note event.</summary>
    public static int NoteOf(JsonObject noteEvent)
        => noteEvent["note"]?["note"] is JsonValue v && v.TryGetValue(out int n) ? n : -1;
}
