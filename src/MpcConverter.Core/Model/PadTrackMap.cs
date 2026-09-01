using System;
using System.Collections.Generic;
using System.Linq;

namespace MpcConverter.Core.Model;

/// <summary>A destination track and the source pad indices assigned to it, in slot order.</summary>
public sealed record DestTrack(string Name, IReadOnlyList<int> PadIndices);

/// <summary>
/// Maps source pads to destination tracks. Each pad lands at a canonical "slot"
/// (its index within its destination track's <see cref="DestTrack.PadIndices"/>),
/// which renormalizes it to MIDI note <c>36 + slot</c>.
/// </summary>
public sealed class PadTrackMap
{
    public const int MaxSlotsPerTrack = 128;

    public IReadOnlyList<DestTrack> Tracks { get; }

    private readonly Dictionary<int, (string Track, int Slot)> _byPad;

    private PadTrackMap(IReadOnlyList<DestTrack> tracks)
    {
        Tracks = tracks;
        _byPad = new Dictionary<int, (string, int)>();
        foreach (var t in tracks)
            for (int slot = 0; slot < t.PadIndices.Count; slot++)
                _byPad[t.PadIndices[slot]] = (t.Name, slot);
    }

    /// <summary>One destination track per pad; track name defaults to the pad's first sample.</summary>
    public static PadTrackMap OneTrackPerPad(IEnumerable<PadInfo> pads)
    {
        var tracks = new List<DestTrack>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in pads)
        {
            var baseName = p.SampleNames.Count > 0 ? p.SampleNames[0] : $"Pad {p.PadIndex}";
            var name = Uniquify(baseName, used);
            tracks.Add(new DestTrack(name, new[] { p.PadIndex }));
        }
        return new PadTrackMap(tracks);
    }

    /// <summary>All pads combined onto one destination track (slots 0..n-1).</summary>
    public static PadTrackMap AllToOne(IEnumerable<PadInfo> pads, string name)
    {
        var indices = pads.Select(p => p.PadIndex).ToArray();
        return new PadTrackMap(new[] { new DestTrack(name, indices) });
    }

    /// <summary>
    /// Builds a map from an explicit pad→trackName assignment. A null/blank track
    /// name skips that pad. Destination track order follows first appearance;
    /// within a track, pads follow the <paramref name="pads"/> order.
    /// </summary>
    public static PadTrackMap FromAssignments(
        IEnumerable<PadInfo> pads, IReadOnlyDictionary<int, string?> padToTrack)
    {
        var order = new List<string>();
        var groups = new Dictionary<string, List<int>>();
        foreach (var p in pads)
        {
            if (!padToTrack.TryGetValue(p.PadIndex, out var track) || string.IsNullOrWhiteSpace(track))
                continue; // skipped
            if (!groups.TryGetValue(track, out var list))
            {
                list = new List<int>();
                groups[track] = list;
                order.Add(track);
            }
            list.Add(p.PadIndex);
        }
        var tracks = order.Select(name => new DestTrack(name, groups[name].ToArray())).ToList();
        return new PadTrackMap(tracks);
    }

    public bool TryGet(int padIndex, out string track, out int slot)
    {
        if (_byPad.TryGetValue(padIndex, out var v))
        {
            track = v.Track; slot = v.Slot; return true;
        }
        track = ""; slot = -1; return false;
    }

    public int SlotOf(int padIndex) => _byPad.TryGetValue(padIndex, out var v) ? v.Slot : -1;
    public string? TrackOf(int padIndex) => _byPad.TryGetValue(padIndex, out var v) ? v.Track : null;

    /// <summary>The renormalized MIDI note a pad plays on its destination track.</summary>
    public int NoteOf(int padIndex)
    {
        int slot = SlotOf(padIndex);
        return slot < 0 ? -1 : MpcJson.BaseNote + slot;
    }

    public void Validate()
    {
        if (Tracks.Count == 0)
            throw new InvalidOperationException("Mapping produces no tracks (every pad skipped).");
        foreach (var t in Tracks)
        {
            if (t.PadIndices.Count > MaxSlotsPerTrack)
                throw new InvalidOperationException(
                    $"Track '{t.Name}' has {t.PadIndices.Count} pads; max is {MaxSlotsPerTrack}.");
        }
    }

    private static string Uniquify(string baseName, HashSet<string> used)
    {
        var name = baseName;
        int i = 2;
        while (!used.Add(name))
            name = $"{baseName} {i++}";
        return name;
    }
}
