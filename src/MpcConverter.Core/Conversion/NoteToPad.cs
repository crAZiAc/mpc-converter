using System.Collections.Generic;
using System.Text.Json.Nodes;
using MpcConverter.Core.Model;

namespace MpcConverter.Core.Conversion;

/// <summary>Inverse of a Drum program's pad→note map: MIDI note → source pad index.</summary>
public sealed class NoteToPad
{
    private readonly Dictionary<int, int> _noteToPad;

    private NoteToPad(Dictionary<int, int> map) => _noteToPad = map;

    public static NoteToPad From(JsonObject data)
    {
        var map = new Dictionary<int, int>();
        var track = MpcJson.FindDrumTrack(data);
        var noteForPad = track?["program"]?["padNoteMap"]?["noteForPad"] as JsonObject;
        if (noteForPad is not null)
        {
            foreach (var kvp in noteForPad)
            {
                // key is "value{padIndex}"
                if (kvp.Key.StartsWith("value") &&
                    int.TryParse(kvp.Key.AsSpan(5), out int padIndex) &&
                    kvp.Value is JsonValue v && v.TryGetValue(out int note))
                {
                    // First pad claiming a note wins (pad maps are 1:1 in practice).
                    map.TryAdd(note, padIndex);
                }
            }
        }
        return new NoteToPad(map);
    }

    public bool TryPad(int note, out int padIndex) => _noteToPad.TryGetValue(note, out padIndex);
}
