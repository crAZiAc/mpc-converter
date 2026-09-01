using System;
using System.Collections.Generic;

namespace MpcConverter.Core.Conversion;

/// <summary>
/// Per-pad colours for converted Drum programs. On a drum-kit track (named
/// <see cref="DrumKitTrackName"/>) each pad is coloured by its drum type; on any
/// other track the pads follow the track colour instead.
/// </summary>
public static class PadColouring
{
    public const string DrumKitTrackName = "Drums";

    // MPC stores colour as 0xRRGGBB. Values are drawn from MPC's own palette.
    public const int Red = 0xFF0000;    // Kick
    public const int Green = 0x11FF00;  // Snare
    public const int Yellow = 0xECEC24; // Hi-hats
    public const int Blue = 0x0066FF;   // Toms
    public const int Purple = 0xB200FF; // Other

    public static bool IsDrumKitTrack(string trackName)
        => string.Equals(trackName, DrumKitTrackName, StringComparison.OrdinalIgnoreCase);

    // First matching category wins. Matched as token prefixes (see Tokenize) so
    // "kick", "kik", "bdrum" hit Kick but "trick" does not.
    private static readonly (int Colour, string[] Keywords)[] Categories =
    {
        (Red,    new[] { "kick", "kik", "bdrum", "bassdrum", "bd" }),
        (Green,  new[] { "snare", "snr", "sd" }),
        (Yellow, new[] { "hihat", "hihats", "hat", "hats", "hh", "openhat", "closedhat",
                         "ohh", "chh", "clhat" }),
        (Blue,   new[] { "tom", "toms", "floortom", "hitom", "lowtom", "midtom" }),
    };

    /// <summary>Colour for a drum pad from its sample name(s); unmatched → Purple (Other).</summary>
    public static int ColourForSample(IEnumerable<string> sampleNames)
    {
        var tokens = new List<string>();
        foreach (var n in sampleNames) tokens.AddRange(Tokenize(n));

        foreach (var (colour, keywords) in Categories)
            foreach (var token in tokens)
                foreach (var kw in keywords)
                    if (token.StartsWith(kw, StringComparison.Ordinal))
                        return colour;
        return Purple;
    }

    private static IEnumerable<string> Tokenize(string name)
    {
        var current = new System.Text.StringBuilder();
        foreach (var ch in name.ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z')
            {
                current.Append(ch);
            }
            else if (current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
            }
        }
        if (current.Length > 0) yield return current.ToString();
    }
}
