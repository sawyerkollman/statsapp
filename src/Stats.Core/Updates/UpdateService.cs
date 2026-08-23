using System.Net.Http;
using System.Net.Http.Headers;

namespace Stats.Core.Updates;

/// <summary>Owns the HTTP side of the updater: the GitHub "latest release" check and the installer download.
/// No WPF, no process/file-system work beyond writing the downloaded bytes — that keeps it usable from a unit
/// test with an injected <see cref="HttpClient"/> (a real network call is never exercised by
/// <see cref="Updates.UpdateChecker"/>'s own tests, which cover <see cref="Updates.UpdateChecker.Parse"/> directly).</summary>
public sealed class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/sawyerkollman/statsapp/releases/latest";
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;

    public UpdateService(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient();
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Stats-updater", "1"));
    }

    /// <summary>Checks GitHub for a newer release. Any failure at all — network error, timeout, non-2xx status,
    /// malformed JSON, no qualifying asset — returns null; this must never surface a visible error to the user.</summary>
    public async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(CheckTimeout);
            using var response = await _http.GetAsync(LatestReleaseUrl, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            return UpdateChecker.Parse(json, current);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Downloads the release asset to <paramref name="destPath"/>, reporting 0..1 progress. Throws on any
    /// failure (non-HTTPS URL, HTTP error, or a final size mismatch against <see cref="UpdateInfo.AssetSize"/>) —
    /// the caller is expected to catch and show "download failed — retry".</summary>
    public async Task DownloadAsync(UpdateInfo info, string destPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(info.AssetUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Update asset URL must be HTTPS.");

        using (var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? info.AssetSize;

            await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long readTotal = 0;
                int read;
                while ((read = await httpStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    readTotal += read;
                    if (total > 0) progress?.Report((double)readTotal / total);
                }
            }
        }

        var actualSize = new FileInfo(destPath).Length;
        if (actualSize != info.AssetSize)
        {
            try { File.Delete(destPath); } catch (Exception) { /* best effort cleanup */ }
            throw new InvalidOperationException($"Downloaded file size {actualSize} does not match the expected {info.AssetSize}.");
        }
        progress?.Report(1.0);
    }
}
