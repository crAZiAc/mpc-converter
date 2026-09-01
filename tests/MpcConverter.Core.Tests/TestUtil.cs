using System;
using System.IO;

namespace MpcConverter.Core.Tests;

public static class TestUtil
{
    /// <summary>Creates a unique temp directory that is cleaned up by the OS temp sweeper.</summary>
    public static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mpcconv-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
