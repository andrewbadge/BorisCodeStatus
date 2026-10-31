using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BorisCodeStatus.Core.Models;

/// <summary>
/// Reads a timestamp that Claude Code may emit either as an ISO-8601 string or as a numeric
/// Unix epoch (seconds, or milliseconds when the value is implausibly large). Always writes ISO-8601.
/// The shape of <c>resets_at</c> is not contractually documented, so accept both rather than throw.
/// </summary>
public sealed class FlexibleDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
{
    // Anything past this many seconds is far beyond year 2286 and is therefore milliseconds.
    private const long MaxPlausibleSeconds = 10_000_000_000L;

    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.Number:
                if (!reader.TryGetInt64(out var epoch))
                {
                    return null;
                }
                return FromEpoch(epoch);

            case JsonTokenType.String:
                var text = reader.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }
                if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
                {
                    return parsed;
                }
                // Some producers stringify the epoch.
                return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stringEpoch)
                    ? FromEpoch(stringEpoch)
                    : null;

            default:
                reader.Skip();
                return null;
        }
    }

    private static DateTimeOffset FromEpoch(long value) => value > MaxPlausibleSeconds
        ? DateTimeOffset.FromUnixTimeMilliseconds(value)
        : DateTimeOffset.FromUnixTimeSeconds(value);

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value.Value.ToUniversalTime());
        }
    }
}
