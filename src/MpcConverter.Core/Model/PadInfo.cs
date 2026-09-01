using System.Collections.Generic;

namespace MpcConverter.Core.Model;

/// <summary>A single sampled pad discovered in a source Drum program.</summary>
public sealed record PadInfo(
    int PadIndex,
    int SourceNote,
    IReadOnlyList<string> SampleNames,
    IReadOnlyList<string> SampleFiles,
    int EventCount,
    bool HasContent);
