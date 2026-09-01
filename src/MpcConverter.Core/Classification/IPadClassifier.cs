using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MpcConverter.Core.Model;

namespace MpcConverter.Core.Classification;

/// <summary>Suggests a destination-track grouping for a set of source pads.</summary>
public interface IPadClassifier
{
    Task<IReadOnlyList<PadSuggestion>> SuggestAsync(
        IReadOnlyList<PadInfo> pads, CancellationToken ct = default);
}
