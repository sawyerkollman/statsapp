using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stats.Core.Settings;

// Append only, like DashboardLayoutMode/MetricGroup: never reorder or remove a member, only add — Histogram (5)
// and FpsSummary (6) were appended here, pinned by TileKindTests.
public enum TileKind { Auto, Sparkline, Gauge, Bar, Value, Histogram, FpsSummary }
public enum TileSize { S, M, L }

/// <summary>Lenient string converter for <see cref="TileKind"/>: an unrecognized token (a future version's new
/// kind, a hand-edited typo, a stray number/null/container) deserializes to <see cref="TileKind.Auto"/> instead of
/// throwing. Copy of <see cref="DashboardLayoutModeConverter"/> (see that type's remarks for why this matters more
/// than it would for an ordinary string-enum setting) with <see cref="TileKind"/>/<see cref="TileKind.Auto"/>
/// substituted. Serializes the same way the global converter would (the enum's member name), so existing/valid
/// values round-trip identically either way.</summary>
public sealed class TileKindConverter : JsonConverter<TileKind>
{
    public override TileKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // A hand-edited "Kind": {} or [] leaves the reader positioned on the container's start token; every other
        // bad value (a string that doesn't parse, a number, null, bool) already leaves the reader positioned on
        // the scalar it read. Consuming to the matching end token here keeps the whole-object
        // JsonSerializer.Deserialize<AppSettings> call from throwing "read too much or not enough" afterwards.
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) reader.Skip();

        return reader.TokenType == JsonTokenType.String
            && Enum.TryParse<TileKind>(reader.GetString(), ignoreCase: true, out var kind)
            && Enum.IsDefined(kind)
                ? kind
                : TileKind.Auto;
    }

    public override void Write(Utf8JsonWriter writer, TileKind value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

/// <summary>Per-tile user preferences keyed by metric id.</summary>
public sealed class TilePref
{
    [JsonConverter(typeof(TileKindConverter))]
    public TileKind Kind { get; set; } = TileKind.Auto;
    public TileSize Size { get; set; } = TileSize.M;
    /// <summary>Friendly display-name override; null/blank = use sensor name.</summary>
    public string? Name { get; set; }
    /// <summary>Explicit scale max for Gauge/Bar; null = derive (limit → 100 for % → session max).</summary>
    public float? Max { get; set; }
    /// <summary>Canvas position for Free/Grid dashboard layout; null = not yet placed (seeded by
    /// <see cref="ViewModels.DashboardViewModel"/>'s pack). Never touched while <see cref="AppSettings.DashboardLayoutMode"/>
    /// is Auto.</summary>
    public double? X { get; set; }
    public double? Y { get; set; }
}
