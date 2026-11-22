using PokeRedSync;
using Xunit;

public class AppVersionTests
{
    [Theory]
    [InlineData("1122c", "PokeRedSync 1122c")]
    [InlineData(null, "PokeRedSync ???")]
    [InlineData("", "PokeRedSync ???")]
    public void DisplayNameUsesVersionOrDevelopmentFallback(string? version, string expected) =>
        Assert.Equal(expected, AppVersion.DisplayName(version));

    [Fact]
    public void DisplayNameFitsNotifyIcon() => Assert.Equal(127, AppVersion.DisplayName(new string('a', 200)).Length);
}
