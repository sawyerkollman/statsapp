using Stats.Core.Fans;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

public class OverlayStatusComposerTests
{
    private static readonly IReadOnlyList<FanChannelStatus> NoChannels = Array.Empty<FanChannelStatus>();
    private static readonly IReadOnlyList<FanChannelStatus> Healthy = new[] { FanChannelStatus.Active, FanChannelStatus.Idle };

    [Fact]
    public void Compose_NothingActive_ReturnsEmpty()
    {
        var status = OverlayStatusComposer.Compose(false, null, NoChannels, null, null);
        Assert.Equal(OverlayStatus.Empty, status);
        Assert.Equal("", status.Text);
        Assert.False(status.IsWarning);
    }

    [Fact]
    public void Compose_FanEnabledWithProfile_ShowsFansProfile()
    {
        var status = OverlayStatusComposer.Compose(true, "Balanced", Healthy, null, null);
        Assert.Equal("Fans: Balanced", status.Text);
        Assert.False(status.IsWarning);
    }

    [Fact]
    public void Compose_FanEnabledNoProfile_ShowsCustom()
    {
        var status = OverlayStatusComposer.Compose(true, null, Healthy, null, null);
        Assert.Equal("Fans: Custom", status.Text);
    }

    [Fact]
    public void Compose_FanDisabled_OmitsFanSegment()
    {
        // Even with a WriteFailed status present, a disabled master switch shows nothing and warns nothing.
        var statuses = new[] { FanChannelStatus.WriteFailed };
        var status = OverlayStatusComposer.Compose(false, "Balanced", statuses, null, null);
        Assert.Equal("", status.Text);
        Assert.False(status.IsWarning);
    }

    [Fact]
    public void Compose_FanEnabledNoChannels_OmitsFanSegment()
    {
        var status = OverlayStatusComposer.Compose(true, "Balanced", NoChannels, null, null);
        Assert.Equal("", status.Text);
    }

    [Fact]
    public void Compose_WriteFailed_AppendsSuffixAndWarns()
    {
        var statuses = new[] { FanChannelStatus.WriteFailed, FanChannelStatus.Active };
        var status = OverlayStatusComposer.Compose(true, "Balanced", statuses, null, null);
        Assert.Equal("Fans: Balanced — write failed", status.Text);
        Assert.True(status.IsWarning);
    }

    [Fact]
    public void Compose_SourceUnavailable_AppendsSuffixAndWarns()
    {
        var statuses = new[] { FanChannelStatus.SourceUnavailable, FanChannelStatus.Idle };
        var status = OverlayStatusComposer.Compose(true, "Balanced", statuses, null, null);
        Assert.Equal("Fans: Balanced — source unavailable", status.Text);
        Assert.True(status.IsWarning);
    }

    [Fact]
    public void Compose_WriteFailedWinsOverSourceUnavailable()
    {
        var statuses = new[] { FanChannelStatus.WriteFailed, FanChannelStatus.SourceUnavailable };
        var status = OverlayStatusComposer.Compose(true, "Balanced", statuses, null, null);
        Assert.Equal("Fans: Balanced — write failed", status.Text);
        Assert.True(status.IsWarning);
    }

    [Fact]
    public void Compose_GameMode_PassedThroughTrimmed()
    {
        var status = OverlayStatusComposer.Compose(false, null, NoChannels, "  Game mode: desktop  ", null);
        Assert.Equal("Game mode: desktop", status.Text);
        Assert.False(status.IsWarning);
    }

    [Fact]
    public void Compose_FrameReason_TrimsTrailingPeriodAndWarns()
    {
        var status = OverlayStatusComposer.Compose(false, null, NoChannels, null, "PresentMon: access denied.");
        Assert.Equal("PresentMon: access denied", status.Text);
        Assert.True(status.IsWarning);
    }

    [Fact]
    public void Compose_JoinsPresentSegmentsInFixedOrder()
    {
        var statuses = new[] { FanChannelStatus.Active };
        var status = OverlayStatusComposer.Compose(true, "Balanced", statuses, "Game mode: desktop", "PresentMon: access denied.");
        Assert.Equal("Fans: Balanced · Game mode: desktop · PresentMon: access denied", status.Text);
        Assert.True(status.IsWarning);
    }

    [Fact]
    public void Compose_WhitespaceSegments_AreOmitted()
    {
        var status = OverlayStatusComposer.Compose(false, null, NoChannels, "  ", "  ");
        Assert.Equal("", status.Text);
        Assert.False(status.IsWarning);
    }
}
