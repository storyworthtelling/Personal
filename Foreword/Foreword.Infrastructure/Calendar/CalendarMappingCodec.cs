using System.Text.Json;
using System.Text.Json.Serialization;
using Foreword.Domain.Models;

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
/// <para>The load-bearing detail is that a column is stored as its <b>word</b>, never a number.
/// The old enum was written through <see cref="JsonStringEnumConverter"/>, so every column in every
/// stored document is a name; <see cref="PersonColumn"/> reads and writes exactly that, which is what
/// lets a document the old app wrote keep meaning what it meant.</para>
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

    /// <summary>
    /// The calendar settings, migrated to this app's shape if an older app wrote them. Every reader
    /// of the mapping goes through here, so the delivery and the screens can never see two
    /// different layouts of one document.
    /// </summary>
    public static CalendarMappingOptions? ReadMapping(string json) =>
        Read<CalendarMappingOptions>(json) is { } mapping ? CalendarMappingMigration.Apply(mapping) : null;
}
