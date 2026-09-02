using Stats.Core.Updates;

namespace Stats.Core.Tests;

public class UpdateCheckerTests
{
    private static readonly Version Current141 = new(1, 4, 1);

    /// <summary>Shapes a minimal GitHub /releases/latest body: tag_name, html_url, and one asset entry (name,
    /// size, browser_download_url) — mirrors the real payload fields UpdateChecker.Parse reads.</summary>
    private static string Release(string tag, string assetName, long assetSize, string assetUrl = "https://github.com/sawyerkollman/statsapp/releases/download/v1.4.2/asset.exe", string htmlUrl = "https://github.com/sawyerkollman/statsapp/releases/tag/v1.4.2") => $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "{{htmlUrl}}",
          "assets": [
            { "name": "{{assetName}}", "size": {{assetSize}}, "browser_download_url": "{{assetUrl}}" }
          ]
        }
        """;

    [Fact]
    public void Parse_NewerVersion_IsOffered()
    {
        var json = Release("v1.4.2", "Stats-Setup-1.4.2.exe", 12345);
        var info = UpdateChecker.Parse(json, Current141);
        Assert.NotNull(info);
        Assert.Equal(new Version(1, 4, 2), info!.Version);
        Assert.Equal("v1.4.2", info.TagName);
    }

    [Fact]
    public void Parse_EqualVersion_ReturnsNull()
    {
        var json = Release("v1.4.1", "Stats-Setup-1.4.1.exe", 12345);
        Assert.Null(UpdateChecker.Parse(json, Current141));
    }

    [Fact]
    public void Parse_OlderVersion_ReturnsNull()
    {
        var json = Release("v1.4.0", "Stats-Setup-1.4.0.exe", 12345);
        Assert.Null(UpdateChecker.Parse(json, Current141));
    }

    [Fact]
    public void Parse_PrereleaseTag_ReturnsNull()
    {
        var json = Release("v1.5.0-beta", "Stats-Setup-1.5.0-beta.exe", 12345);
        Assert.Null(UpdateChecker.Parse(json, Current141));
    }

    [Fact]
    public void Parse_MissingAsset_ReturnsNull()
    {
        var json = Release("v1.4.2", "SomeOtherFile.exe", 12345);
        Assert.Null(UpdateChecker.Parse(json, Current141));
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsNull()
    {
        Assert.Null(UpdateChecker.Parse("{ this is not json", Current141));
    }

    [Fact]
    public void Parse_EmptyJson_ReturnsNull()
    {
        Assert.Null(UpdateChecker.Parse("", Current141));
        Assert.Null(UpdateChecker.Parse("   ", Current141));
    }

    [Fact]
    public void Parse_AssetPickedByExactName_IgnoresOtherAssets()
    {
        const string json = """
            {
              "tag_name": "v1.4.2",
              "html_url": "https://github.com/sawyerkollman/statsapp/releases/tag/v1.4.2",
              "assets": [
                { "name": "Stats-Setup-1.4.2.exe.sha256", "size": 64, "browser_download_url": "https://github.com/wrong1" },
                { "name": "NotStats-Setup-1.4.2.exe", "size": 99, "browser_download_url": "https://github.com/wrong2" },
                { "name": "Stats-Setup-1.4.2.exe", "size": 5551212, "browser_download_url": "https://github.com/right" }
              ]
            }
            """;
        var info = UpdateChecker.Parse(json, Current141);
        Assert.NotNull(info);
        Assert.Equal(5551212L, info!.AssetSize);
        Assert.Equal("https://github.com/right", info.AssetUrl);
    }

    [Fact]
    public void Parse_SizeAndUrlAndReleasePage_AreSurfaced()
    {
        var json = Release("v1.4.2", "Stats-Setup-1.4.2.exe", 999,
            assetUrl: "https://github.com/sawyerkollman/statsapp/releases/download/v1.4.2/Stats-Setup-1.4.2.exe",
            htmlUrl: "https://github.com/sawyerkollman/statsapp/releases/tag/v1.4.2");
        var info = UpdateChecker.Parse(json, Current141);
        Assert.NotNull(info);
        Assert.Equal(999L, info!.AssetSize);
        Assert.Equal("https://github.com/sawyerkollman/statsapp/releases/download/v1.4.2/Stats-Setup-1.4.2.exe", info.AssetUrl);
        Assert.Equal("https://github.com/sawyerkollman/statsapp/releases/tag/v1.4.2", info.ReleasePageUrl);
    }

    // ---- version edges ----

    [Fact]
    public void Parse_141_vs_140_IsOffered()
    {
        var json = Release("v1.4.1", "Stats-Setup-1.4.1.exe", 1);
        Assert.NotNull(UpdateChecker.Parse(json, new Version(1, 4, 0)));
    }

    [Fact]
    public void Parse_1_10_0_vs_1_9_9_IsOffered()
    {
        var json = Release("v1.10.0", "Stats-Setup-1.10.0.exe", 1);
        Assert.NotNull(UpdateChecker.Parse(json, new Version(1, 9, 9)));
    }

    [Fact]
    public void Parse_2_0_0_vs_1_99_99_IsOffered()
    {
        var json = Release("v2.0.0", "Stats-Setup-2.0.0.exe", 1);
        Assert.NotNull(UpdateChecker.Parse(json, new Version(1, 99, 99)));
    }

    [Fact]
    public void Parse_DevBuildCurrent_AlwaysReturnsNull()
    {
        var json = Release("v99.0.0", "Stats-Setup-99.0.0.exe", 1);
        Assert.Null(UpdateChecker.Parse(json, new Version(0, 0, 0)));
        Assert.Null(UpdateChecker.Parse(json, new Version(0, 0, 0, 0)));
    }

    // ---- malformed shapes ----

    [Fact]
    public void Parse_ArrayRoot_ReturnsNull()
    {
        Assert.Null(UpdateChecker.Parse("[]", Current141));
    }

    [Fact]
    public void Parse_NullRoot_ReturnsNull()
    {
        Assert.Null(UpdateChecker.Parse("null", Current141));
    }

    [Fact]
    public void Parse_MissingTagName_ReturnsNull()
    {
        const string json = """
            {
              "html_url": "https://github.com/sawyerkollman/statsapp/releases/tag/v1.4.2",
              "assets": [
                { "name": "Stats-Setup-1.4.2.exe", "size": 1, "browser_download_url": "https://example.com/asset.exe" }
              ]
            }
            """;
        Assert.Null(UpdateChecker.Parse(json, Current141));
    }

    [Fact]
    public void Parse_MissingAssets_ReturnsNull()
    {
        const string json = """
            {
              "tag_name": "v1.4.2",
              "html_url": "https://github.com/sawyerkollman/statsapp/releases/tag/v1.4.2"
            }
            """;
        Assert.Null(UpdateChecker.Parse(json, Current141));
    }

    [Fact]
    public void Parse_FourFieldTag_DoesNotThrow()
    {
        var json = Release("v1.4.2.3", "Stats-Setup-1.4.2.3.exe", 1);
        var ex = Record.Exception(() => UpdateChecker.Parse(json, Current141));
        Assert.Null(ex);
    }

    // ---- asset host allow-list ----

    [Fact]
    public void Parse_AssetUrlWrongHost_ReturnsNull()
    {
        var json = Release("v1.4.2", "Stats-Setup-1.4.2.exe", 12345, assetUrl: "https://evil.example.com/Stats-Setup-1.4.2.exe");
        Assert.Null(UpdateChecker.Parse(json, Current141));
    }

    [Fact]
    public void Parse_AssetUrlGithubusercontentSubdomain_IsAccepted()
    {
        var json = Release("v1.4.2", "Stats-Setup-1.4.2.exe", 12345,
            assetUrl: "https://objects.githubusercontent.com/github-production-release-asset/Stats-Setup-1.4.2.exe");
        var info = UpdateChecker.Parse(json, Current141);
        Assert.NotNull(info);
        Assert.Equal("https://objects.githubusercontent.com/github-production-release-asset/Stats-Setup-1.4.2.exe", info!.AssetUrl);
    }

    // ---- dev-build detection / About-section display (v1.7) ----

    [Fact]
    public void IsDevBuild_AllZeroFirstThreeComponents_IsTrue()
    {
        Assert.True(UpdateChecker.IsDevBuild(new Version(0, 0, 0)));
        Assert.True(UpdateChecker.IsDevBuild(new Version(0, 0))); // unset Build (-1) normalizes the same as 0
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(1, 7, 0)]
    public void IsDevBuild_AnyNonZeroFirstThreeComponents_IsFalse(int major, int minor, int build)
    {
        Assert.False(UpdateChecker.IsDevBuild(new Version(major, minor, build)));
    }

    [Fact]
    public void FormatVersionDisplay_DevBuild_SaysDevelopmentBuild()
    {
        Assert.Equal("Development build", UpdateChecker.FormatVersionDisplay(new Version(0, 0, 0, 0)));
    }

    [Fact]
    public void FormatVersionDisplay_ReleaseBuild_FormatsFirstThreeComponentsWithVPrefix()
    {
        Assert.Equal("v1.7.0", UpdateChecker.FormatVersionDisplay(new Version(1, 7, 0)));
        Assert.Equal("v1.7.0", UpdateChecker.FormatVersionDisplay(new Version(1, 7, 0, 3))); // fourth field ignored
    }

    // ---- SHA-256 release-body parsing (v1.7 update integrity) ----

    private const string ValidHash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    /// <summary>Same shape as <see cref="Release"/> but with a "body" field, mirroring the real payload field
    /// UpdateChecker.Parse reads the SHA256 line from.</summary>
    private static string ReleaseWithBody(string tag, string assetName, long assetSize, string body) => $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://github.com/sawyerkollman/statsapp/releases/tag/v1.4.2",
          "body": {{System.Text.Json.JsonSerializer.Serialize(body)}},
          "assets": [
            { "name": "{{assetName}}", "size": {{assetSize}}, "browser_download_url": "https://github.com/sawyerkollman/statsapp/releases/download/v1.4.2/asset.exe" }
          ]
        }
        """;

    [Fact]
    public void Parse_ReleaseBodyWithSha256Line_IsSurfacedAndLowercased()
    {
        var json = ReleaseWithBody("v1.4.2", "Stats-Setup-1.4.2.exe", 1, $"## Install\n\nSHA256: {ValidHash.ToUpperInvariant()}\n\n---");
        var info = UpdateChecker.Parse(json, Current141);
        Assert.NotNull(info);
        Assert.Equal(ValidHash, info!.Sha256);
    }

    [Fact]
    public void Parse_ReleaseBodyCaseInsensitiveLabelAndSurroundingWhitespace_IsRecognized()
    {
        var json = ReleaseWithBody("v1.4.2", "Stats-Setup-1.4.2.exe", 1, $"  sha256:   {ValidHash}  ");
        var info = UpdateChecker.Parse(json, Current141);
        Assert.NotNull(info);
        Assert.Equal(ValidHash, info!.Sha256);
    }

    [Fact]
    public void Parse_ReleaseBodyWithoutSha256Line_LeavesShaNull()
    {
        var json = ReleaseWithBody("v1.4.2", "Stats-Setup-1.4.2.exe", 1, "## Install\n\nDownload and run it.");
        var info = UpdateChecker.Parse(json, Current141);
        Assert.NotNull(info);
        Assert.Null(info!.Sha256);
    }

    [Fact]
    public void Parse_ReleaseWithNoBodyField_LeavesShaNull()
    {
        var json = Release("v1.4.2", "Stats-Setup-1.4.2.exe", 12345); // no "body" property at all
        var info = UpdateChecker.Parse(json, Current141);
        Assert.NotNull(info);
        Assert.Null(info!.Sha256);
    }

    [Theory]
    [InlineData("SHA256: " /* too short */ + "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a")]
    [InlineData("SHA256: " /* too long: 65 hex chars */ + "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a081")]
    [InlineData("SHA256: 9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08 trailing-text")]
    [InlineData("SHA256: 9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a0g")] // non-hex char
    [InlineData("SHA-256: 9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")] // wrong label
    [InlineData("PGP256: 9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")]
    public void Parse_MalformedSha256Line_IsIgnored(string malformedLine)
    {
        var json = ReleaseWithBody("v1.4.2", "Stats-Setup-1.4.2.exe", 1, $"## Install\n{malformedLine}\n---");
        var info = UpdateChecker.Parse(json, Current141);
        Assert.NotNull(info);
        Assert.Null(info!.Sha256);
    }

    [Fact]
    public void Parse_BodyIsNotAString_LeavesShaNull()
    {
        const string json = """
            {
              "tag_name": "v1.4.2",
              "html_url": "https://github.com/sawyerkollman/statsapp/releases/tag/v1.4.2",
              "body": 12345,
              "assets": [
                { "name": "Stats-Setup-1.4.2.exe", "size": 1, "browser_download_url": "https://github.com/sawyerkollman/statsapp/releases/download/v1.4.2/asset.exe" }
              ]
            }
            """;
        var info = UpdateChecker.Parse(json, Current141);
        Assert.NotNull(info);
        Assert.Null(info!.Sha256);
    }
}
