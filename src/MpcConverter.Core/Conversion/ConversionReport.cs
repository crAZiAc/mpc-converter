using System.Collections.Generic;

namespace MpcConverter.Core.Conversion;

/// <summary>Summary of a conversion, surfaced to the user.</summary>
public sealed record ConversionReport(
    int TracksCreated,
    int PadsPlaced,
    int EventsMoved,
    int SamplesCopied,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Decisions);

/// <summary>Accumulator used while a conversion runs.</summary>
public sealed class ConversionReportBuilder
{
    public int TracksCreated { get; set; }
    public int PadsPlaced { get; set; }
    public int EventsMoved { get; set; }
    public int SamplesCopied { get; set; }
    public List<string> Warnings { get; } = new();
    public List<string> Decisions { get; } = new();

    public void Warn(string message) => Warnings.Add(message);
    public void Decide(string message) => Decisions.Add(message);

    public ConversionReport Build() => new(
        TracksCreated, PadsPlaced, EventsMoved, SamplesCopied,
        Warnings.AsReadOnly(), Decisions.AsReadOnly());
}
