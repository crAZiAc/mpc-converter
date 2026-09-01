using System.Collections.Generic;
using System.Text.Json.Nodes;
using MpcConverter.Core.Model;

namespace MpcConverter.Core.Analysis;

public static class PadAnalyzer
{
    /// <summary>
    /// Analyzes the first Drum program in <paramref name="data"/> and returns one
    /// <see cref="PadInfo"/> per pad slot that carries a sample. EventCount is the
    /// number of note events for that pad's note summed across ALL sequences.
    /// </summary>
    public static IReadOnlyList<PadInfo> Analyze(JsonObject data)
    {
        var track = MpcJson.FindDrumTrack(data)
            ?? throw new System.InvalidOperationException("No Drum program (type 0) found in project.");
        var program = track["program"]!.AsObject();
        var instruments = program["drum"]?["instruments"] as JsonArray
            ?? throw new System.InvalidOperationException("Drum program has no instruments.");

        string drumTrackName = (string?)track["name"] ?? "";

        // Count note events per MIDI note across all sequences (for this drum track).
        var eventsByNote = CountEventsByNote(data, drumTrackName);

        var result = new List<PadInfo>();
        for (int i = 0; i < instruments.Count; i++)
        {
            if (instruments[i] is not JsonObject ins) continue;
            var sampleNames = new List<string>();
            var sampleFiles = new List<string>();
            if (ins["layersv"] is JsonArray layers)
            {
                foreach (var layer in layers)
                {
                    if (layer is not JsonObject lo) continue;
                    var name = (string?)lo["sampleName"];
                    if (!string.IsNullOrEmpty(name))
                    {
                        sampleNames.Add(name);
                        var file = (string?)lo["sampleFile"];
                        if (!string.IsNullOrEmpty(file)) sampleFiles.Add(file);
                    }
                }
            }

            bool hasContent = sampleNames.Count > 0;
            if (!hasContent) continue; // only report sampled pads

            int note = MpcJson.NoteForPad(program, i);
            eventsByNote.TryGetValue(note, out int count);
            result.Add(new PadInfo(i, note, sampleNames, sampleFiles, count, hasContent));
        }
        return result;
    }

    private static Dictionary<int, int> CountEventsByNote(JsonObject data, string drumTrackName)
    {
        var counts = new Dictionary<int, int>();
        if (data["sequences"] is not JsonArray sequences) return counts;
        foreach (var seq in sequences)
        {
            if (seq is not JsonObject so || so["value"] is not JsonObject value) continue;
            foreach (var (key, clip) in MpcJson.EnumerateClips(value))
            {
                if (key != drumTrackName) continue;
                foreach (var ev in MpcJson.NoteEvents(clip))
                {
                    int n = MpcJson.NoteOf(ev);
                    if (n >= 0) counts[n] = counts.GetValueOrDefault(n) + 1;
                }
            }
        }
        return counts;
    }
}
