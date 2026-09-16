using System.Net;
using System.Text.Json;
using Stats.Core.Updates;
using Stats.Core.ViewModels;
using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.Tests;

public sealed class BetaUpdateTests
{
    private static string Release(string version, bool prerelease = false, bool draft = false, string? asset = null, long size = 42) =>
        JsonSerializer.Serialize(new
        {
            tag_name = "v" + version, prerelease, draft,
            html_url = "https://github.com/sawyerkollman/statsapp/releases/tag/v" + version,
            body = "SHA256: " + new string('a', 64),
            assets = new[] { new { name = asset ?? $"Stats-Setup-{version}.exe", size,
                browser_download_url = "https://github.com/sawyerkollman/statsapp/releases/download/v" + version + "/installer.exe" } }
        });

    [Theory]
    [InlineData("1.11.0-beta.2", "1.11.0-beta.10", true, true)]
    [InlineData("1.11.0-beta.10", "1.11.0-beta.2", true, false)]
    [InlineData("1.11.0-beta.2", "1.11.0-beta.2", true, false)]
    [InlineData("1.11.0-beta.10", "1.11.0", false, true)]
    [InlineData("1.11.0", "1.11.0-beta.10", true, false)]
    [InlineData("1.11.0-beta.10", "1.10.0", false, false)]
    [InlineData("1.11.0-beta.2", "1.12.0-beta.1", false, false)]
    public void OrdersBetasAndStableWithoutDowngrade(string installed, string candidate, bool optedIn, bool offered)
    {
        var current = Version.Parse(installed.Split('-')[0]);
        var info = UpdateChecker.Parse(Release(candidate, candidate.Contains('-')), current, optedIn, installed + "+abc123");
        Assert.Equal(offered, info is not null);
    }

    [Fact]
    public void ChoosesHighestEligibleRelease_NotApiOrder_AndRejectsDraftMalformedAssetsAndFlags()
    {
        var json = "[" + string.Join(',', Release("1.12.0-beta.2", true), Release("1.12.0-beta.10", true),
            Release("1.12.0"), Release("9.0.0", draft: true), Release("8.0.0", prerelease: true),
            Release("7.0.0-beta.1", true, asset: "wrong.exe"), Release("6.0.0-beta.1", true, size: -1)) + "]";
        Assert.Equal("v1.12.0", UpdateChecker.Parse(json, new(1, 10, 0), true)!.TagName);
        Assert.Equal("v1.12.0", UpdateChecker.Parse(json, new(1, 10, 0))!.TagName);
        Assert.Null(UpdateChecker.Parse(Release("1.12.0-beta.1"), new(1, 10, 0))); // mislabeled beta is still excluded
        Assert.Null(UpdateChecker.Parse(Release("1.12.0", prerelease: true), new(1, 10, 0), true));
        Assert.Null(UpdateChecker.Parse(Release("1.12.0-rc.1", prerelease: true), new(1, 10, 0), true));
        Assert.Null(UpdateChecker.Parse(json, new(0, 0, 0), true));
    }

    [Fact]
    public void BetaIdentityIsVisibleAndRetainsDownloadHash()
    {
        Assert.Equal("v1.11.0-beta.3", UpdateChecker.FormatVersionDisplay(new(1, 11, 0), "1.11.0-beta.3+abcdef"));
        Assert.Equal("Development build", UpdateChecker.FormatVersionDisplay(new(0, 0, 0), "0.0.0-dev"));
        var info = UpdateChecker.Parse(Release("1.11.0-beta.3", true), new(1, 10, 0), true)!;
        Assert.Equal(new string('a', 64), info.Sha256);
    }

    [Theory]
    [InlineData(false, "/repos/sawyerkollman/statsapp/releases/latest")]
    [InlineData(true, "/repos/sawyerkollman/statsapp/releases?per_page=100")]
    public async Task CheckUsesChannelEndpointAndInstalledBetaIdentity(bool optedIn, string expectedPath)
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal(expectedPath, request.RequestUri!.PathAndQuery);
            Assert.NotEmpty(request.Headers.UserAgent);
            var release = Release("1.11.0");
            return new(HttpStatusCode.OK) { Content = new StringContent(optedIn ? "[" + release + "]" : release) };
        }));
        using var service = new UpdateService(http);
        var update = await service.CheckAsync(new(1, 11, 0), includePrereleases: optedIn, currentInformationalVersion: "1.11.0-beta.2");
        Assert.Equal("v1.11.0", update!.TagName);
    }

    [Fact]
    public void ClearingChannelOfferPreventsOldInstall_AndBackgroundOffersCannotReplaceActiveDownload()
    {
        var vm = new DashboardViewModel(new MetricStore([]), new AppSettings(), () => { });
        var old = new UpdateInfo(new(1, 11, 0), "v1.11.0-beta.1", "", 42, "");
        var installs = 0;
        vm.InstallUpdateRequested += _ => installs++;
        vm.OfferUpdate(old);
        vm.SetUpdateProgress(0.5);
        vm.OfferUpdate(old with { TagName = "v1.12.0" });
        Assert.True(vm.UpdateBusy);
        Assert.Contains("beta.1", vm.UpdateNotice);
        vm.ClearUpdateOffer();
        vm.InstallUpdateCommand.Execute(null);
        Assert.Equal(0, installs);
        Assert.False(vm.UpdateAvailable);
        Assert.False(vm.UpdateBusy);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
