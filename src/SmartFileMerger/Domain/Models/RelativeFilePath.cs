namespace SmartFileMerger.Domain.Models;

/// <summary>
/// Kapselt einen relativen Dateipfad und verhindert versehentliche Verwendung absoluter Pfade.
/// </summary>
public sealed record RelativeFilePath
{
    private const string TraversalSegment = "..";

    public string Value { get; }

    public RelativeFilePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relativer Pfad darf nicht leer sein.", nameof(relativePath));
        }

        var cleaned = relativePath.Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            throw new ArgumentException("Relativer Pfad darf nicht leer sein.", nameof(relativePath));
        }

        if (Path.IsPathRooted(cleaned))
        {
            throw new ArgumentException("Pfad muss relativ sein.", nameof(relativePath));
        }

        if (ContainsPathTraversal(cleaned))
        {
            throw new ArgumentException("Path traversal ('..') is not permitted.", nameof(relativePath));
        }

        Value = cleaned.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    public string FileName => Path.GetFileName(Value);

    public string Extension => Path.GetExtension(Value);

    public string FileNameWithoutExtension => Path.GetFileNameWithoutExtension(Value);

    public string? DirectoryName => Path.GetDirectoryName(Value);

    private static bool ContainsPathTraversal(string path)
    {
        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => segment.Trim() == TraversalSegment);
    }
}
