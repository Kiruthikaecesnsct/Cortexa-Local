using System.Globalization;
using Collector.Presentation.Behaviors;

namespace Collector.Tests.Presentation;

public sealed class ColumnsForWidthConverterTests
{
    [Theory]
    [InlineData(744, 176, 16, 5, 3)]
    [InlineData(968, 176, 16, 5, 5)]
    [InlineData(1608, 176, 16, 5, 5)]
    [InlineData(694, 320, 16, 2, 2)]
    [InlineData(694, 320, 16, 3, 2)]
    [InlineData(992, 320, 16, 3, 3)]
    [InlineData(694, 280, 12, 5, 2)]
    [InlineData(918, 280, 12, 5, 3)]
    [InlineData(1558, 280, 12, 5, 5)]
    public void Columns_FitsAsManyItemsAsTheWidthAllows(double width, double min, double gap, int max, int expected)
    {
        Assert.Equal(expected, ColumnsForWidthConverter.Columns(width, min, gap, max));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(-5)]
    public void Columns_NarrowOrInvalidWidth_NeverDropsBelowOne(double width)
    {
        Assert.Equal(1, ColumnsForWidthConverter.Columns(width, 320, 16, 3));
    }

    [Fact]
    public void Columns_WidthNotMeasuredYet_UsesTheMaximum()
    {
        Assert.Equal(3, ColumnsForWidthConverter.Columns(double.NaN, 320, 16, 3));
    }

    [Fact]
    public void Convert_RemovesTheBleedBeforeCounting()
    {
        var converter = new ColumnsForWidthConverter { MinItemWidth = 176, Gap = 16, Bleed = 16, MaxColumns = 5 };

        var columns = converter.Convert(760d, typeof(int), null!, CultureInfo.InvariantCulture);

        Assert.Equal(3, columns);
    }
}
