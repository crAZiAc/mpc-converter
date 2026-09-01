using System;
using System.IO;
using System.Runtime.InteropServices;
using MpcConverter.Core.Settings;
using Xunit;

namespace MpcConverter.Core.Tests;

public class SettingsTests
{
    private static IDisposable IsolatedSettingsDir(out string dir)
    {
        dir = TestUtil.TempDir();
        Environment.SetEnvironmentVariable("MPCCONVERTER_SETTINGS_DIR", dir);
        return new Cleanup(() => Environment.SetEnvironmentVariable("MPCCONVERTER_SETTINGS_DIR", null));
    }

    [Fact]
    public void AppSettings_RoundTrips()
    {
        using var scope = IsolatedSettingsDir(out _);
        var s = new AppSettings { AiEnabled = true, Model = "claude-sonnet-5", OutputFolder = @"C:\out" };
        s.Save();
        var back = AppSettings.Load();
        Assert.True(back.AiEnabled);
        Assert.Equal("claude-sonnet-5", back.Model);
        Assert.Equal(@"C:\out", back.OutputFolder);
    }

    [Fact]
    public void DpapiKeyStore_SaveLoadClear_RoundTrips()
    {
        if (!OperatingSystem.IsWindows()) return; // DPAPI is Windows-only
        using var scope = IsolatedSettingsDir(out _);
        DpapiKeyStore.Save("sk-ant-secret-123");
        Assert.Equal("sk-ant-secret-123", DpapiKeyStore.Load());
        DpapiKeyStore.Clear();
        Assert.Null(DpapiKeyStore.Load());
    }

    [Fact]
    public void DpapiKeyStore_CiphertextIsNotPlaintext()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var _ = IsolatedSettingsDir(out var dir);
        DpapiKeyStore.Save("sk-ant-secret-123");
        var bytes = File.ReadAllBytes(Path.Combine(dir, "apikey.bin"));
        var asText = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("sk-ant-secret-123", asText);
    }

    [Fact]
    public void ResolveApiKey_EnvOverridesStored()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var scope = IsolatedSettingsDir(out _);
        DpapiKeyStore.Save("stored-key");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "env-key");
        try
        {
            Assert.Equal("env-key", AppSettings.ResolveApiKey());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
        }
        Assert.Equal("stored-key", AppSettings.ResolveApiKey());
    }

    private sealed class Cleanup : IDisposable
    {
        private readonly Action _action;
        public Cleanup(Action action) => _action = action;
        public void Dispose() => _action();
    }
}
