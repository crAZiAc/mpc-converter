using CommunityToolkit.Mvvm.ComponentModel;
using MpcConverter.Core.Model;

namespace MpcConverter.App.ViewModels;

/// <summary>One row in the pad-mapping grid.</summary>
public partial class PadRowViewModel : ObservableObject
{
    public PadInfo Pad { get; }

    public int PadIndex => Pad.PadIndex;
    public int SourceNote => Pad.SourceNote;
    public string Samples => string.Join(", ", Pad.SampleNames);
    public int EventCount => Pad.EventCount;

    /// <summary>Destination track name; blank/"(skip)" excludes the pad.</summary>
    [ObservableProperty]
    private string? _destTrackName;

    /// <summary>Reason/confidence tooltip from the last suggestion, if any.</summary>
    [ObservableProperty]
    private string? _suggestionReason;

    /// <summary>True when the last suggestion was low-confidence (needs a look).</summary>
    [ObservableProperty]
    private bool _lowConfidence;

    public PadRowViewModel(PadInfo pad)
    {
        Pad = pad;
    }
}
