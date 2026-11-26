using OptimizingSoftware.Core.Updates;

namespace OptimizingSoftware.Core.Tests;

public class UpdateFeedTests
{
    [Theory]
    [InlineData("v0.8.0", "0.8.0")]
    [InlineData("0.8", "0.8.0")]
    [InlineData("v1.2.3-beta", "1.2.3")]
    [InlineData("1", "1.0.0")]
    public void Versions_are_parsed(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateFeed.ParseVersion(tag));

    [Theory]
    [InlineData("")]
    [InlineData("nightly")]
    public void Bad_versions_are_ignored(string tag) => Assert.Null(UpdateFeed.ParseVersion(tag));

    [Fact]
    public void Release_json_gives_version_and_installer()
    {
        const string json = """
            {"tag_name":"v0.8.0","html_url":"https://github.com/x/y/releases/tag/v0.8.0","body":"Nouveautés",
             "assets":[{"name":"OptimizingSoftware-win-x64.zip","browser_download_url":"https://a/zip"},
                       {"name":"OptimizingSoftware-Setup-0.8.0.exe","browser_download_url":"https://a/setup"}]}
            """;
        var info = UpdateFeed.Parse(json)!;
        Assert.Equal(new Version(0, 8, 0), info.Version);
        Assert.Equal("https://a/setup", info.InstallerUrl);
        Assert.True(UpdateFeed.IsNewer(info, new Version(0, 7, 0, 0)));
        Assert.False(UpdateFeed.IsNewer(info, new Version(0, 8, 0, 0)));
        Assert.Null(UpdateFeed.Parse("""{"message":"Not Found"}"""));
    }
}
