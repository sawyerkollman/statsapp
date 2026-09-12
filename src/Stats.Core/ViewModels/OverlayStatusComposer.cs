using Stats.Core.Fans;

namespace Stats.Core.ViewModels;

/// <summary>One composed overlay status strip: the text and whether any segment is a fault (warn styling + glyph).</summary>
public sealed record OverlayStatus(string Text, bool IsWarning)
{
    public static readonly OverlayStatus Empty = new("", false);
}

/// <summary>Pure composer for the overlay's optional status strip (design:
/// docs/superpowers/specs/2026-09-11-overlay-sparklines-design.md). Lives in Core so it is unit-tested without
/// WPF; the composition root (<c>App.xaml.cs</c>) only gathers already-published inputs on the UI thread and
/// pushes the result into <see cref="OverlayViewModel.SetStatus"/>.</summary>
public static class OverlayStatusComposer
{
    public const string Separator = " · ";
    public const string FanPrefix = "Fans: ";
    public const string CustomProfile = "Custom";
    public const string WriteFailedSuffix = " — write failed";
    public const string SourceUnavailableSuffix = " — source unavailable";

    /// <summary>Pure. Segments in fixed order Fans · Game mode · PresentMon; a segment is omitted when its source
    /// has nothing to say; an empty result is <see cref="OverlayStatus.Empty"/>.</summary>
    public static OverlayStatus Compose(
        bool fanControlEnabled, string? activeFanProfile, IReadOnlyList<FanChannelStatus> fanChannelStatuses,
        string? gameModeStatus,
        string? frameReason)
    {
        var segments = new List<string>(3);
        bool isWarning = false;

        if (fanControlEnabled && fanChannelStatuses.Count > 0)
        {
            var name = string.IsNullOrWhiteSpace(activeFanProfile) ? CustomProfile : activeFanProfile!.Trim();
            var segment = FanPrefix + name;
            if (fanChannelStatuses.Contains(FanChannelStatus.WriteFailed))
            {
                segment += WriteFailedSuffix;
                isWarning = true;
            }
            else if (fanChannelStatuses.Contains(FanChannelStatus.SourceUnavailable))
            {
                segment += SourceUnavailableSuffix;
                isWarning = true;
            }
            segments.Add(segment);
        }

        if (!string.IsNullOrWhiteSpace(gameModeStatus))
            segments.Add(gameModeStatus.Trim());

        if (!string.IsNullOrWhiteSpace(frameReason))
        {
            segments.Add(frameReason.Trim().TrimEnd('.'));
            isWarning = true;
        }

        return segments.Count == 0 ? OverlayStatus.Empty : new OverlayStatus(string.Join(Separator, segments), isWarning);
    }
}
