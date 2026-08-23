using System.Text.Json;

namespace Stats.Core.Updates;

/// <summary>A newer release, ready to offer. <see cref="Version"/> is the parsed numeric version (first three
/// fields; the fourth is always 0); <see cref="TagName"/> is the raw GitHub tag (e.g. "v1.4.2") for display.</summary>
public sealed record UpdateInfo(Version Version, string TagName, string AssetUrl, long AssetSize, string ReleasePageUrl);

/// <summary>Pure parsing of a GitHub "latest release" API response — no I/O, no WPF. Kept separate from
/// <see cref="UpdateService"/> so the interesting logic is unit-testable without a network.</summary>
public static class UpdateChecker
{
    private const string AssetPrefix = "Stats-Setup-";
    private const string AssetSuffix = ".exe";

    /// <summary>Parses a GitHub /releases/latest JSON body and decides whether it describes an update over
    /// <paramref name="current"/>. Returns null for: malformed/empty JSON, a missing/non-string tag_name, a
    /// prerelease tag (contains "-"), a version that is not strictly newer than <paramref name="current"/> (only
    /// the first three fields are compared), a dev build current version (0.0.0), or a release with no asset
    /// named exactly "Stats-Setup-{tag-without-v}.exe".</summary>
    public static UpdateInfo? Parse(string latestReleaseJson, Version current)
    {
        if (string.IsNullOrWhiteSpace(latestReleaseJson)) return null;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(latestReleaseJson); }
        catch (JsonException) { return null; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("tag_name", out var tagProp) || tagProp.ValueKind != JsonValueKind.String) return null;
            var tag = tagProp.GetString();
            if (string.IsNullOrWhiteSpace(tag)) return null;

            var versionPart = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag[1..] : tag;
            if (versionPart.Contains('-')) return null; // prerelease suffix (e.g. "1.4.1-beta") — never offered
            if (!Version.TryParse(versionPart, out var parsed)) return null;

            var latest = NormalizeToThree(parsed);
            var currentNormalized = NormalizeToThree(current);
            if (currentNormalized == new Version(0, 0, 0)) return null; // dev build: never offer
            if (latest <= currentNormalized) return null;

            var releasePageUrl = root.TryGetProperty("html_url", out var htmlProp) && htmlProp.ValueKind == JsonValueKind.String
                ? htmlProp.GetString() ?? "" : "";

            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

            var expectedName = AssetPrefix + versionPart + AssetSuffix;
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object) continue;
                if (!asset.TryGetProperty("name", out var nameProp) || nameProp.ValueKind != JsonValueKind.String) continue;
                if (!string.Equals(nameProp.GetString(), expectedName, StringComparison.Ordinal)) continue;
                if (!asset.TryGetProperty("browser_download_url", out var urlProp) || urlProp.ValueKind != JsonValueKind.String) continue;
                if (!asset.TryGetProperty("size", out var sizeProp) || sizeProp.ValueKind != JsonValueKind.Number) continue;

                return new UpdateInfo(latest, tag, urlProp.GetString()!, sizeProp.GetInt64(), releasePageUrl);
            }
            return null; // no asset with the expected exact name
        }
    }

    /// <summary>First three fields only; an unset Minor/Build (-1, from parsing e.g. "1") normalizes to 0.</summary>
    private static Version NormalizeToThree(Version v) =>
        new(Math.Max(v.Major, 0), Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
}
