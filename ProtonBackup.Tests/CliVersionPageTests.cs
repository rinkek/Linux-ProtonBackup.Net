using ProtonBackup.Core;

namespace ProtonBackup.Tests;

public class CliVersionPageTests
{
    private static string RealPage =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "version-page-0.8.0.html"));

    [Fact]
    public void ParsesTheRealVersionPage()
    {
        var release = CliVersionPage.Parse(RealPage);

        Assert.NotNull(release);
        Assert.Equal("0.8.0", release.Version);
        Assert.Equal(9, release.Downloads.Count);
    }

    [Fact]
    public void FindsTheLinuxBuildWithItsChecksum()
    {
        var download = CliVersionPage.Parse(RealPage)!.For("linux-x64");

        Assert.NotNull(download);
        Assert.Equal("https://proton.me/download/drive/cli/0.8.0/linux-x64/proton-drive", download.Url);
        Assert.Equal("cf61c2688c45e1055d8add6221d9471a5a5b64bf3bcdb86460f5cb18414596cc4df3cdb6627c9097c94bec32a3c9915ada3211ef2ae5be33c46ebbc996ccaa28",
            download.Sha512);
    }

    [Fact]
    public void FindsTheBaselineBuildUsedAfterAnIllegalInstruction() =>
        Assert.NotNull(CliVersionPage.Parse(RealPage)!.For("linux-x64-baseline"));

    [Fact]
    public void ReturnsNothingWhenThePageIsUnrecognisable()
    {
        Assert.Null(CliVersionPage.Parse("<html><body>Onderhoud</body></html>"));
        Assert.Null(CliVersionPage.Parse(""));
    }

    [Fact]
    public void ReturnsNothingWhenTheVersionIsPresentButTheTableIsEmpty() =>
        Assert.Null(CliVersionPage.Parse("<h1>Proton Drive CLI 0.9.0</h1><table><tbody></tbody></table>"));

    [Fact]
    public void ToleratesAMissingChecksumColumn()
    {
        var release = CliVersionPage.Parse("""
            <h1>Proton Drive CLI 0.9.0</h1>
            <table><tr><td>linux/x64</td><td><a href="https://example.test/proton-drive">link</a></td></tr></table>
            """);

        var download = release!.For("linux-x64");
        Assert.NotNull(download);
        Assert.Null(download.Sha512);
    }

    [Theory]
    [InlineData("0.9.0", "0.8.0", true)]
    [InlineData("0.10.0", "0.9.0", true)]
    [InlineData("1.0.0", "0.99.9", true)]
    [InlineData("0.8.0", "0.8.0", false)]
    [InlineData("0.7.9", "0.8.0", false)]
    public void ComparesVersionsAsNumbers(string candidate, string current, bool expected) =>
        Assert.Equal(expected, CliVersionPage.IsNewer(candidate, current));
}
