using System.Text.Json;
using Android.Content;
using Codex.Domain.Music;
using Codex.Infrastructure.Music;

namespace Score;

/// <summary>
/// The on-device home of the week's crate — the raw title + track ids the Friday
/// worker chose, when it was generated, and what has been struck out of it. Ids are
/// re-resolved against the live catalog at display time (via PlaylistPlan), so a
/// changed library never leaves a stale record in the crate.
///
/// There is no "dismissed" flag any more. The shelf it belonged to was something you
/// got rid of; a crate is something you go to.
/// </summary>
public class FridayPicksStore
{
    private const string PreferencesName = "fridayPicks";
    private const string SelectionKey = "selection";
    private const string GeneratedKey = "generatedUtc";
    private const string VetoedKey = "vetoed";

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

    /// <summary>
    /// The ids struck out of this week's crate. Held here rather than in the crate
    /// because a strike has to survive leaving the tab — a veto undone by walking
    /// away from it is not a veto — and because the week's strikes are what gets
    /// folded into the taste profile when the stack is replaced.
    /// </summary>
    public IReadOnlyList<string> Vetoed
    {
        get
        {
            var json = preferences.GetString(VetoedKey, null);
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            try
            {
                return JsonSerializer.Deserialize<List<string>>(json, SerializerOptions) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
        set => preferences.Edit()!.PutString(VetoedKey, JsonSerializer.Serialize(value, SerializerOptions))!.Apply();
    }

    /// <summary>
    /// The week's picks. Records the owner does not have, so they are held by name
    /// rather than by a library id — there is no id for something not in the library,
    /// which is what the old shape got wrong.
    /// </summary>
    public IReadOnlyList<Recommendation> LoadPicks()
    {
        var json = preferences.GetString(SelectionKey, null);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            // A crate written by the previous shape was a title plus library ids. It
            // cannot be converted into records the owner does not own, so it reads as
            // empty and Friday fills it properly.
            return JsonSerializer.Deserialize<List<Recommendation>>(json, SerializerOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Replaces the week's crate. The strikes go with the stack they belonged to:
    /// Friday replaces Friday, nothing carries over, and a veto held over from a run
    /// that is gone must not colour the new one.
    /// </summary>
    public void Save(IReadOnlyList<Recommendation> picks, DateTimeOffset generatedUtc) =>
        preferences.Edit()!
            .PutString(SelectionKey, JsonSerializer.Serialize(picks, SerializerOptions))!
            .PutLong(GeneratedKey, generatedUtc.UtcTicks)!
            .PutString(VetoedKey, JsonSerializer.Serialize(Array.Empty<string>(), SerializerOptions))!
            .Apply();
}
