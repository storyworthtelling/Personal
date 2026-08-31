using System.Text.Json;
using System.Text.Json.Serialization;
using Codex.Domain.Audio;

namespace Codex.Infrastructure.Podcasts;

/// <summary>
/// JSON encode/decode for what is downloaded, so the Android store stays a thin prefs
/// adapter and the (de)serialization is unit-tested.
///
/// The download dates are the reason this is persisted at all: the files themselves
/// are on disk either way, but when each arrived is knowable from nowhere else, and
/// without it the sweep has no idea what has been held too long.
///
/// Enums are written as names rather than numbers — this file outlives the code that
/// wrote it, and a value inserted into the enum later would silently re-map every
/// stored kind.
/// </summary>
public static class DownloadBudgetCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Encode(DownloadBudget budget) =>
        JsonSerializer.Serialize(budget.Items, Options);

    /// <summary>
    /// What is held, or nothing. A garbled value forgets the dates — the files are
    /// still there and will be swept a little late — where throwing would take the
    /// app down on launch, which is worse and cannot be recovered from on the device.
    /// </summary>
    public static DownloadBudget Decode(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return DownloadBudget.Empty;
        }

        try
        {
            var items = JsonSerializer.Deserialize<List<DownloadedItem>>(json, Options);
            return items is null ? DownloadBudget.Empty : new DownloadBudget(items);
        }
        catch (JsonException)
        {
            return DownloadBudget.Empty;
        }
    }
}
