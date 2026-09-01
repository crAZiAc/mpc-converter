using System;
using System.IO;

namespace MpcConverter.Core.Tests;

/// <summary>
/// Resolves the reference-project fixtures from the test project's source
/// directory (walking up from the build output), so the large .wav samples
/// don't need to be copied into bin/.
/// </summary>
public static class FixturePaths
{
    private static string FixturesDir { get; } = Locate();

    public static string HouseFolder => Path.Combine(FixturesDir, "House os 01");
    public static string HouseInnerFile => Path.Combine(HouseFolder, "House os 01");
    public static string HouseXpj => Path.Combine(FixturesDir, "House os 01.xpj");
    public static string HouseProjectData => Path.Combine(FixturesDir, "House os 01_[ProjectData]");

    public static string KeysFolder => Path.Combine(FixturesDir, "Keys v1");
    public static string KeysInnerFile => Path.Combine(KeysFolder, "Keys v1");
    public static string KeysXpj => Path.Combine(FixturesDir, "Keys v1.xpj");
    public static string KeysProjectData => Path.Combine(FixturesDir, "Keys v1_[ProjectData]");

    private static string Locate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Fixtures");
            if (File.Exists(Path.Combine(dir.FullName, "MpcConverter.Core.Tests.csproj")) &&
                Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate the test Fixtures directory from " + AppContext.BaseDirectory);
    }
}
