using System.Net;
using System.Net.Http;
using Stats.Core.Updates;

namespace Stats.Core.Tests;

public class UpdateServiceTests
{
    /// <summary>Minimal stub transport: <paramref name="respond"/> builds the response for every request, or a
    /// list of responses can be supplied in order via <see cref="Queue"/>. Keeps this test free of any real
    /// network call and of any third-party mocking package (Stats.Core.Tests has none).</summary>
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
}
