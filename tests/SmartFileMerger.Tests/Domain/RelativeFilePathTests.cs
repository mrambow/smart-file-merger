namespace SmartFileMerger.Tests.Domain;

using FluentAssertions;
using SmartFileMerger.Domain.Models;
using Xunit;

public sealed class RelativeFilePathTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    [InlineData("''")]
    [InlineData("  \"\"  ")]
    [InlineData("\"   \"")]
    public void Constructor_WhenPathIsNullOrWhiteSpaceOrOnlyQuotes_ThrowsArgumentException(string? invalidPath)
    {
        // Act
        var act = () => new RelativeFilePath(invalidPath!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(@"C:\file.txt")]
    [InlineData(@"/file.txt")]
    [InlineData(@"\file.txt")]
    [InlineData(@"\\server\share\file.txt")]
    public void Constructor_WhenPathIsRooted_ThrowsArgumentException(string rootedPath)
    {
        // Act
        var act = () => new RelativeFilePath(rootedPath);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Pfad muss relativ sein.*");
    }

    [Theory]
    [InlineData("\"folder/file.txt\"", @"folder\file.txt")]
    [InlineData("'folder\\file.txt'", @"folder\file.txt")]
    [InlineData("  \"folder/sub/file.txt\"  ", @"folder\sub\file.txt")]
    public void Constructor_WhenPathHasQuotesOrForwardSlashes_NormalizesValue(string inputPath, string expected)
    {
        // Act
        var relativePath = new RelativeFilePath(inputPath);

        // Assert
        relativePath.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData(@"..\file.txt")]
    [InlineData(@"../file.txt")]
    [InlineData(@"a/../b")]
    [InlineData(@"a\..\b")]
    [InlineData(@"sub\dir\..")]
    [InlineData(@"..")]
    [InlineData(@"dir\..\..\file.txt")]
    [InlineData(@"a/ .. /b")]
    public void Constructor_WhenPathContainsPathTraversal_ThrowsArgumentException(string traversalPath)
    {
        // Act
        var act = () => new RelativeFilePath(traversalPath);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Path traversal ('..') is not permitted.*");
    }

    [Theory]
    [InlineData("file..name.txt")]
    [InlineData(@"folder..sub\file.txt")]
    [InlineData("version.1..2.zip")]
    public void Constructor_WhenPathContainsDoubleDotsInFileNameOrFolder_DoesNotThrow(string validPath)
    {
        // Act
        var act = () => new RelativeFilePath(validPath);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Properties_WhenSingleFileName_ReturnsExpectedComponents()
    {
        // Arrange
        var relativePath = new RelativeFilePath("document.pdf");

        // Assert
        relativePath.FileName.Should().Be("document.pdf");
        relativePath.Extension.Should().Be(".pdf");
        relativePath.FileNameWithoutExtension.Should().Be("document");
        relativePath.DirectoryName.Should().BeEmpty();
    }

    [Fact]
    public void Properties_WhenNestedFilePath_ReturnsExpectedComponents()
    {
        // Arrange
        var relativePath = new RelativeFilePath(@"sub\nested\image.png");

        // Assert
        relativePath.FileName.Should().Be("image.png");
        relativePath.Extension.Should().Be(".png");
        relativePath.FileNameWithoutExtension.Should().Be("image");
        relativePath.DirectoryName.Should().Be(@"sub\nested");
    }
}
