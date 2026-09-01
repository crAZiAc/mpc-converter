using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using MpcConverter.Core.Acvs;
using MpcConverter.Core.Model;
using MpcConverter.Core.ProjectIo;

namespace MpcConverter.Core.Conversion;

public static class Converter
{
    public const string TargetFormatVersion = "3.10.0.23";
    public const int TargetDataVersion = 30;

    /// <summary>
    /// MPC's 16-colour track/pad palette (stored as 0xRRGGBB integers), spread around
    /// the hue wheel. Values are taken from native MPC projects.
    /// </summary>
    public static readonly int[] Palette =
    {
        0xFF0000, // red
        0xFF6D17, // orange
        0xFF8800, // amber-orange
        0xFFD500, // amber
        0xE6FF00, // yellow
        0xA2FF00, // yellow-green
        0x55FF00, // lime
        0x11FF00, // green
        0x00FF80, // spring green
        0x00FFC4, // teal
        0x00AAFF, // sky blue
        0x0066FF, // blue
        0x0022FF, // deep blue
        0x5200FF, // indigo
        0xB200FF, // violet
        0xFF00FF, // magenta
    };

    /// <summary>The palette colour for a track, cycling (reused) beyond 16 tracks.</summary>
    public static int TrackColour(int index) => Palette[index % Palette.Length];

    /// <summary>
    /// Converts a source "Sample" project to a 3.10 track-based project in memory,
    /// splitting the Drum program's pads into tracks per <paramref name="map"/>.
    /// The source project is never modified.
    /// </summary>
    public static (MpcProject Project, ConversionReport Report) Convert(MpcProject source, PadTrackMap map)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(map);
        map.Validate();

        var report = new ConversionReportBuilder();

        var doc = source.Document.Clone();
        var data = doc.Data;

        var sourceDrumTrack = MpcJson.FindDrumTrack(data)
            ?? throw new InvalidOperationException("Source has no Drum program to convert.");
        var sourceDrumProgram = sourceDrumTrack["program"]!.AsObject();
        string sourceDrumTrackName = (string?)sourceDrumTrack["name"] ?? "";

        // Build the note→pad map from the SOURCE program before we replace tracks.
        var noteToPad = NoteToPad.From(data);

        // Build the new drum tracks (reads source samples + instruments).
        var drumTracks = new List<JsonObject>();
        for (int i = 0; i < map.Tracks.Count; i++)
        {
            var dest = map.Tracks[i];
            var track = ProgramBuilder.BuildDrumTrack(data, sourceDrumProgram, dest);
            track["colour"] = TrackColour(i);
            drumTracks.Add(track);
            report.PadsPlaced += dest.PadIndices.Count;
        }
        report.TracksCreated = drumTracks.Count;

        // Rewrite every sequence's clips.
        int eventsMoved = 0;
        foreach (var seq in data["sequences"]!.AsArray())
        {
            if (seq is JsonObject so && so["value"] is JsonObject value)
                eventsMoved += SequenceRewriter.RewriteSequence(
                    value, data, sourceDrumTrackName, map, noteToPad);
        }
        report.EventsMoved = eventsMoved;

        // Finalize document (tracks, mixer, top-level fields, samples union).
        DocumentAssembler.FinalizeDocument(data, drumTracks, report);

        // Native MPC lists EVERY track in EVERY sequence's clip map (empty clips for
        // non-playing tracks). MPC builds its track view from this, so without it only
        // the tracks that happen to play are shown.
        var allTrackNames = new List<string>();
        foreach (var t in data["tracks"]!.AsArray())
        {
            var name = (string?)t?["name"];
            if (name is not null) allTrackNames.Add(name);
        }
        SequenceRewriter.EnsureClipsForAllTracks(data, allTrackNames);

        // The old Sample format can leave gaps in sequence slot numbering; native
        // projects use contiguous slots and MPC rejects gaps.
        SequenceRewriter.NormalizeSequenceKeys(data);

        doc.FormatVersion = TargetFormatVersion;

        var project = new MpcProject
        {
            Name = source.Name,
            Document = doc,
            ProjectDataDir = source.ProjectDataDir,
        };
        return (project, report.Build());
    }

    /// <summary>The sample file names referenced by a converted project (for copying).</summary>
    public static IReadOnlyList<string> ReferencedSampleFiles(MpcProject project)
    {
        var files = new List<string>();
        if (project.Data["samples"] is JsonArray samples)
        {
            foreach (var s in samples)
            {
                var path = (string?)s?["path"];
                if (!string.IsNullOrEmpty(path)) files.Add(path);
            }
        }
        return files;
    }

    /// <summary>
    /// Re-reads a written project and asserts the conversion invariants. Throws
    /// <see cref="ConversionSelfCheckException"/> on any failure.
    /// </summary>
    public static void SelfCheck(string writtenProjectFolder, PadTrackMap map, int expectedType3Events)
    {
        var reopened = ProjectReader.Open(writtenProjectFolder);
        if (reopened.Document.FormatVersion != TargetFormatVersion)
            throw new ConversionSelfCheckException(
                $"Format version is '{reopened.Document.FormatVersion}', expected '{TargetFormatVersion}'.");

        var data = reopened.Data;
        if ((int?)data["version"] != TargetDataVersion)
            throw new ConversionSelfCheckException(
                $"data.version is {(int?)data["version"]}, expected {TargetDataVersion}.");

        // Count type-3 events across all clips.
        int total = 0;
        foreach (var seq in data["sequences"]!.AsArray())
        {
            if (seq is not JsonObject so || so["value"] is not JsonObject value) continue;
            foreach (var (_, clip) in MpcJson.EnumerateClips(value))
                total += MpcJson.NoteEvents(clip).Count();
        }
        if (total != expectedType3Events)
            throw new ConversionSelfCheckException(
                $"Note-event count {total} does not match expected {expectedType3Events}.");

        // Every note event resolves to a valid renormalized note (>= 36).
        var validNotes = new HashSet<int>();
        for (int slot = 0; slot < PadTrackMap.MaxSlotsPerTrack; slot++)
            validNotes.Add(MpcJson.BaseNote + slot);
        foreach (var seq in data["sequences"]!.AsArray())
        {
            if (seq is not JsonObject so || so["value"] is not JsonObject value) continue;
            foreach (var (_, clip) in MpcJson.EnumerateClips(value))
                foreach (var ev in MpcJson.NoteEvents(clip))
                    if (!validNotes.Contains(MpcJson.NoteOf(ev)))
                        throw new ConversionSelfCheckException(
                            $"Note {MpcJson.NoteOf(ev)} is outside the canonical pad range.");
        }
    }
}

public sealed class ConversionSelfCheckException : Exception
{
    public ConversionSelfCheckException(string message) : base(message) { }
}
