using System.Windows;
using app.Input;

namespace app.Tests.Input;

public class CursorControllerTests
{
    [Fact]
    public void ConvertToScreenCoordinates_Zero_Zero_Returns_Zero_Zero()
    {
        var (x, y) = CursorController.ConvertToScreenCoordinates(0, 0);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void ConvertToScreenCoordinates_One_One_Returns_ScreenDimensions()
    {
        var (x, y) = CursorController.ConvertToScreenCoordinates(1, 1);
        Assert.Equal((int)SystemParameters.PrimaryScreenWidth, x);
        Assert.Equal((int)SystemParameters.PrimaryScreenHeight, y);
    }

    [Fact]
    public void ConvertToScreenCoordinates_Half_Half_Returns_ScreenCenter()
    {
        var (x, y) = CursorController.ConvertToScreenCoordinates(0.5, 0.5);
        Assert.Equal((int)(SystemParameters.PrimaryScreenWidth * 0.5), x);
        Assert.Equal((int)(SystemParameters.PrimaryScreenHeight * 0.5), y);
    }

    [Fact]
    public void ConvertToScreenCoordinates_CorrectlyScales()
    {
        // Test that the conversion is proportional
        var (x1, y1) = CursorController.ConvertToScreenCoordinates(0.25, 0.25);
        var (x2, y2) = CursorController.ConvertToScreenCoordinates(0.75, 0.75);

        // 0.75 should be 3x 0.25
        Assert.Equal(x2, x1 * 3);
        Assert.Equal(y2, y1 * 3);
    }

    [Fact]
    public void ConvertToScreenCoordinates_OutOfRange_WrapsCorrectly()
    {
        // The ConvertToScreenCoordinates method itself doesn't clamp,
        // but MoveToNormalized does. This test verifies the raw conversion.
        // Negative values should produce negative screen coordinates
        var (x, y) = CursorController.ConvertToScreenCoordinates(-0.5, -0.5);
        Assert.True(x < 0);
        Assert.True(y < 0);

        // Values > 1 should produce coordinates beyond screen dimensions
        var (x2, y2) = CursorController.ConvertToScreenCoordinates(1.5, 1.5);
        Assert.True(x2 > SystemParameters.PrimaryScreenWidth);
        Assert.True(y2 > SystemParameters.PrimaryScreenHeight);
    }
}
