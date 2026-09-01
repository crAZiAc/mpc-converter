namespace MpcConverter.Core.Classification;

/// <summary>A suggested destination track for a source pad.</summary>
public sealed record PadSuggestion(int PadIndex, string TrackName, double Confidence, string? Reason);
