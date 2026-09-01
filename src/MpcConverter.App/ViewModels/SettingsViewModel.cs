using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using MpcConverter.Core.Settings;

namespace MpcConverter.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;

    public IReadOnlyList<string> Models { get; } =
        new[] { "claude-opus-5", "claude-sonnet-5", "claude-haiku-4-5" };

    [ObservableProperty] private bool _aiEnabled;
    [ObservableProperty] private string _model;
    [ObservableProperty] private string? _outputFolder;
    [ObservableProperty] private string _apiKeyStatus;

    public SettingsViewModel(AppSettings settings)
    {
        _settings = settings;
        _aiEnabled = settings.AiEnabled;
        _model = settings.Model;
        _outputFolder = settings.OutputFolder;
        _apiKeyStatus = ComputeKeyStatus();
    }

    private static string ComputeKeyStatus()
    {
        if (!OperatingSystem.IsWindows()) return "Key storage requires Windows.";
        var env = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (!string.IsNullOrWhiteSpace(env)) return "Using ANTHROPIC_API_KEY from environment.";
        return DpapiKeyStore.Load() is not null ? "A key is stored (encrypted)." : "No key stored.";
    }

    /// <summary>Persists settings; optionally stores/clears the API key. Returns true.</summary>
    public bool Save(string? newApiKey, bool clearKey)
    {
        _settings.AiEnabled = AiEnabled;
        _settings.Model = Model;
        _settings.OutputFolder = string.IsNullOrWhiteSpace(OutputFolder) ? null : OutputFolder;
        _settings.Save();

        if (OperatingSystem.IsWindows())
        {
            if (clearKey) DpapiKeyStore.Clear();
            else if (!string.IsNullOrWhiteSpace(newApiKey)) DpapiKeyStore.Save(newApiKey!);
        }
        ApiKeyStatus = ComputeKeyStatus();
        return true;
    }
}
