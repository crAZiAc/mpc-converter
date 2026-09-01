using System;
using System.IO;
using MpcConverter.Core.Acvs;

namespace MpcConverter.Core.ProjectIo;

public static class ProjectReader
{
    /// <summary>
    /// Opens an MPC project. <paramref name="path"/> may be the project folder
    /// (which contains an extension-less inner file named the same as the folder)
    /// or the inner file itself.
    /// </summary>
    public static MpcProject Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string name;
        AcvsDocument doc;
        // The folder that the "<Name>_[ProjectData]" sample dir sits beside.
        string sampleSearchDir;

        if (File.Exists(path) && Directory.Exists(Path.GetDirectoryName(path)))
        {
            // Pointed at a file directly: either the extension-less inner file or a .xpj.
            (name, doc) = ReadFile(path);
            sampleSearchDir = Path.GetDirectoryName(Path.GetDirectoryName(path)!)!;
        }
        else if (Directory.Exists(path))
        {
            name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var innerFile = Path.Combine(path, name);
            if (File.Exists(innerFile))
            {
                (name, doc) = ReadFile(innerFile);
            }
            else
            {
                // Fallback 1: a single extension-less inner file in the folder.
                var candidate = FindInnerFile(path);
                // Fallback 2: a .xpj (gzip) copy in the folder.
                candidate ??= Directory.EnumerateFiles(path, "*.xpj").FirstOrDefault();
                if (candidate is null)
                    throw new FileNotFoundException(
                        $"No MPC project file (inner file or .xpj) found in '{path}'.");
                (name, doc) = ReadFile(candidate);
            }
            // Samples may sit inside the folder OR beside it.
            sampleSearchDir = path;
        }
        else
        {
            throw new FileNotFoundException($"Project path not found: '{path}'.");
        }

        string? projectData = FindProjectData(sampleSearchDir, name)
            ?? FindProjectData(Path.GetDirectoryName(sampleSearchDir), name);

        return new MpcProject
        {
            Name = name,
            Document = doc,
            ProjectDataDir = projectData,
        };
    }

    /// <summary>Reads either an extension-less inner ACVS file or a gzipped .xpj.</summary>
    private static (string Name, AcvsDocument Doc) ReadFile(string file)
    {
        var bytes = File.ReadAllBytes(file);
        if (string.Equals(Path.GetExtension(file), ".xpj", StringComparison.OrdinalIgnoreCase))
            bytes = XpjFile.Decompress(bytes);
        var doc = AcvsDocument.Parse(bytes);
        var name = Path.GetFileNameWithoutExtension(file);
        return (name, doc);
    }

    private static string? FindProjectData(string? dir, string name)
    {
        if (dir is null) return null;
        var pd = Path.Combine(dir, name + "_[ProjectData]");
        return Directory.Exists(pd) ? pd : null;
    }

    private static string? FindInnerFile(string folder)
    {
        foreach (var f in Directory.EnumerateFiles(folder))
        {
            if (string.IsNullOrEmpty(Path.GetExtension(f)))
                return f;
        }
        return null;
    }
}
