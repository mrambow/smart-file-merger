namespace SmartFileMerger.Tests.Domain;

using FluentAssertions;
using SmartFileMerger.Domain.Models;
using Xunit;

public sealed class DirectoryRootTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhenPathIsNullOrWhiteSpace_ThrowsArgumentException(string? invalidPath)
    {
        // Act
        var act = () => new DirectoryRoot(invalidPath!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("\"C:\\Folder\\SubFolder\"", "C:\\Folder\\SubFolder")]
    [InlineData("'C:\\Folder\\SubFolder'", "C:\\Folder\\SubFolder")]
    [InlineData("  \"C:\\Folder\\SubFolder\"  ", "C:\\Folder\\SubFolder")]
    [InlineData("C:\\Folder\\SubFolder\\", "C:\\Folder\\SubFolder")]
    [InlineData("C:\\Folder\\SubFolder/", "C:\\Folder\\SubFolder")]
    public void Constructor_WhenPathHasQuotesOrTrailingSeparators_NormalizesPath(string inputPath, string expectedFullPath)
    {
        // Act
        var directoryRoot = new DirectoryRoot(inputPath);

        // Assert
        directoryRoot.FullPath.Should().Be(expectedFullPath);
    }

    [Fact]
    public void CombineWith_WhenGivenRelativePath_ReturnsCombinedFullPath()
    {
        // Arrange
        var root = new DirectoryRoot(@"C:\Data\Source");
        var relative = new RelativeFilePath(@"subfolder\document.txt");

        // Act
        var result = root.CombineWith(relative);

        // Assert
        result.Should().Be(@"C:\Data\Source\subfolder\document.txt");
    }

    [Fact]
    public void IsSubdirectoryOf_WhenChildIsInsideParent_ReturnsTrue()
    {
        // Arrange
        var parent = new DirectoryRoot(@"C:\Data");
        var child = new DirectoryRoot(@"C:\Data\SubFolder\Nested");

        // Act & Assert
        child.IsSubdirectoryOf(parent).Should().BeTrue();
    }

    [Fact]
    public void IsSubdirectoryOf_WhenPathsAreEqual_ReturnsTrue()
    {
        // Arrange
        var dir1 = new DirectoryRoot(@"C:\Data");
        var dir2 = new DirectoryRoot(@"C:\Data");

        // Act & Assert
        dir1.IsSubdirectoryOf(dir2).Should().BeTrue();
    }

    [Fact]
    public void IsSubdirectoryOf_WhenDirectorySharesPrefixNameButIsNotSubfolder_ReturnsFalse()
    {
        // Arrange
        // C:\Data2 is NOT a subdirectory of C:\Data
        var potentialParent = new DirectoryRoot(@"C:\Data");
        var sibling = new DirectoryRoot(@"C:\Data2");

        // Act & Assert
        sibling.IsSubdirectoryOf(potentialParent).Should().BeFalse();
    }

    [Fact]
    public void IsSubdirectoryOf_WhenParentIsChild_ReturnsFalse()
    {
        // Arrange
        var parent = new DirectoryRoot(@"C:\Data");
        var child = new DirectoryRoot(@"C:\Data\SubFolder");

        // Act & Assert
        parent.IsSubdirectoryOf(child).Should().BeFalse();
    }

    [Fact]
    public void CombineWith_WhenRelativePathIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var root = new DirectoryRoot(@"C:\Data\Source");

        // Act
        var act = () => root.CombineWith(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsSubdirectoryOf_WhenPotentialParentIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var root = new DirectoryRoot(@"C:\Data\Source");

        // Act
        var act = () => root.IsSubdirectoryOf(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsSubdirectoryOf_WhenDifferentDrives_ReturnsFalse()
    {
        // Arrange
        var rootC = new DirectoryRoot(@"C:\Data");
        var rootD = new DirectoryRoot(@"D:\Data");

        // Act & Assert
        rootD.IsSubdirectoryOf(rootC).Should().BeFalse();
    }
}
