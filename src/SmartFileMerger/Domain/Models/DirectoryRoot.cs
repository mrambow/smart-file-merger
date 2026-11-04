namespace SmartFileMerger.Domain.Models;

/// <summary>
/// Kapselt einen normalisierten, absoluten Verzeichnispfad und prüft Gültigkeit.
/// </summary>
public sealed record DirectoryRoot
{
    public string FullPath { get; }

    public DirectoryRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Pfad darf nicht leer sein.", nameof(path));
        }

        var cleaned = path.Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            throw new ArgumentException("Pfad darf nicht leer sein.", nameof(path));
        }

        var full = Path.GetFullPath(cleaned);
        var root = Path.GetPathRoot(full);
        FullPath = IsRootDirectory(full, root)
            ? EnsureTrailingSeparator(root!)
            : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public string CombineWith(RelativeFilePath relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        return Path.Combine(FullPath, relativePath.Value);
    }

    public bool IsSubdirectoryOf(DirectoryRoot potentialParent)
    {
        ArgumentNullException.ThrowIfNull(potentialParent);

        var parentPath = EnsureTrailingSeparator(potentialParent.FullPath);
        var thisPath = EnsureTrailingSeparator(FullPath);
        return thisPath.StartsWith(parentPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRootDirectory(string fullPath, string? root)
    {
        if (root is null)
        {
            return false;
        }

        var trimmedFull = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var trimmedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(trimmedFull, trimmedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }
}
