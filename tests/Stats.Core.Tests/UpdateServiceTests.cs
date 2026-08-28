using System.Net;
using System.Net.Http;
using Stats.Core.Updates;

namespace Stats.Core.Tests;

public class UpdateServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }

    private static UpdateService MakeService(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)));

    [Fact]
    public async Task DownloadAsync_NonHttpsAssetUrl_Throws()
    {
        var service = MakeService(_ => new HttpResponseMessage(HttpStatusCode.OK)); // never reached
        var info = new UpdateInfo(new Version(1, 4, 2), "v1.4.2", "http://example.com/Stats-Setup-1.4.2.exe", 5, "");
        var destPath = Path.Combine(Path.GetTempPath(), $"stats-updateservice-test-{Guid.NewGuid():N}.exe");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DownloadAsync(info, destPath, progress: null, CancellationToken.None));

        Assert.False(File.Exists(destPath));
    }

    [Fact]
    public async Task DownloadAsync_SizeMismatch_DeletesPartialFileAndThrows()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5 }; // 5 bytes on the wire …
        var service = MakeService(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes),
        });
        var info = new UpdateInfo(new Version(1, 4, 2), "v1.4.2", "https://github.com/sawyerkollman/statsapp/releases/download/v1.4.2/Stats-Setup-1.4.2.exe",
            AssetSize: 999, ReleasePageUrl: ""); // … but the API said 999
        var destPath = Path.Combine(Path.GetTempPath(), $"stats-updateservice-test-{Guid.NewGuid():N}.exe");

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.DownloadAsync(info, destPath, progress: null, CancellationToken.None));

            Assert.False(File.Exists(destPath)); // the partial file must be cleaned up, not left behind
        }
        finally
        {
            if (File.Exists(destPath)) File.Delete(destPath);
        }
    }

    private sealed class SyncProgress : IProgress<double>
    {
        public readonly List<double> Reports = new();
        public void Report(double value) => Reports.Add(value);
    }

    [Fact]
    public async Task DownloadAsync_HappyPath_WritesFileAndReportsCompletion()
    {
        var bytes = new byte[200_000];
        new Random(42).NextBytes(bytes);
        var service = MakeService(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes),
        });
        var info = new UpdateInfo(new Version(1, 4, 2), "v1.4.2", "https://github.com/sawyerkollman/statsapp/releases/download/v1.4.2/Stats-Setup-1.4.2.exe",
            AssetSize: bytes.Length, ReleasePageUrl: "");
        var destPath = Path.Combine(Path.GetTempPath(), $"stats-updateservice-test-{Guid.NewGuid():N}.exe");
        var progress = new SyncProgress();

        try
        {
            await service.DownloadAsync(info, destPath, progress, CancellationToken.None);

            Assert.True(File.Exists(destPath));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(destPath));
            Assert.NotEmpty(progress.Reports);
            Assert.Equal(1.0, progress.Reports[^1]); // progress ends at 100% on success
            Assert.All(progress.Reports, r => Assert.InRange(r, 0.0, 1.0));
        }
        finally
        {
            if (File.Exists(destPath)) File.Delete(destPath);
        }
    }

    private sealed class CancelingStream : Stream
    {
        private readonly CancellationTokenSource _cts;
        private bool _cancelled;
        public CancelingStream(CancellationTokenSource cts) => _cts = cts;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_cancelled)
            {
                _cancelled = true;
                _cts.Cancel();
            }
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(0); // unreachable once cancelled, kept for completeness
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task DownloadAsync_Cancelled_ThrowsAndLeavesNoSuccessfulLookingFile()
    {
        using var cts = new CancellationTokenSource();
        var service = MakeService(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new CancelingStream(cts)),
        });
        var info = new UpdateInfo(new Version(1, 4, 2), "v1.4.2", "https://github.com/sawyerkollman/statsapp/releases/download/v1.4.2/Stats-Setup-1.4.2.exe",
            AssetSize: 200_000, ReleasePageUrl: "");
        var destPath = Path.Combine(Path.GetTempPath(), $"stats-updateservice-test-{Guid.NewGuid():N}.exe");

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.DownloadAsync(info, destPath, progress: null, cts.Token));

            if (File.Exists(destPath))
                Assert.NotEqual(info.AssetSize, new FileInfo(destPath).Length);
        }
        finally
        {
            if (File.Exists(destPath)) File.Delete(destPath);
        }
    }

    // ---- CheckAsync: quiet (automatic) vs throwOnFailure (manual, v1.7 About section) ----

    private const string NewerReleaseJson = """
        {
          "tag_name": "v1.4.2",
          "html_url": "https://github.com/sawyerkollman/statsapp/releases/tag/v1.4.2",
          "assets": [
            { "name": "Stats-Setup-1.4.2.exe", "size": 12345, "browser_download_url": "https://github.com/sawyerkollman/statsapp/releases/download/v1.4.2/Stats-Setup-1.4.2.exe" }
          ]
        }
        """;

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("simulated network failure");
    }

    [Fact]
    public async Task CheckAsync_NewerRelease_ReturnsUpdateInfo()
    {
        var service = MakeService(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(NewerReleaseJson) });
        var info = await service.CheckAsync(new Version(1, 4, 1));
        Assert.NotNull(info);
        Assert.Equal("v1.4.2", info!.TagName);
    }

    [Fact]
    public async Task CheckAsync_NoUpdate_ReturnsNullQuietly_DefaultAndManualModeAgree()
    {
        var upToDateJson = NewerReleaseJson.Replace("v1.4.2", "v1.4.1"); // tag equals current → not offered
        var service = MakeService(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(upToDateJson) });

        Assert.Null(await service.CheckAsync(new Version(1, 4, 1)));
        Assert.Null(await service.CheckAsync(new Version(1, 4, 1), throwOnFailure: true)); // still null, not an exception
    }

    [Fact]
    public async Task CheckAsync_NetworkFailure_DefaultMode_ReturnsNullQuietly()
    {
        var service = new UpdateService(new HttpClient(new ThrowingHandler()));
        var info = await service.CheckAsync(new Version(1, 4, 1));
        Assert.Null(info);
    }

    [Fact]
    public async Task CheckAsync_NetworkFailure_ThrowOnFailure_Throws()
    {
        var service = new UpdateService(new HttpClient(new ThrowingHandler()));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.CheckAsync(new Version(1, 4, 1), throwOnFailure: true));
    }

    [Fact]
    public async Task CheckAsync_NonSuccessStatus_DefaultMode_ReturnsNullQuietly()
    {
        var service = MakeService(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        Assert.Null(await service.CheckAsync(new Version(1, 4, 1)));
    }

    [Fact]
    public async Task CheckAsync_NonSuccessStatus_ThrowOnFailure_Throws()
    {
        var service = MakeService(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CheckAsync(new Version(1, 4, 1), throwOnFailure: true));
    }
}
