using Stats.Core.Updates;

namespace Stats.Core.Tests;

public class UpdateCheckerTests
{
    private static readonly Version Current141 = new(1, 4, 1);

    /// <summary>Shapes a minimal GitHub /releases/latest body: tag_name, html_url, and one asset entry (name,
    /// size, browser_download_url) — mirrors the real payload fields UpdateChecker.Parse reads.</summary>
    private static string Release(string tag, string assetName, long assetSize, string assetUrl = "https://example.com/asset.exe", string htmlUrl = "https://github.com/sawyerkollman/statsapp/releases/tag/v1.4.2") => $$"""
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
                { "name": "Stats-Setup-1.4.2.exe.sha256", "size": 64, "browser_download_url": "https://example.com/wrong1" },
                { "name": "NotStats-Setup-1.4.2.exe", "size": 99, "browser_download_url": "https://example.com/wrong2" },
                { "name": "Stats-Setup-1.4.2.exe", "size": 5551212, "browser_download_url": "https://example.com/right" }
              ]
            }
            """;
        var info = UpdateChecker.Parse(json, Current141);
        Assert.NotNull(info);
        Assert.Equal(5551212L, info!.AssetSize);
        Assert.Equal("https://example.com/right", info.AssetUrl);
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
}
