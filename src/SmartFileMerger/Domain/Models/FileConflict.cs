namespace SmartFileMerger.Domain.Models;

/// <summary>
/// Repräsentiert eine Namenskollision zwischen Quelle und Ziel.
/// </summary>
public sealed record FileConflict(
    RelativeFilePath RelativePath,
    FileMetadata SourceFile,
    FileMetadata TargetFile)
{
    private const double IdenticalTimestampToleranceSeconds = 2.0;

    public long SizeDifferenceInBytes => SourceFile.SizeInBytes - TargetFile.SizeInBytes;

    public TimeSpan AgeDifference => SourceFile.LastWriteAtUtc - TargetFile.LastWriteAtUtc;

    /// <summary>
    /// Erkennt 100% identische Dateien anhand gleicher Bytegröße und sekundengenauem Änderungsdatum.
    /// </summary>
    public bool IsIdenticalSizeAndTimestamp =>
        SizeDifferenceInBytes == 0 && Math.Abs(AgeDifference.TotalSeconds) < IdenticalTimestampToleranceSeconds;
}
