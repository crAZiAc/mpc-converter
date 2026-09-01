using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace MpcConverter.Core.Settings;

/// <summary>
/// Stores the Claude API key encrypted at rest using Windows DPAPI (per-user
/// scope). The plaintext never touches disk or git.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DpapiKeyStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MpcConverter.ApiKey.v1");

    private static string KeyFile =>
        Path.Combine(AppSettings.SettingsDir, "apikey.bin");

    public static void Save(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        Directory.CreateDirectory(AppSettings.SettingsDir);
        var cipher = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(apiKey), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(KeyFile, cipher);
    }

    public static string? Load()
    {
        if (!File.Exists(KeyFile)) return null;
        try
        {
            var cipher = File.ReadAllBytes(KeyFile);
            var plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            return null; // corrupt or from a different user
        }
    }

    public static void Clear()
    {
        if (File.Exists(KeyFile)) File.Delete(KeyFile);
    }
}
