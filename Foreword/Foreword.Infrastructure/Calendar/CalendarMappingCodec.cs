using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foreword.Infrastructure.Calendar;

/// <summary>
/// Reads and writes the stored calendar settings.
///
/// <para><b>The wire shape is not free to change.</b> This document already exists on the owner's
/// phone, written by the JSON editor the screens replace, and the secrets vault carries a copy of it
/// under a frozen key. A screen that wrote a different shape would orphan a configuration they
/// already have — or restore one from the vault into something nothing reads — and it would do it
/// silently, which is the failure this phase keeps finding.</para>
///
/// <para>The load-bearing detail is <see cref="JsonStringEnumConverter"/>. The old parser
/// registered it, so every column in every stored document is the <b>word</b> "Rowan", not the
/// number 0. Serialising without it writes numbers and reading without it rejects words — either
/// way the mapping comes back empty and the morning prints into one column.</para>
///
/// <para>A garbled document reads as absent rather than throwing. The owner can do something with
/// an empty list; they can do nothing with a stack trace, and a bad paste must never stop a
/// delivery.</para>
/// </summary>
public static class CalendarMappingCodec
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T? Read<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
}
