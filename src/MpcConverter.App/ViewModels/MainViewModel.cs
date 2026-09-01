using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MpcConverter.App.Views;
using MpcConverter.Core.Analysis;
using MpcConverter.Core.Classification;
using MpcConverter.Core.Conversion;
using MpcConverter.Core.Model;
using MpcConverter.Core.ProjectIo;
using MpcConverter.Core.Settings;

namespace MpcConverter.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private MpcProject? _source;
    private IReadOnlyList<PadInfo> _pads = Array.Empty<PadInfo>();
    private readonly MediaPlayer _previewPlayer = new();
    private readonly Dictionary<string, string> _sampleFilesByName = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<PadRowViewModel> Pads { get; } = new();

    /// <summary>Distinct destination track names currently in use (for the combo dropdown).</summary>
    public ObservableCollection<string> TrackNameOptions { get; } = new();

    [ObservableProperty] private string _projectName = "(no project loaded)";
    [ObservableProperty] private string _formatVersion = "";
    [ObservableProperty] private string _status = "Open a Sample-format MPC project to begin.";
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    [NotifyCanExecuteChangedFor(nameof(SuggestCommand))]
    [NotifyCanExecuteChangedFor(nameof(OneTrackPerPadCommand))]
    [NotifyCanExecuteChangedFor(nameof(AllToOneCommand))]
    private bool _projectLoaded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopPlaybackCommand))]
    private bool _isPreviewPlaying;

    public AppSettings Settings { get; private set; } = AppSettings.Load();

    public MainViewModel()
    {
        _previewPlayer.MediaEnded += (_, _) => IsPreviewPlaying = false;
        _previewPlayer.MediaFailed += OnPreviewFailed;
    }

    [RelayCommand]
    private void OpenProject()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select an MPC project (.xpj)",
            Filter = "MPC project (*.xpj)|*.xpj",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() != true) return;
        LoadProject(dlg.FileName);
    }

    /// <summary>
    /// Opens a project from a path (an .xpj file or a project folder). Shared by the
    /// Open dialog and by drag-and-drop onto the window.
    /// </summary>
    public void LoadProject(string path)
    {
        try
        {
            StopPlayback();

            _source = ProjectReader.Open(path);
            _pads = PadAnalyzer.Analyze(_source.Data);
            BuildSampleFileIndex();

            ProjectName = _source.Name;
            FormatVersion = _source.Document.FormatVersion;

            foreach (var existing in Pads)
                existing.PropertyChanged -= OnPadRowChanged;
            Pads.Clear();
            foreach (var pad in _pads)
            {
                var row = new PadRowViewModel(pad);
                row.PropertyChanged += OnPadRowChanged;
                Pads.Add(row);
            }

            ApplyOneTrackPerPad(); // sensible default mapping
            ProjectLoaded = true;
            Status = $"Loaded '{_source.Name}' ({FormatVersion}) — {_pads.Count} sampled pads.";
        }
        catch (Exception ex)
        {
            ProjectLoaded = false;
            _sampleFilesByName.Clear();
            Status = "Failed to open project: " + ex.Message;
            System.Windows.MessageBox.Show(ex.Message, "Open failed",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(ProjectLoaded))]
    private void PlayPad(PadRowViewModel? row)
    {
        if (row is null || _source is null)
            return;

        var samplePath = ResolvePreviewSamplePath(row.Pad);
        if (samplePath is null)
        {
            Status = $"No playable sample file found for pad {row.PadIndex}.";
            return;
        }

        try
        {
            _previewPlayer.Stop();
            _previewPlayer.Open(new Uri(samplePath, UriKind.Absolute));
            _previewPlayer.Position = TimeSpan.Zero;
            _previewPlayer.Play();
            IsPreviewPlaying = true;
            Status = $"Previewing pad {row.PadIndex}: {Path.GetFileName(samplePath)}";
        }
        catch (Exception ex)
        {
            IsPreviewPlaying = false;
            Status = "Preview failed: " + ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(IsPreviewPlaying))]
    private void StopPlayback()
    {
        _previewPlayer.Stop();
        IsPreviewPlaying = false;
    }

    [RelayCommand(CanExecute = nameof(ProjectLoaded))]
    private void OneTrackPerPad() => ApplyOneTrackPerPad();

    private void ApplyOneTrackPerPad()
    {
        var map = PadTrackMap.OneTrackPerPad(_pads);
        AssignFromMap(map);
        Status = "Applied: one track per pad.";
    }

    [RelayCommand(CanExecute = nameof(ProjectLoaded))]
    private void AllToOne()
    {
        foreach (var row in Pads) row.DestTrackName = "Drums";
        RefreshTrackOptions();
        Status = "Applied: all pads to one 'Drums' track.";
    }

    /// <summary>Assigns the given rows to one destination track (a combine-group).</summary>
    public void GroupRows(IEnumerable<PadRowViewModel> rows, string trackName)
    {
        foreach (var row in rows)
            row.DestTrackName = trackName;
        RefreshTrackOptions();
        Status = $"Grouped selected pads onto '{trackName}'.";
    }

    /// <summary>Excludes the given rows from the conversion.</summary>
    public void SkipRows(IEnumerable<PadRowViewModel> rows)
    {
        int n = 0;
        foreach (var row in rows) { row.DestTrackName = "(skip)"; n++; }
        RefreshTrackOptions();
        Status = $"Skipping {n} selected pad(s).";
    }

    [RelayCommand(CanExecute = nameof(ProjectLoaded))]
    private async Task SuggestAsync()
    {
        IsBusy = true;
        try
        {
            Status = "Preparing pad-group suggestions...";

            IPadClassifier classifier = new RuleBasedClassifier();
            bool usedAi = false;
            string? aiUnavailableReason = null;

            if (Settings.AiEnabled)
            {
                var key = AppSettings.ResolveApiKey();
                if (!string.IsNullOrWhiteSpace(key))
                {
                    try
                    {
                        Status = $"Calling Claude ({Settings.Model}) for {_pads.Count} pad(s)...";
                        var ai = new ClaudeClassifier(key!, Settings.Model);
                        var aiResult = await ai.SuggestAsync(_pads);
                        ApplySuggestions(aiResult);
                        usedAi = true;
                        Status = "Suggested groups using Claude (" + Settings.Model + ").";
                    }
                    catch (ClassifierUnavailableException ex)
                    {
                        aiUnavailableReason = ex.Message;
                        Status = "Claude unavailable — falling back to offline rules...";
                    }
                }
                else
                {
                    aiUnavailableReason = "No API key configured.";
                    Status = "AI is enabled but no API key is configured — using offline rules...";
                }
            }

            if (!usedAi)
            {
                if (!Settings.AiEnabled)
                    Status = "Running offline rule-based suggestions...";

                var rules = await classifier.SuggestAsync(_pads);
                ApplySuggestions(rules);
                Status = Settings.AiEnabled
                    ? "Claude unavailable"
                        + (string.IsNullOrWhiteSpace(aiUnavailableReason) ? "" : $" ({aiUnavailableReason})")
                        + " — used offline rule-based suggestions."
                    : "Suggested groups using offline rules.";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplySuggestions(IReadOnlyList<PadSuggestion> suggestions)
    {
        var byPad = suggestions.ToDictionary(s => s.PadIndex);
        foreach (var row in Pads)
        {
            if (byPad.TryGetValue(row.PadIndex, out var s))
            {
                row.DestTrackName = s.TrackName;
                row.SuggestionReason = s.Reason is null
                    ? $"confidence {s.Confidence:P0}"
                    : $"{s.Reason} (confidence {s.Confidence:P0})";
                row.LowConfidence = s.Confidence < 0.5;
            }
        }
        RefreshTrackOptions();
    }

    private void AssignFromMap(PadTrackMap map)
    {
        foreach (var row in Pads)
            row.DestTrackName = map.TrackOf(row.PadIndex);
        RefreshTrackOptions();
    }

    // When the user types/commits a destination name on any row, make it available
    // in every row's dropdown.
    private void OnPadRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PadRowViewModel.DestTrackName))
            RefreshTrackOptions();
    }

    private void RefreshTrackOptions()
    {
        // Sync in place — never Clear() — so the ItemsSource of the row combos keeps
        // the value the user just chose. See TrackOptionsSync for the reasoning.
        MpcConverter.Core.Model.TrackOptionsSync.Sync(
            TrackNameOptions, Pads.Select(p => p.DestTrackName));
    }

    [RelayCommand(CanExecute = nameof(ProjectLoaded))]
    private void Convert()
    {
        if (_source is null) return;
        try
        {
            var assignments = Pads.ToDictionary(
                p => p.PadIndex,
                p => string.IsNullOrWhiteSpace(p.DestTrackName) || p.DestTrackName == "(skip)"
                    ? null
                    : p.DestTrackName);

            var map = PadTrackMap.FromAssignments(_pads, assignments);
            map.Validate();

            int expectedEvents = CountSourceNoteEvents();

            var (project, report) = Converter.Convert(_source, map);

            var destParent = Settings.OutputFolder;
            var flatOutput = !string.IsNullOrWhiteSpace(destParent);
            if (string.IsNullOrWhiteSpace(destParent))
            {
                // Default: sibling of the source project folder.
                var sourceParent = Directory.GetParent(
                    Path.GetDirectoryName(Path.Combine(_source.ProjectDataDir ?? ".", "x")) ?? ".")?.FullName;
                destParent = sourceParent ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }

            var outName = _source.Name + " (converted)";
            var warnings = new List<string>();
            var samples = Converter.ReferencedSampleFiles(project);
            var writtenProject = ProjectWriter.Write(
                project, destParent!, outName, samples, overwrite: true, warnings, flatOutput: flatOutput);
            Converter.SelfCheck(writtenProject, map, expectedEvents);

            var fullReport = new ConversionReport(
                report.TracksCreated, report.PadsPlaced, report.EventsMoved,
                samples.Count,
                report.Warnings.Concat(warnings).ToList(),
                report.Decisions);

            Status = $"Converted → {writtenProject}";
            new ReportWindow(fullReport, writtenProject).ShowDialog();
        }
        catch (Exception ex)
        {
            Status = "Conversion failed: " + ex.Message;
            System.Windows.MessageBox.Show(ex.Message, "Conversion failed",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private int CountSourceNoteEvents()
    {
        if (_source is null) return 0;
        var drumTrack = MpcJson.FindDrumTrack(_source.Data);
        var name = (string?)drumTrack?["name"] ?? "";
        int total = 0;
        foreach (var seq in _source.Data["sequences"]!.AsArray())
        {
            if (seq is null) continue;
            foreach (var (key, clip) in MpcJson.EnumerateClips(seq["value"]!.AsObject()))
                if (key == name)
                    total += MpcJson.NoteEvents(clip).Count();
        }
        return total;
    }

    private void BuildSampleFileIndex()
    {
        _sampleFilesByName.Clear();

        var projectDataDir = _source?.ProjectDataDir;
        if (string.IsNullOrWhiteSpace(projectDataDir) || !Directory.Exists(projectDataDir))
            return;

        foreach (var file in Directory.EnumerateFiles(projectDataDir, "*", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(file);
            if (!string.IsNullOrWhiteSpace(fileName))
                _sampleFilesByName.TryAdd(fileName, file);

            var noExt = Path.GetFileNameWithoutExtension(file);
            if (!string.IsNullOrWhiteSpace(noExt))
                _sampleFilesByName.TryAdd(noExt, file);

            var rel = Path.GetRelativePath(projectDataDir, file).Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(rel))
                _sampleFilesByName.TryAdd(rel, file);
        }
    }

    private string? ResolvePreviewSamplePath(PadInfo pad)
    {
        var projectDataDir = _source?.ProjectDataDir;
        var tokens = pad.SampleFiles.Concat(pad.SampleNames);

        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token)) continue;

            if (Path.IsPathRooted(token) && File.Exists(token))
                return token;

            if (string.IsNullOrWhiteSpace(projectDataDir)) continue;

            var normalized = token.Replace('/', Path.DirectorySeparatorChar);
            var combined = Path.Combine(projectDataDir, normalized);
            if (File.Exists(combined))
                return combined;

            var byName = Path.Combine(projectDataDir, Path.GetFileName(normalized));
            if (File.Exists(byName))
                return byName;

            if (_sampleFilesByName.TryGetValue(token, out var indexed) && File.Exists(indexed))
                return indexed;

            var slash = normalized.Replace('\\', '/');
            if (_sampleFilesByName.TryGetValue(slash, out indexed) && File.Exists(indexed))
                return indexed;

            var fileName = Path.GetFileName(normalized);
            if (!string.IsNullOrWhiteSpace(fileName) &&
                _sampleFilesByName.TryGetValue(fileName, out indexed) &&
                File.Exists(indexed))
                return indexed;

            var bare = Path.GetFileNameWithoutExtension(normalized);
            if (!string.IsNullOrWhiteSpace(bare) &&
                _sampleFilesByName.TryGetValue(bare, out indexed) &&
                File.Exists(indexed))
                return indexed;
        }

        return null;
    }

    private void OnPreviewFailed(object? sender, ExceptionEventArgs e)
    {
        IsPreviewPlaying = false;
        Status = "Preview failed: " + (e.ErrorException?.Message ?? "Unknown media error.");
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var vm = new SettingsViewModel(Settings);
        var win = new SettingsWindow(vm);
        if (win.ShowDialog() == true)
        {
            Settings = AppSettings.Load();
        }
    }
}
