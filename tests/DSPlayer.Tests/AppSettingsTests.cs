using DSPlayer.Models;
using Xunit;

namespace DSPlayer.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void Defaults_UseSevenSecondPollingAndSafePersistenceChoices()
    {
        var settings = new AppSettings();

        Assert.Equal(7, settings.BbsIntervalSeconds);
        Assert.False(settings.AlwaysOnTop);
        Assert.True(settings.SaveWindowPlacement);
        Assert.False(settings.SaveVolume);
        Assert.Equal(0, settings.SavedVolume);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(5, 5)]
    [InlineData(7, 7)]
    [InlineData(121, 120)]
    public void Sanitize_ClampsPollingInterval(int input, int expected)
    {
        var settings = new AppSettings { BbsIntervalSeconds = input };

        settings.Sanitize();

        Assert.Equal(expected, settings.BbsIntervalSeconds);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(55, 55)]
    [InlineData(101, 100)]
    public void Sanitize_ClampsSavedVolume(double input, double expected)
    {
        var settings = new AppSettings { SavedVolume = input };

        settings.Sanitize();

        Assert.Equal(expected, settings.SavedVolume);
    }
}
