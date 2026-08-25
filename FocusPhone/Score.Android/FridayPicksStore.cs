using System.Text.Json;
using Android.Content;
using Codex.Infrastructure.Music;

namespace Score;

/// <summary>
/// The on-device home of the latest weekly "New for you" selection — the raw title + track ids the Friday
/// worker chose, plus when it was generated and whether the owner dismissed this week's shelf. Ids are
/// re-resolved against the live catalog at display time (via PlaylistPlan), so a changed library never
/// leaves a stale track in the shelf.
/// </summary>
public class FridayPicksStore
{
    private const string PreferencesName = "fridayPicks";
    private const string SelectionKey = "selection";
    private const string GeneratedKey = "generatedUtc";
    private const string DismissedKey = "dismissed";

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ISharedPreferences preferences;

    public FridayPicksStore(Context context) =>
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;

    public DateTimeOffset? GeneratedUtc
    {
        get
        {
            var ticks = preferences.GetLong(GeneratedKey, 0);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public bool Dismissed
    {
        get => preferences.GetBoolean(DismissedKey, false);
        set => preferences.Edit()!.PutBoolean(DismissedKey, value)!.Apply();
    }

    public DiscoverySelection LoadSelection()
    {
        var json = preferences.GetString(SelectionKey, null);
        if (string.IsNullOrWhiteSpace(json))
        {
            return DiscoverySelection.Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<DiscoverySelection>(json, SerializerOptions) ?? DiscoverySelection.Empty;
        }
        catch (JsonException)
        {
            return DiscoverySelection.Empty;
        }
    }

    public void Save(DiscoverySelection selection, DateTimeOffset generatedUtc) =>
        preferences.Edit()!
            .PutString(SelectionKey, JsonSerializer.Serialize(selection, SerializerOptions))!
            .PutLong(GeneratedKey, generatedUtc.UtcTicks)!
            .PutBoolean(DismissedKey, false)!
            .Apply();
}
