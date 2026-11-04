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
        FullPath = Path.GetFullPath(cleaned).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public string CombineWith(RelativeFilePath relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        return Path.Combine(FullPath, relativePath.Value);
    }

    public bool IsSubdirectoryOf(DirectoryRoot potentialParent)
    {
        ArgumentNullException.ThrowIfNull(potentialParent);

        var parentPath = potentialParent.FullPath + Path.DirectorySeparatorChar;
        var thisPath = FullPath + Path.DirectorySeparatorChar;
        return thisPath.StartsWith(parentPath, StringComparison.OrdinalIgnoreCase);
    }
}
