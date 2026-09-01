using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
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
    private readonly MediaPlayer _player = new();

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

    public AppSettings Settings { get; private set; } = AppSettings.Load();

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

        try
        {
            _source = ProjectReader.Open(dlg.FileName);
            _pads = PadAnalyzer.Analyze(_source.Data);

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
            Status = "Failed to open project: " + ex.Message;
            System.Windows.MessageBox.Show(ex.Message, "Open failed",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
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

    /// <summary>Auditions a pad's sample so the user can identify it.</summary>
    [RelayCommand]
    private void PlayPad(PadRowViewModel? row)
    {
        if (row is null) return;
        if (_source?.ProjectDataDir is null)
        {
            Status = "No sample folder available for playback.";
            return;
        }
        var file = row.Pad.SampleFiles.FirstOrDefault()
                   ?? (row.Pad.SampleNames.FirstOrDefault() is { } n ? n + ".wav" : null);
        if (file is null)
        {
            Status = "Pad has no sample to play.";
            return;
        }
        var path = Path.Combine(_source.ProjectDataDir, file);
        if (!File.Exists(path))
        {
            Status = "Sample not found on disk: " + file;
            return;
        }
        try
        {
            _player.Stop();
            _player.Open(new Uri(path, UriKind.Absolute));
            _player.Play();
            Status = "Playing: " + file;
        }
        catch (Exception ex)
        {
            Status = "Playback failed: " + ex.Message;
        }
    }

    /// <summary>Stops any pad currently auditioning.</summary>
    [RelayCommand]
    private void StopPlayback() => _player.Stop();

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
            IPadClassifier classifier = new RuleBasedClassifier();
            bool usedAi = false;

            if (Settings.AiEnabled)
            {
                var key = AppSettings.ResolveApiKey();
                if (!string.IsNullOrWhiteSpace(key))
                {
                    try
                    {
                        var ai = new ClaudeClassifier(key!, Settings.Model);
                        var aiResult = await ai.SuggestAsync(_pads);
                        ApplySuggestions(aiResult);
                        usedAi = true;
                        Status = "Suggested groups using Claude (" + Settings.Model + ").";
                    }
                    catch (ClassifierUnavailableException)
                    {
                        // fall back to rules below
                    }
                }
            }

            if (!usedAi)
            {
                var rules = await classifier.SuggestAsync(_pads);
                ApplySuggestions(rules);
                Status = Settings.AiEnabled
                    ? "Claude unavailable — used offline rule-based suggestions."
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
            if (string.IsNullOrWhiteSpace(destParent))
            {
                // Default: sibling of the source project folder.
                var sourceParent = Directory.GetParent(
                    Path.GetDirectoryName(Path.Combine(_source.ProjectDataDir ?? ".", "x")) ?? ".")?.FullName;
                destParent = sourceParent ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }

            var outName = _source.Name + " (3.9)";
            var warnings = new List<string>();
            var samples = Converter.ReferencedSampleFiles(project);
            var folder = ProjectWriter.Write(project, destParent!, outName, samples, overwrite: true, warnings);
            Converter.SelfCheck(folder, map, expectedEvents);

            var fullReport = new ConversionReport(
                report.TracksCreated, report.PadsPlaced, report.EventsMoved,
                samples.Count,
                report.Warnings.Concat(warnings).ToList(),
                report.Decisions);

            Status = $"Converted → {folder}";
            new ReportWindow(fullReport, folder).ShowDialog();
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
