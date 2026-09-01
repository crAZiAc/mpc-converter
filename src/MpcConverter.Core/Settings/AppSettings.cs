using System;
using System.IO;
using System.Runtime.Versioning;
using System.Text.Json;

namespace MpcConverter.Core.Settings;

/// <summary>User-level app settings, persisted as JSON under %APPDATA%/MpcConverter.</summary>
public sealed class AppSettings
{
    public bool AiEnabled { get; set; }
    public string Model { get; set; } = "claude-opus-5";
    public string? OutputFolder { get; set; }

    // Tests set MPCCONVERTER_SETTINGS_DIR to isolate from the real user profile.
    public static string SettingsDir
    {
        get
        {
            var overrideDir = Environment.GetEnvironmentVariable("MPCCONVERTER_SETTINGS_DIR");
            return !string.IsNullOrWhiteSpace(overrideDir)
                ? overrideDir
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MpcConverter");
        }
    }

    private static string SettingsFile => Path.Combine(SettingsDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // fall through to defaults
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        File.WriteAllText(SettingsFile,
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Resolves the effective API key: the ANTHROPIC_API_KEY environment variable
    /// takes precedence over the stored key.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static string? ResolveApiKey()
    {
        var env = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        return !string.IsNullOrWhiteSpace(env) ? env : DpapiKeyStore.Load();
    }
}
