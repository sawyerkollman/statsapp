using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stats.Core.Settings;

/// <summary>How dashboard tiles are arranged (design: docs/superpowers/specs/2026-09-11-dashboard-layout-modes-design.md).
/// Append-only, like <see cref="Metrics.MetricGroup"/> — never reorder or remove a member, only add.</summary>
public enum DashboardLayoutMode
{
    /// <summary>Today's behavior: collapsible groups, WrapPanel per group, same-group drag reorder.</summary>
    Auto,
    /// <summary>One scrollable canvas; every tile (and the core-matrix block) sits at a persisted, freely-dragged X/Y.</summary>
    Free,
    /// <summary>Same as Free, but a dropped/nudged position snaps to <see cref="DashboardLayout.GridSize"/>; overlap is allowed.</summary>
    Grid,
}

/// <summary>Lenient string converter for <see cref="DashboardLayoutMode"/>: an unrecognized token (a future
/// version's new mode, a hand-edited typo, a stray number/null) deserializes to <see cref="DashboardLayoutMode.Auto"/>
/// instead of throwing. This matters more than it would for the app's other string-enum settings — the global
/// <c>JsonStringEnumConverter</c> in <see cref="SettingsService"/> throws on an unrecognized value, and that
/// exception propagates out of the whole-object <c>JsonSerializer.Deserialize&lt;AppSettings&gt;</c> call, which
/// <see cref="SettingsService.Load"/> only catches by discarding the *entire* settings object and starting over
/// from defaults — silently reverting every other setting the user has, not just this one. Scoping a lenient
/// converter to just this property (rather than, say, sanitizing in <see cref="SettingsService.Normalize"/> after
/// the fact, which never runs because deserialization never completes) avoids that blast radius entirely.
/// Serializes the same way the global converter would (the enum's member name), so existing/valid values round-trip
/// identically either way.</summary>
public sealed class DashboardLayoutModeConverter : JsonConverter<DashboardLayoutMode>
{
    public override DashboardLayoutMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // A hand-edited "DashboardLayoutMode": {} or [] leaves the reader positioned on the container's start
        // token; every other bad value (a string that doesn't parse, a number, null, bool) already leaves the
        // reader positioned on the scalar it read. Consuming to the matching end token here keeps the whole-object
        // JsonSerializer.Deserialize<AppSettings> call from throwing "read too much or not enough" afterwards.
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) reader.Skip();

        return reader.TokenType == JsonTokenType.String
            && Enum.TryParse<DashboardLayoutMode>(reader.GetString(), ignoreCase: true, out var mode)
            && Enum.IsDefined(mode)
                ? mode
                : DashboardLayoutMode.Auto;
    }

    public override void Write(Utf8JsonWriter writer, DashboardLayoutMode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
