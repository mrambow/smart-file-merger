namespace SmartFileMerger.Tests.Infrastructure;

using System.IO;
using FluentAssertions;
using SmartFileMerger.Domain.Models;
using SmartFileMerger.Infrastructure;
using Xunit;

public sealed class WindowsFileSystemServiceIntegrationTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly WindowsFileSystemService _sut;

    public WindowsFileSystemServiceIntegrationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SmartFileMergerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
        _sut = new WindowsFileSystemService();
    }

    public void Dispose()
    {
        if (!Directory.Exists(_testDirectory))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(_testDirectory, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_testDirectory, recursive: true);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public void DirectoryExists_WhenDirectoryExists_ReturnsTrue()
    {
        var result = _sut.DirectoryExists(_testDirectory);

        result.Should().BeTrue();
    }

    [Fact]
    public void DirectoryExists_WhenDirectoryDoesNotExist_ReturnsFalse()
    {
        var nonExistent = Path.Combine(_testDirectory, "non_existent_" + Guid.NewGuid().ToString("N"));

        var result = _sut.DirectoryExists(nonExistent);

        result.Should().BeFalse();
    }

    [Fact]
    public void FileExists_WhenFileExists_ReturnsTrue()
    {
        var filePath = Path.Combine(_testDirectory, "test.txt");
        File.WriteAllText(filePath, "content");

        var result = _sut.FileExists(filePath);

        result.Should().BeTrue();
    }

    [Fact]
    public void FileExists_WhenFileDoesNotExist_ReturnsFalse()
    {
        var nonExistent = Path.Combine(_testDirectory, "non_existent.txt");

        var result = _sut.FileExists(nonExistent);

        result.Should().BeFalse();
    }

    [Fact]
    public void CreateDirectory_WhenCalled_CreatesDirectory()
    {
        var targetDir = Path.Combine(_testDirectory, "new_folder", "nested");

        _sut.CreateDirectory(targetDir);

        Directory.Exists(targetDir).Should().BeTrue();
    }

    [Fact]
    public void GetAllRelativeFilePaths_WhenFilesExist_ReturnsAllRelativePathsRecursively()
    {
        var root = new DirectoryRoot(_testDirectory);
        var subDir = Path.Combine(_testDirectory, "sub");
        Directory.CreateDirectory(subDir);

        var file1 = Path.Combine(_testDirectory, "root_file.txt");
        var file2 = Path.Combine(subDir, "nested_file.txt");
        File.WriteAllText(file1, "1");
        File.WriteAllText(file2, "2");

        var results = _sut.GetAllRelativeFilePaths(root);

        results.Should().HaveCount(2);
        results.Select(r => r.Value).Should().Contain("root_file.txt");
        results.Select(r => r.Value).Should().Contain(Path.Combine("sub", "nested_file.txt"));
    }

    [Fact]
    public void GetAllRelativeFilePaths_WhenDirectoryDoesNotExist_ReturnsEmptyList()
    {
        var nonExistentRoot = new DirectoryRoot(Path.Combine(_testDirectory, "missing"));

        var results = _sut.GetAllRelativeFilePaths(nonExistentRoot);

        results.Should().BeEmpty();
    }

    [Fact]
    public void GetMetadata_WhenFileExists_ReturnsAccurateMetadata()
    {
        var filePath = Path.Combine(_testDirectory, "meta_test.txt");
        File.WriteAllText(filePath, "Hello World");

        var metadata = _sut.GetMetadata(filePath);

        metadata.FullPath.Should().Be(filePath);
        metadata.SizeInBytes.Should().Be(11);
        metadata.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        metadata.LastWriteAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void EnsureDirectoryExistsForFile_WhenParentDirectoryDoesNotExist_CreatesParentDirectory()
    {
        var filePath = Path.Combine(_testDirectory, "deep", "nested", "target_file.txt");

        _sut.EnsureDirectoryExistsForFile(filePath);

        var parent = Path.GetDirectoryName(filePath);
        Directory.Exists(parent).Should().BeTrue();
    }

    [Fact]
    public void MoveFile_WhenSourceIsReadOnly_MovesSuccessfullyWithoutThrowing()
    {
        var sourcePath = Path.Combine(_testDirectory, "source_readonly.txt");
        var targetPath = Path.Combine(_testDirectory, "target_readonly.txt");
        File.WriteAllText(sourcePath, "source readonly content");
        File.SetAttributes(sourcePath, FileAttributes.ReadOnly);

        var act = () => _sut.MoveFile(sourcePath, targetPath, overwrite: false);

        act.Should().NotThrow();
        File.Exists(sourcePath).Should().BeFalse();
        File.Exists(targetPath).Should().BeTrue();
        File.ReadAllText(targetPath).Should().Be("source readonly content");
    }

    [Fact]
    public void MoveFile_WhenTargetIsReadOnlyAndOverwriteIsTrue_OverwritesSuccessfully()
    {
        var sourcePath = Path.Combine(_testDirectory, "source.txt");
        var targetPath = Path.Combine(_testDirectory, "target.txt");
        File.WriteAllText(sourcePath, "source content");
        File.WriteAllText(targetPath, "target content");

        File.SetAttributes(targetPath, FileAttributes.ReadOnly);

        var act = () => _sut.MoveFile(sourcePath, targetPath, overwrite: true);

        act.Should().NotThrow();
        File.Exists(sourcePath).Should().BeFalse();
        File.Exists(targetPath).Should().BeTrue();
        File.ReadAllText(targetPath).Should().Be("source content");
    }

    [Fact]
    public void MoveFile_WhenTargetIsHiddenAndOverwriteIsTrue_OverwritesSuccessfully()
    {
        var sourcePath = Path.Combine(_testDirectory, "source_hidden.txt");
        var targetPath = Path.Combine(_testDirectory, "target_hidden.txt");
        File.WriteAllText(sourcePath, "source content");
        File.WriteAllText(targetPath, "target hidden content");

        File.SetAttributes(targetPath, FileAttributes.Hidden);

        var act = () => _sut.MoveFile(sourcePath, targetPath, overwrite: true);

        act.Should().NotThrow();
        File.ReadAllText(targetPath).Should().Be("source content");
    }

    [Fact]
    public void MoveFile_WhenCalled_PreservesTimestampsOnTarget()
    {
        var sourcePath = Path.Combine(_testDirectory, "source_timed.txt");
        var targetPath = Path.Combine(_testDirectory, "target_timed.txt");
        File.WriteAllText(sourcePath, "timestamp test");

        var pastCreationTime = new DateTime(2023, 5, 10, 8, 30, 0, DateTimeKind.Utc);
        var pastLastWriteTime = new DateTime(2023, 6, 15, 14, 45, 0, DateTimeKind.Utc);
        File.SetCreationTimeUtc(sourcePath, pastCreationTime);
        File.SetLastWriteTimeUtc(sourcePath, pastLastWriteTime);

        _sut.MoveFile(sourcePath, targetPath, overwrite: false);

        var targetCreation = File.GetCreationTimeUtc(targetPath);
        var targetLastWrite = File.GetLastWriteTimeUtc(targetPath);

        targetCreation.Should().BeCloseTo(pastCreationTime, TimeSpan.FromSeconds(2));
        targetLastWrite.Should().BeCloseTo(pastLastWriteTime, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void DeleteFile_WhenFileIsReadOnly_DeletesSuccessfully()
    {
        var filePath = Path.Combine(_testDirectory, "readonly_delete.txt");
        File.WriteAllText(filePath, "to be deleted");
        File.SetAttributes(filePath, FileAttributes.ReadOnly);

        var act = () => _sut.DeleteFile(filePath);

        act.Should().NotThrow();
        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public void DeleteFile_WhenFileDoesNotExist_DoesNotThrow()
    {
        var nonExistent = Path.Combine(_testDirectory, "does_not_exist.txt");

        var act = () => _sut.DeleteFile(nonExistent);

        act.Should().NotThrow();
    }

    [Fact]
    public void RemoveEmptyDirectories_WhenNestedEmptyDirectoriesExist_DeletesOnlyEmptyDirectoriesAndPreservesRoot()
    {
        var root = new DirectoryRoot(_testDirectory);

        var emptySub1 = Path.Combine(_testDirectory, "empty1");
        var emptySub2Nested = Path.Combine(_testDirectory, "nested_empty", "child_empty");
        var nonEmptyDir = Path.Combine(_testDirectory, "has_file");
        var nonEmptyNestedDir = Path.Combine(_testDirectory, "nested_has_file", "sub_has_file");

        Directory.CreateDirectory(emptySub1);
        Directory.CreateDirectory(emptySub2Nested);
        Directory.CreateDirectory(nonEmptyDir);
        Directory.CreateDirectory(nonEmptyNestedDir);

        File.WriteAllText(Path.Combine(nonEmptyDir, "file.txt"), "keep");
        File.WriteAllText(Path.Combine(nonEmptyNestedDir, "file2.txt"), "keep2");

        _sut.RemoveEmptyDirectories(root);

        Directory.Exists(emptySub1).Should().BeFalse();
        Directory.Exists(emptySub2Nested).Should().BeFalse();
        Directory.Exists(Path.Combine(_testDirectory, "nested_empty")).Should().BeFalse();

        Directory.Exists(_testDirectory).Should().BeTrue();
        Directory.Exists(nonEmptyDir).Should().BeTrue();
        Directory.Exists(nonEmptyNestedDir).Should().BeTrue();
        Directory.Exists(Path.Combine(_testDirectory, "nested_has_file")).Should().BeTrue();
    }

    [Fact]
    public void RemoveEmptyDirectories_WhenRootDoesNotExist_DoesNotThrow()
    {
        var nonExistent = new DirectoryRoot(Path.Combine(_testDirectory, "missing_root"));

        var act = () => _sut.RemoveEmptyDirectories(nonExistent);

        act.Should().NotThrow();
    }

    [Fact]
    public void CopyFile_WhenDestinationIsReadOnly_OverwritesSuccessfully()
    {
        var sourcePath = Path.Combine(_testDirectory, "source.txt");
        var targetPath = Path.Combine(_testDirectory, "target.txt");
        File.WriteAllText(sourcePath, "source content");
        File.WriteAllText(targetPath, "target content");
        File.SetAttributes(targetPath, FileAttributes.ReadOnly);

        _sut.CopyFile(sourcePath, targetPath, overwrite: true);

        File.ReadAllText(targetPath).Should().Be("source content");
        (File.GetAttributes(targetPath) & FileAttributes.ReadOnly).Should().Be(0);
    }

    [Fact]
    public void StripReadOnlyAttribute_WhenFileIsReadOnly_ClearsReadOnlyFlag()
    {
        var filePath = Path.Combine(_testDirectory, "readonly.txt");
        File.WriteAllText(filePath, "content");
        File.SetAttributes(filePath, FileAttributes.ReadOnly);

        _sut.StripReadOnlyAttribute(filePath);

        (File.GetAttributes(filePath) & FileAttributes.ReadOnly).Should().Be(0);
    }
}
