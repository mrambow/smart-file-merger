namespace SmartFileMerger.Infrastructure;

using System.IO;
using SmartFileMerger.Domain.Interfaces;
using SmartFileMerger.Domain.Models;

/// <summary>
/// Konkrete Implementierung des <see cref="IFileSystemService"/> für Windows-Dateisysteme.
/// Berücksichtigt Windows-Besonderheiten wie ReadOnly-/Hidden-Attribute, plattformspezifische Zeitstempel und Dateisperren.
/// </summary>
public sealed class WindowsFileSystemService : IFileSystemService
{
    public bool DirectoryExists(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Directory.Exists(path);
    }

    public bool FileExists(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return File.Exists(path);
    }

    public void CreateDirectory(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Directory.CreateDirectory(path);
    }

    public IReadOnlyList<RelativeFilePath> GetAllRelativeFilePaths(DirectoryRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (!Directory.Exists(root.FullPath))
        {
            return Array.Empty<RelativeFilePath>();
        }

        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true
        };

        return Directory.EnumerateFiles(root.FullPath, "*", enumerationOptions)
            .Select(filePath => new RelativeFilePath(Path.GetRelativePath(root.FullPath, filePath)))
            .ToList();
    }

    public FileMetadata GetMetadata(string fullPath)
    {
        ArgumentNullException.ThrowIfNull(fullPath);

        var fileInfo = new FileInfo(fullPath);
        return new FileMetadata(
            fileInfo.FullName,
            fileInfo.Length,
            fileInfo.CreationTimeUtc,
            fileInfo.LastWriteTimeUtc);
    }

    public void EnsureDirectoryExistsForFile(string fullFilePath)
    {
        ArgumentNullException.ThrowIfNull(fullFilePath);

        var directory = Path.GetDirectoryName(fullFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public void MoveFile(string sourceFullPath, string targetFullPath, bool overwrite = false)
    {
        ArgumentNullException.ThrowIfNull(sourceFullPath);
        ArgumentNullException.ThrowIfNull(targetFullPath);

        EnsureDirectoryExistsForFile(targetFullPath);

        if (File.Exists(sourceFullPath))
        {
            StripRestrictingAttributes(sourceFullPath);
        }

        if (overwrite && File.Exists(targetFullPath))
        {
            StripRestrictingAttributes(targetFullPath);
        }

        var creationTime = File.GetCreationTimeUtc(sourceFullPath);
        var lastWriteTime = File.GetLastWriteTimeUtc(sourceFullPath);

        File.Move(sourceFullPath, targetFullPath, overwrite);

        RestoreTimestamps(targetFullPath, creationTime, lastWriteTime);
    }

    public void CopyFile(string sourceFullPath, string targetFullPath, bool overwrite = false)
    {
        ArgumentNullException.ThrowIfNull(sourceFullPath);
        ArgumentNullException.ThrowIfNull(targetFullPath);

        EnsureDirectoryExistsForFile(targetFullPath);

        if (overwrite && File.Exists(targetFullPath))
        {
            StripRestrictingAttributes(targetFullPath);
        }

        File.Copy(sourceFullPath, targetFullPath, overwrite);
    }

    public void StripReadOnlyAttribute(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (!File.Exists(filePath))
        {
            return;
        }

        StripRestrictingAttributes(filePath);
    }

    public void DeleteFile(string fullPath)
    {
        ArgumentNullException.ThrowIfNull(fullPath);

        if (!File.Exists(fullPath))
        {
            return;
        }

        StripRestrictingAttributes(fullPath);
        File.Delete(fullPath);
    }

    public void RemoveEmptyDirectories(DirectoryRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (!Directory.Exists(root.FullPath))
        {
            return;
        }

        CleanEmptyDirectoriesRecursively(root.FullPath, root.FullPath);
    }

    private static void StripRestrictingAttributes(string filePath)
    {
        var attributes = File.GetAttributes(filePath);
        var updated = attributes & ~FileAttributes.ReadOnly & ~FileAttributes.Hidden;

        if (updated == 0)
        {
            updated = FileAttributes.Normal;
        }

        if (updated != attributes)
        {
            File.SetAttributes(filePath, updated);
        }
    }

    private static void RestoreTimestamps(string targetFullPath, DateTime creationTimeUtc, DateTime lastWriteTimeUtc)
    {
        try
        {
            File.SetCreationTimeUtc(targetFullPath, creationTimeUtc);
            File.SetLastWriteTimeUtc(targetFullPath, lastWriteTimeUtc);
        }
        catch
        {
            // Timestamp restoration failure (e.g. restricted permissions or remote volume) does not invalidate move
        }
    }

    private static void CleanEmptyDirectoriesRecursively(string currentDir, string rootDir)
    {
        try
        {
            if (Directory.Exists(currentDir))
            {
                DeleteEmptySubdirectories(currentDir, rootDir);
                TryDeleteDirectoryIfEmpty(currentDir, rootDir);
            }
        }
        catch
        {
            // Ignore directories that cannot be accessed or deleted (e.g. locked or restricted permissions)
        }
    }

    private static void DeleteEmptySubdirectories(string currentDir, string rootDir)
    {
        foreach (var subDir in Directory.EnumerateDirectories(currentDir))
        {
            CleanEmptyDirectoriesRecursively(subDir, rootDir);
        }
    }

    private static void TryDeleteDirectoryIfEmpty(string currentDir, string rootDir)
    {
        if (IsSameOrRootDirectory(currentDir, rootDir))
        {
            return;
        }

        if (!Directory.EnumerateFileSystemEntries(currentDir).Any())
        {
            Directory.Delete(currentDir, recursive: false);
        }
    }

    private static bool IsSameOrRootDirectory(string currentDir, string rootDir)
    {
        var normalizedCurrent = Path.GetFullPath(currentDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(rootDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(normalizedCurrent, normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
