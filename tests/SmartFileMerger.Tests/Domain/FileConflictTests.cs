namespace SmartFileMerger.Tests.Domain;

using FluentAssertions;
using SmartFileMerger.Domain.Models;
using Xunit;

public sealed class FileConflictTests
{
    private static FileMetadata CreateMetadata(string fullPath, long size, DateTime lastWriteUtc) =>
        new(fullPath, size, lastWriteUtc.AddHours(-1), lastWriteUtc);

    [Fact]
    public void SizeDifferenceInBytes_CalculatesSourceMinusTarget()
    {
        // Arrange
        var relativePath = new RelativeFilePath("test.txt");
        var source = CreateMetadata(@"C:\A\test.txt", 1500, DateTime.UtcNow);
        var target = CreateMetadata(@"C:\B\test.txt", 1000, DateTime.UtcNow);
        var conflict = new FileConflict(relativePath, source, target);

        // Act & Assert
        conflict.SizeDifferenceInBytes.Should().Be(500);
    }

    [Fact]
    public void AgeDifference_CalculatesSourceMinusTarget()
    {
        // Arrange
        var relativePath = new RelativeFilePath("test.txt");
        var baseTime = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        var source = CreateMetadata(@"C:\A\test.txt", 1000, baseTime.AddMinutes(5));
        var target = CreateMetadata(@"C:\B\test.txt", 1000, baseTime);
        var conflict = new FileConflict(relativePath, source, target);

        // Act & Assert
        conflict.AgeDifference.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void IsIdenticalSizeAndTimestamp_WhenExactSameSizeAndTimestamp_ReturnsTrue()
    {
        // Arrange
        var relativePath = new RelativeFilePath("test.txt");
        var now = DateTime.UtcNow;
        var source = CreateMetadata(@"C:\A\test.txt", 2048, now);
        var target = CreateMetadata(@"C:\B\test.txt", 2048, now);
        var conflict = new FileConflict(relativePath, source, target);

        // Act & Assert
        conflict.IsIdenticalSizeAndTimestamp.Should().BeTrue();
    }

    [Theory]
    [InlineData(0.5, true)]
    [InlineData(1.0, true)]
    [InlineData(1.99, true)]
    [InlineData(-1.5, true)]
    [InlineData(2.0, false)]
    [InlineData(3.0, false)]
    [InlineData(-2.0, false)]
    public void IsIdenticalSizeAndTimestamp_WhenTimestampDiffers_FollowsTwoSecondTolerance(double secondDelta, bool expectedResult)
    {
        // Arrange
        var relativePath = new RelativeFilePath("test.txt");
        var baseTime = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        var source = CreateMetadata(@"C:\A\test.txt", 1024, baseTime.AddSeconds(secondDelta));
        var target = CreateMetadata(@"C:\B\test.txt", 1024, baseTime);
        var conflict = new FileConflict(relativePath, source, target);

        // Act & Assert
        conflict.IsIdenticalSizeAndTimestamp.Should().Be(expectedResult);
    }

    [Fact]
    public void IsIdenticalSizeAndTimestamp_WhenSizesDifferEvenWithSameTimestamp_ReturnsFalse()
    {
        // Arrange
        var relativePath = new RelativeFilePath("test.txt");
        var now = DateTime.UtcNow;
        var source = CreateMetadata(@"C:\A\test.txt", 1024, now);
        var target = CreateMetadata(@"C:\B\test.txt", 1025, now);
        var conflict = new FileConflict(relativePath, source, target);

        // Act & Assert
        conflict.IsIdenticalSizeAndTimestamp.Should().BeFalse();
    }
}
