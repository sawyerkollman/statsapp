using System.Text.Json;
using System.Text.RegularExpressions;

namespace Stats.Core.Updates;

/// <summary>A newer release, ready to offer. <see cref="Version"/> is the parsed numeric version (first three
/// fields); <see cref="TagName"/> retains the full GitHub tag, including any beta suffix, for display.
/// <see cref="Sha256"/> is the lowercase hex SHA-256 of the installer asset when the release body carries a
/// machine-readable "SHA256: &lt;64 hex chars&gt;" line (see <see cref="UpdateChecker.Parse"/>); null for older
/// releases published before integrity verification, in which case <see cref="UpdateService.DownloadAsync"/>
/// falls back to size-only verification.</summary>
public sealed record UpdateInfo(Version Version, string TagName, string AssetUrl, long AssetSize, string ReleasePageUrl, string? Sha256 = null);

/// <summary>Pure parsing of GitHub release objects/lists — no I/O, no WPF. Kept separate from
/// <see cref="UpdateService"/> so the interesting logic is unit-testable without a network.</summary>
public static class UpdateChecker
{
    private const string AssetPrefix = "Stats-Setup-";
    private const string AssetSuffix = ".exe";

    /// <summary>Matches a standalone release-body line of the form "SHA256: &lt;64 hex chars&gt;" (label
    /// case-insensitive, optional surrounding whitespace). The hex group is fixed at exactly 64 characters, so
    /// lines with extra trailing characters, a short/long hash, or no hash at all do not match and are ignored —
    /// old releases without this line simply produce no match.</summary>
    private static readonly Regex Sha256LinePattern = new(
        @"^[ \t]*SHA256[ \t]*:[ \t]*([0-9a-fA-F]{64})[ \t]*\r?$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>Selects the newest eligible stable or opted-in beta release. Drafts, malformed entries,
    /// unsupported suffixes, older/equal versions, dev builds and missing exact-name installer assets
    /// produce no offer. Informational version distinguishes installed beta builds with equal numeric cores.</summary>
    public static UpdateInfo? Parse(string latestReleaseJson, Version current, bool includePrereleases = false,
        string? currentInformationalVersion = null)
    {
        if (string.IsNullOrWhiteSpace(latestReleaseJson)) return null;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(latestReleaseJson); }
        catch (JsonException) { return null; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                UpdateInfo? best = null;
                foreach (var release in root.EnumerateArray())
                {
                    var candidate = Parse(release.GetRawText(), current, includePrereleases, currentInformationalVersion);
                    if (candidate is not null && (best is null || CompareTags(candidate.TagName, best.TagName) > 0)) best = candidate;
                }
                return best;
            }
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("draft", out var draft) && draft.ValueKind != JsonValueKind.False) return null;
            if (!root.TryGetProperty("tag_name", out var tagProp) || tagProp.ValueKind != JsonValueKind.String) return null;
            var tag = tagProp.GetString();
            if (string.IsNullOrWhiteSpace(tag)) return null;

            var versionPart = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag[1..] : tag;
            if (!TryReleaseVersion(versionPart, out var latest, out var beta)) return null;
            if (!includePrereleases && beta is not null) return null;
            if (root.TryGetProperty("prerelease", out var prerelease) && prerelease.ValueKind != JsonValueKind.False
                && (!includePrereleases || beta is null || prerelease.ValueKind != JsonValueKind.True)) return null;
            var currentNormalized = NormalizeToThree(current);
            if (currentNormalized == new Version(0, 0, 0)) return null; // dev build: never offer
            var currentTag = currentNormalized.ToString();
            var informational = currentInformationalVersion?.Split('+')[0];
            if (informational is not null && TryReleaseVersion(informational, out var installed, out _) && installed == currentNormalized)
                currentTag = informational;
            if (CompareTags(versionPart, currentTag) <= 0) return null;

            var releasePageUrl = root.TryGetProperty("html_url", out var htmlProp) && htmlProp.ValueKind == JsonValueKind.String
                ? htmlProp.GetString() ?? "" : "";

            var sha256 = ParseSha256(root);

            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

            var expectedName = AssetPrefix + versionPart + AssetSuffix;
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object) continue;
                if (!asset.TryGetProperty("name", out var nameProp) || nameProp.ValueKind != JsonValueKind.String) continue;
                if (!string.Equals(nameProp.GetString(), expectedName, StringComparison.Ordinal)) continue;
                if (!asset.TryGetProperty("browser_download_url", out var urlProp) || urlProp.ValueKind != JsonValueKind.String) continue;
                if (!asset.TryGetProperty("size", out var sizeProp) || sizeProp.ValueKind != JsonValueKind.Number) continue;

                var assetUrl = urlProp.GetString();
                if (string.IsNullOrEmpty(assetUrl) || !IsAllowedAssetHost(assetUrl)) continue;

                if (!sizeProp.TryGetInt64(out var size) || size <= 0) continue;
                return new UpdateInfo(latest, tag, assetUrl, size, releasePageUrl, sha256);
            }
            return null; // no asset with the expected exact name (and an allowed host)
        }
    }

    private static bool TryReleaseVersion(string tag, out Version version, out int? beta)
    {
        version = new Version(0, 0, 0);
        beta = null;
        var match = Regex.Match(tag, @"^v?([0-9]+\.[0-9]+\.[0-9]+)(-beta(?:\.(0|[1-9][0-9]*))?)?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (!match.Success || !Version.TryParse(match.Groups[1].Value, out var parsed)) return false;
        version = parsed;
        if (!match.Groups[2].Success) return true;
        if (!match.Groups[3].Success) { beta = 0; return true; }
        if (!int.TryParse(match.Groups[3].Value, out var number)) return false;
        beta = number;
        return true;
    }

    private static int CompareTags(string left, string right)
    {
        TryReleaseVersion(left, out var a, out var ab);
        TryReleaseVersion(right, out var b, out var bb);
        var numeric = a.CompareTo(b);
        if (numeric != 0) return numeric;
        if (ab is null) return bb is null ? 0 : 1;
        return bb is null ? -1 : ab.Value.CompareTo(bb.Value);
    }

    /// <summary>Reads the release "body" (markdown text), looking for a single machine-readable
    /// "SHA256: &lt;64 hex chars&gt;" line (see <see cref="Sha256LinePattern"/>). Returns the hash normalized to
    /// lowercase, or null when the body is absent/not a string, has no such line, or the line is malformed
    /// (wrong length, extra trailing characters, etc.) — old releases without the line simply return null and
    /// <see cref="UpdateService.DownloadAsync"/> continues with size-only verification.</summary>
    private static string? ParseSha256(JsonElement root)
    {
        if (!root.TryGetProperty("body", out var bodyProp) || bodyProp.ValueKind != JsonValueKind.String) return null;
        var body = bodyProp.GetString();
        if (string.IsNullOrEmpty(body)) return null;

        var match = Sha256LinePattern.Match(body);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    /// <summary>First three fields only; an unset Minor/Build (-1, from parsing e.g. "1") normalizes to 0.</summary>
    private static Version NormalizeToThree(Version v) =>
        new(Math.Max(v.Major, 0), Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    /// <summary>True when the first three version components are all zero — a local/dev build with no version
    /// stamped by CI. Such a build never checks for updates (see <see cref="Parse"/>) and the About section
    /// never offers a manual check for one either.</summary>
    public static bool IsDevBuild(Version v) => v.Major == 0 && v.Minor == 0 && v.Build <= 0;

    /// <summary>About-section display text: "Development build" for <see cref="IsDevBuild"/>, otherwise
    /// "vMAJOR.MINOR.BUILD" (matching the three-field scheme <see cref="Parse"/> compares against).</summary>
    public static string FormatVersionDisplay(Version v, string? informationalVersion = null)
    {
        if (IsDevBuild(v)) return "Development build";
        var tag = informationalVersion?.Split('+')[0];
        return tag is not null && TryReleaseVersion(tag, out var parsed, out _) && parsed == NormalizeToThree(v)
            ? "v" + tag.TrimStart('v', 'V') : $"v{v.Major}.{v.Minor}.{v.Build}";
    }

    /// <summary>Guards against a compromised/malformed API response pointing the installer download at an
    /// attacker-controlled host: only github.com, a github.com subdomain, or a githubusercontent.com subdomain
    /// (where release assets are actually served from) are accepted.</summary>
    private static bool IsAllowedAssetHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        var host = uri.Host;
        return string.Equals(host, "github.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }
}
