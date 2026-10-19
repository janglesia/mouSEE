namespace app.Tests;

public class UiColorsTests
{
    [Theory]
    [InlineData("#101010")] // the eye check, accuracy test, fine-tune, re-center
    [InlineData("#141414")] // the calibrations window
    public void Middle_dot_is_the_yellow_on_dark(string background)
    {
        Assert.Equal(UiColors.Yellow, UiColors.MiddleDot(UiColors.Hex(background)));
    }

    [Fact]
    public void Middle_dot_is_the_dark_on_the_yellow()
    {
        // the grid screen, and the eye check's bright steps
        Assert.Equal(UiColors.Dark, UiColors.MiddleDot(UiColors.Hex("#F7D060")));
    }
}
