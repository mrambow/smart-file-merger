namespace SmartFileMerger.Domain.Models;

/// <summary>
/// Metadaten einer Datei für Vergleich und Visualisierung.
/// </summary>
public sealed record FileMetadata(
    string FullPath,
    long SizeInBytes,
    DateTime CreatedAtUtc,
    DateTime LastWriteAtUtc);
