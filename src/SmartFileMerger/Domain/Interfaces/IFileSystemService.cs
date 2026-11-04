namespace SmartFileMerger.Domain.Interfaces;

using SmartFileMerger.Domain.Models;

/// <summary>
/// Abstraktion für alle Dateisystemoperationen zur vollständigen Entkopplung von physischem I/O.
/// </summary>
public interface IFileSystemService
{
    bool DirectoryExists(string path);

    bool FileExists(string path);

    void CreateDirectory(string path);

    IReadOnlyList<RelativeFilePath> GetAllRelativeFilePaths(DirectoryRoot root);

    FileMetadata GetMetadata(string fullPath);

    void EnsureDirectoryExistsForFile(string fullFilePath);

    void MoveFile(string sourceFullPath, string targetFullPath, bool overwrite = false);

    void DeleteFile(string fullPath);

    void RemoveEmptyDirectories(DirectoryRoot root);
}
