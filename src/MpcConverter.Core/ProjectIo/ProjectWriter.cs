using System;
using System.Collections.Generic;
using System.IO;
using MpcConverter.Core.Acvs;

namespace MpcConverter.Core.ProjectIo;

public static class ProjectWriter
{
    /// <summary>
    /// Writes a project as a self-contained MPC project folder. Two layouts:
    /// <list type="bullet">
    /// <item><b>Packaged (default)</b>: <c>&lt;destParent&gt;/&lt;name&gt;/</c> containing
    /// <c>&lt;name&gt;.xpj</c> (the gzipped project) and <c>&lt;name&gt;_[ProjectData]/</c>
    /// (the samples) — matching how MPC stores projects.</item>
    /// <item><b>Unpacked</b> (<paramref name="packageAsXpj"/> = false): also writes the
    /// uncompressed inner ACVS file <c>&lt;name&gt;/&lt;name&gt;</c> alongside the .xpj.</item>
    /// </list>
    /// Returns the path to the created project folder.
    /// </summary>
    /// <param name="sampleFileNames">
    /// Sample file names (as stored in the project's <c>samples[].path</c>) to copy
    /// from <c>project.ProjectDataDir</c>. Missing files are skipped and reported.
    /// </param>
    public static string Write(
        MpcProject project,
        string destParent,
        string name,
        IEnumerable<string> sampleFileNames,
        bool overwrite,
        IList<string>? warnings = null,
        bool packageAsXpj = true)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(destParent);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var projectFolder = Path.Combine(destParent, name);
        var xpjFile = Path.Combine(projectFolder, name + ".xpj");
        var projectDataDir = Path.Combine(projectFolder, name + "_[ProjectData]");

        if (Directory.Exists(projectFolder))
        {
            if (!overwrite)
                throw new IOException($"Output already exists: '{projectFolder}'. Enable overwrite.");
            Directory.Delete(projectFolder, recursive: true);
        }

        Directory.CreateDirectory(projectFolder);
        Directory.CreateDirectory(projectDataDir);

        var bytes = project.Document.ToBytes();

        // The .xpj (gzip) is the project file MPC loads.
        File.WriteAllBytes(xpjFile, XpjFile.Compress(bytes));

        // Optionally also emit the uncompressed inner file (folder-named same).
        if (!packageAsXpj)
            File.WriteAllBytes(Path.Combine(projectFolder, name), bytes);

        CopySamples(project, projectDataDir, sampleFileNames, warnings);

        return projectFolder;
    }

    private static void CopySamples(
        MpcProject project, string projectDataDir,
        IEnumerable<string> sampleFileNames, IList<string>? warnings)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sample in sampleFileNames)
        {
            if (string.IsNullOrWhiteSpace(sample) || !seen.Add(sample)) continue;
            if (project.ProjectDataDir is null)
            {
                warnings?.Add($"No source ProjectData folder; sample not copied: {sample}");
                continue;
            }
            var src = Path.Combine(project.ProjectDataDir, sample);
            if (!File.Exists(src))
            {
                warnings?.Add($"Sample missing on disk, not copied: {sample}");
                continue;
            }
            try
            {
                File.Copy(src, Path.Combine(projectDataDir, sample), overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // e.g. the file is locked (open in MPC) or read-protected. Don't abort
                // the whole conversion for one uncopyable sample.
                warnings?.Add($"Could not copy sample (in use or locked): {sample} — {ex.Message}");
            }
        }
    }
}
