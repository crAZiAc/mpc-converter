using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MpcConverter.Core.Analysis;
using MpcConverter.Core.Classification;
using MpcConverter.Core.Conversion;
using MpcConverter.Core.Model;
using MpcConverter.Core.ProjectIo;

var options = CliOptions.Parse(args);
if (options is null) return 2;      // parse error / help already printed
if (options.ShowHelp) { CliOptions.PrintHelp(); return 0; }

// Gather input .xpj files.
var inputs = new List<string>();
foreach (var path in options.Inputs)
{
    if (Directory.Exists(path))
    {
        var opt = options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        inputs.AddRange(Directory.EnumerateFiles(path, "*.xpj", opt));
    }
    else if (File.Exists(path))
    {
        inputs.Add(path);
    }
    else
    {
        Console.Error.WriteLine($"warning: not found, skipping: {path}");
    }
}
// Don't reconvert our own outputs when scanning directories.
inputs = inputs
    .Where(f => !Path.GetFileNameWithoutExtension(f).EndsWith(options.Suffix, StringComparison.OrdinalIgnoreCase))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
    .ToList();

if (inputs.Count == 0)
{
    Console.Error.WriteLine("No .xpj files to convert.");
    return 1;
}

Console.WriteLine($"Converting {inputs.Count} project(s)…\n");
int ok = 0, skipped = 0, failed = 0;
var classifier = new RuleBasedClassifier();

foreach (var input in inputs)
{
    var label = Path.GetFileName(input);
    try
    {
        var source = ProjectReader.Open(input);
        var pads = PadAnalyzer.Analyze(source.Data);
        if (pads.Count == 0)
        {
            Console.WriteLine($"  SKIP  {label}  (no sampled drum pads)");
            skipped++;
            continue;
        }

        var map = await BuildMapAsync(options, pads, classifier);
        map.Validate();

        var (project, report) = Converter.Convert(source, map);

        var destParent = options.OutputDir ?? Path.GetDirectoryName(Path.GetFullPath(input))!;
        var name = source.Name + options.Suffix;
        var referenced = Converter.ReferencedSampleFiles(project);
        var warnings = new List<string>();
        var folder = ProjectWriter.Write(
            project, destParent, name, referenced,
            overwrite: options.Overwrite, warnings);

        Converter.SelfCheck(folder, map, report.EventsMoved);

        int copied = referenced.Count - warnings.Count;
        Console.WriteLine(
            $"  OK    {label} → {name}  " +
            $"[{report.TracksCreated} tracks, {report.EventsMoved} events, {copied}/{referenced.Count} samples]");
        foreach (var w in warnings) Console.WriteLine($"          warn: {w}");
        ok++;
    }
    catch (IOException ex) when (ex.Message.Contains("already exists"))
    {
        Console.Error.WriteLine($"  FAIL  {label}  (output exists; use --overwrite)");
        failed++;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"  FAIL  {label}  ({ex.Message})");
        failed++;
    }
}

Console.WriteLine($"\nDone. {ok} converted, {skipped} skipped, {failed} failed.");
return failed > 0 ? 1 : 0;

static async Task<PadTrackMap> BuildMapAsync(
    CliOptions options, IReadOnlyList<PadInfo> pads, RuleBasedClassifier classifier)
{
    switch (options.Group)
    {
        case GroupMode.PerPad:
            return PadTrackMap.OneTrackPerPad(pads);
        case GroupMode.One:
            return PadTrackMap.AllToOne(pads, options.TrackName);
        default: // Rules
            var suggestions = await classifier.SuggestAsync(pads);
            var assignments = suggestions.ToDictionary(s => s.PadIndex, s => (string?)s.TrackName);
            return PadTrackMap.FromAssignments(pads, assignments);
    }
}

enum GroupMode { Rules, PerPad, One }

sealed class CliOptions
{
    public List<string> Inputs { get; } = new();
    public string? OutputDir { get; set; }
    public GroupMode Group { get; set; } = GroupMode.Rules;
    public string TrackName { get; set; } = "Drums";
    public string Suffix { get; set; } = " (3.9)";
    public bool Overwrite { get; set; }
    public bool Recursive { get; set; }
    public bool ShowHelp { get; set; }

    public static CliOptions? Parse(string[] args)
    {
        var o = new CliOptions();
        if (args.Length == 0) { o.ShowHelp = true; return o; }

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "-h" or "--help": o.ShowHelp = true; return o;
                case "--out" or "-o": o.OutputDir = Next(args, ref i, a); break;
                case "--group" or "-g":
                    var g = Next(args, ref i, a);
                    o.Group = g?.ToLowerInvariant() switch
                    {
                        "rules" => GroupMode.Rules,
                        "per-pad" or "perpad" => GroupMode.PerPad,
                        "one" => GroupMode.One,
                        _ => throw new ArgumentException($"Unknown --group '{g}' (rules|per-pad|one)"),
                    };
                    break;
                case "--track-name": o.TrackName = Next(args, ref i, a) ?? o.TrackName; break;
                case "--suffix": o.Suffix = Next(args, ref i, a) ?? o.Suffix; break;
                case "--overwrite": o.Overwrite = true; break;
                case "--recursive" or "-r": o.Recursive = true; break;
                default:
                    if (a.StartsWith('-'))
                    {
                        Console.Error.WriteLine($"Unknown option: {a}");
                        return null;
                    }
                    o.Inputs.Add(a);
                    break;
            }
        }

        if (o.OutputDir is not null) Directory.CreateDirectory(o.OutputDir);
        return o;
    }

    private static string? Next(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"Missing value for {flag}");
        return args[++i];
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
mpcconvert — batch-convert AKAI MPC Sample projects to MPC 3 (track-based).

USAGE:
  mpcconvert <input.xpj | directory> [more…] [options]

INPUTS:
  One or more .xpj files, or directories to scan for .xpj files.

OPTIONS:
  -o, --out <dir>        Output parent folder (default: next to each source).
  -g, --group <mode>     Pad→track grouping: rules (default) | per-pad | one.
      --track-name <s>   Track name for --group one (default: "Drums").
      --suffix <s>       Output name suffix (default: " (3.9)").
      --overwrite        Overwrite existing output folders.
  -r, --recursive        Recurse into input directories.
  -h, --help             Show this help.

EXAMPLES:
  mpcconvert "House os 01.xpj"
  mpcconvert C:\Projects --recursive --out C:\Converted --overwrite
  mpcconvert a.xpj b.xpj --group per-pad

Each source's "<Name>_[ProjectData]" sample folder must sit next to its .xpj.
Output is written as "<Name><suffix>/<Name><suffix>.xpj" plus a copied
"_[ProjectData]" folder.
""");
    }
}
