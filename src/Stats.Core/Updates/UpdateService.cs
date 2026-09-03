using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Stats.Core.Updates;

/// <summary>Owns the HTTP side of the updater: the GitHub "latest release" check and the installer download.
/// No WPF, no process/file-system work beyond writing the downloaded bytes — that keeps it usable from a unit
/// test with an injected <see cref="HttpClient"/> (a real network call is never exercised by
/// <see cref="Updates.UpdateChecker"/>'s own tests, which cover <see cref="Updates.UpdateChecker.Parse"/> directly).</summary>
public sealed class UpdateService : IDisposable
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

    /// <summary>Checks GitHub for a newer release. By default (<paramref name="throwOnFailure"/> false, the
    /// automatic-check path) any failure at all — network error, timeout, non-2xx status, malformed JSON, no
    /// qualifying asset — returns null and never surfaces a visible error to the user. A manual "Check for
    /// updates" click passes <paramref name="throwOnFailure"/> true so it can tell a genuine failure apart from
    /// a legitimate "no update" null and show an explicit inline error instead of silently reporting "Up to
    /// date".</summary>
    public async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken cancellationToken = default, bool throwOnFailure = false)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(CheckTimeout);
            using var response = await _http.GetAsync(LatestReleaseUrl, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                if (throwOnFailure) throw new InvalidOperationException($"GitHub returned {(int)response.StatusCode}.");
                return null;
            }
            var json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            return UpdateChecker.Parse(json, current);
        }
        catch (Exception) when (!throwOnFailure)
        {
            return null;
        }
    }

    /// <summary>Downloads the release asset to <paramref name="destPath"/>, reporting 0..1 progress. Throws on any
    /// failure (non-HTTPS URL, HTTP error, a final size mismatch against <see cref="UpdateInfo.AssetSize"/>, or —
    /// when <see cref="UpdateInfo.Sha256"/> is present — a SHA-256 mismatch) — the caller is expected to catch and
    /// show "download failed — retry". Any failure, including cancellation mid-download, deletes whatever partial
    /// or fully-written file is at <paramref name="destPath"/> first, so a retry never has to contend with a
    /// leftover file for the <c>FileMode.CreateNew</c> staging check below, and nothing that looks like a
    /// successfully verified installer is ever left on disk.</summary>
    public async Task DownloadAsync(UpdateInfo info, string destPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(info.AssetUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Update asset URL must be HTTPS.");

        var createdDestination = false;
        try
        {
            using (var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? info.AssetSize;

                await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                // CreateNew: destPath lives in a fresh admin-only staging directory (see App.xaml.cs) — a file
                // already there means something pre-planted it, so fail loudly instead of silently overwriting it.
                await using (var fileStream = new FileStream(destPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    createdDestination = true;
                    var buffer = new byte[81920];
                    long readTotal = 0;
                    int read;
                    while ((read = await httpStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        readTotal += read;
                        if (total > 0) progress?.Report(Math.Min(1.0, (double)readTotal / total));
                    }
                }
            }

            var actualSize = new FileInfo(destPath).Length;
            if (actualSize != info.AssetSize)
                throw new InvalidOperationException($"Downloaded file size {actualSize} does not match the expected {info.AssetSize}.");

            if (info.Sha256 is string expectedHash)
            {
                var actualHash = await ComputeSha256Async(destPath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Downloaded file SHA-256 does not match the expected hash.");
            }

            progress?.Report(1.0);
        }
        catch
        {
            try { if (createdDestination && File.Exists(destPath)) File.Delete(destPath); }
            catch (Exception) { /* best effort cleanup */ }
            throw;
        }
    }

    /// <summary>Streams the file at <paramref name="path"/> through SHA-256 without loading it fully into memory,
    /// returning the digest as lowercase hex.</summary>
    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void Dispose() => _http.Dispose();
}
