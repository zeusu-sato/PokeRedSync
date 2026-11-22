using System.Text.Json;
using PokeRedSync.Update;
using Xunit;

public class ReleaseSelectionTests
{
    [Theory]
    [InlineData("v2026.1122a", true)]
    [InlineData("v2026.1231z", true)]
    [InlineData("v1999.1122a", false)]
    [InlineData("v2026.1331a", false)]
    [InlineData("v2026.1100a", false)]
    [InlineData("v2026.1122A", false)]
    [InlineData("v2026.1122a\n", false)]
    [InlineData(null, false)]
    public void Tags(string? tag, bool valid) => Assert.Equal(valid, ReleaseSelection.IsTag(tag));

    [Fact]
    public void SelectsNewestCompleteStableRelease()
    {
        object Release(string tag, bool complete = true, bool draft = false, bool prerelease = false) => new
        {
            tag_name = tag, draft, prerelease,
            assets = complete ? new[] { Asset($"PokeRedSync-{tag}-win-x64.zip"), Asset("SHA256SUMS.txt") } : new[] { Asset("SHA256SUMS.txt") }
        };
        object Asset(string name) => new { name, browser_download_url = "https://example.invalid/asset", size = 1 };
        string json = JsonSerializer.Serialize(new[] { Release("v2026.1123a"), Release("v2026.1125a", false), Release("v2026.1126a", draft: true), Release("v2026.1127a", prerelease: true), Release("v2026.1124a") });
        Assert.Equal("v2026.1124a", ReleaseSelection.Select(json, "v2026.1122a")?.Tag);
        Assert.Null(ReleaseSelection.Select(json, "v2026.1124a"));
    }
}
