using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stats.Core.Settings;

/// <summary>Whether overlay tiles draw a sparkline beside/below their value (design:
/// docs/superpowers/specs/2026-09-11-overlay-sparklines-design.md). Append-only, like
/// <see cref="DashboardLayoutMode"/> — never reorder or remove a member, only add.</summary>
public enum OverlayGraphs
{
    /// <summary>No sparkline; the overlay tile shows only its label and value (today's behavior).</summary>
    None,
    /// <summary>A compact sparkline beside (horizontal) or below (vertical) the value, matching the dashboard's
    /// history window and Smooth lines / Glow and motion effects.</summary>
    Sparkline,
}

/// <summary>Lenient string converter for <see cref="OverlayGraphs"/>: a verbatim sibling of
/// <see cref="DashboardLayoutModeConverter"/>. An unrecognized token (a future version's new mode, a hand-edited
/// typo, a stray number/null) deserializes to <see cref="OverlayGraphs.Sparkline"/> — the default, *not*
/// <c>default(enum)</c> — instead of throwing. This matters more than it would for the app's other string-enum
/// settings — the global <c>JsonStringEnumConverter</c> in <see cref="SettingsService"/> throws on an unrecognized
/// value, and that exception propagates out of the whole-object <c>JsonSerializer.Deserialize&lt;AppSettings&gt;</c>
/// call, which <see cref="SettingsService.Load"/> only catches by discarding the *entire* settings object and
/// starting over from defaults — silently reverting every other setting the user has, not just this one. Scoping a
/// lenient converter to just this property avoids that blast radius entirely. Serializes the same way the global
/// converter would (the enum's member name), so existing/valid values round-trip identically either way.</summary>
public sealed class OverlayGraphsConverter : JsonConverter<OverlayGraphs>
{
    public override OverlayGraphs Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // A hand-edited "OverlayGraphs": {} or [] leaves the reader positioned on the container's start token;
        // every other bad value (a string that doesn't parse, a number, null, bool) already leaves the reader
        // positioned on the scalar it read. Consuming to the matching end token here keeps the whole-object
        // JsonSerializer.Deserialize<AppSettings> call from throwing "read too much or not enough" afterwards.
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) reader.Skip();

        return reader.TokenType == JsonTokenType.String
            && Enum.TryParse<OverlayGraphs>(reader.GetString(), ignoreCase: true, out var mode)
                ? mode
                : OverlayGraphs.Sparkline;
    }

    public override void Write(Utf8JsonWriter writer, OverlayGraphs value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
