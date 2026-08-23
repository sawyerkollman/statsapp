namespace Stats.Core.Settings;

/// <summary>A saved fan configuration: one <see cref="FanChannelPref"/> per channel id. Channels absent from
/// the profile revert to Auto when the profile is applied (see <see cref="Fans.FanController.ApplyProfile"/>).</summary>
public sealed class FanProfile
{
    public string Name { get; set; } = "";
    public Dictionary<string, FanChannelPref> Channels { get; set; } = new();
}
