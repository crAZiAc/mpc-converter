using System;
using System.Collections.Generic;
using System.Linq;

namespace MpcConverter.Core.Model;

/// <summary>
/// Keeps a live list of destination-track options (bound as a ComboBox ItemsSource)
/// in sync with the names assigned to pad rows — <b>without clearing it</b>. Clearing
/// would null every bound editable ComboBox's SelectedItem and wipe the value the
/// user just chose. Instead this removes only names no longer used and inserts new
/// ones in sorted order, so a name still in use is never removed.
/// </summary>
public static class TrackOptionsSync
{
    public const string SkipSentinel = "(skip)";

    public static void Sync(IList<string> options, IEnumerable<string?> assignedNames)
    {
        var desired = assignedNames
            .Where(n => !string.IsNullOrWhiteSpace(n) && n != SkipSentinel)
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Remove options no longer used by any row.
        for (int i = options.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(options[i], StringComparer.OrdinalIgnoreCase))
                options.RemoveAt(i);
        }

        // Insert missing options, keeping the list sorted (case-insensitive).
        foreach (var name in desired)
        {
            if (options.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;
            int idx = 0;
            while (idx < options.Count &&
                   string.Compare(options[idx], name, StringComparison.OrdinalIgnoreCase) < 0)
                idx++;
            options.Insert(idx, name);
        }
    }
}
