using System.Globalization;
using System.Text.Json;
using Android.Content;
using Codex.Ui.Voice;
using Pulp.Domain.Voice;

namespace Pulp.Title;

/// <summary>
/// The why-this seat (D36, D38, D41): composed in the owner's hand at most once per title, facts and
/// day (<see cref="VoiceLinePlan"/>), remembered on this phone so a second look at the page is not a
/// second model call. Hidden until composed and hidden for good when there is no profile or key
/// (<see cref="VoiceFallback.Hide"/>): the composer degrades to an empty line, which is remembered as
/// the day's attempt. Errors are never voiced and never reach the page.
/// </summary>
internal sealed class TitleVoiceLine
{
    /// <summary>The preferences file, named once (never derived from a member name).</summary>
    public const string PreferencesName = "pulp-title-voice";

    private const string KeyPrefix = "whythis:";

    private static readonly Lazy<VoiceGreeting> Composer =
        new(() => new VoiceGreeting(new VoiceGoogleAccount().GetAccessToken, "Pulp"));

    private readonly ISharedPreferences preferences;

    public TitleVoiceLine(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)
            ?? throw new InvalidOperationException("Pulp could not open its title-voice preferences.");
    }

    /// <summary>
    /// The line to show for <paramref name="facts"/> today, composing it when the plan says so; null
    /// when the seat stays hidden. Runs the model call, so call it off the UI thread.
    /// </summary>
    public async Task<string?> LineAsync(string titleKey, VoiceFacts facts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var key = KeyPrefix + titleKey;
        var remembered = Read(key);

        switch (VoiceLinePlan.Decide(facts, voiceReady: true, remembered, today))
        {
            case VoiceLineAction.Reuse:
                return remembered!.Line;
            case VoiceLineAction.Hide:
            case VoiceLineAction.ShowNeutral:
                return null;
            case VoiceLineAction.Compose:
                var line = await Composer.Value.ComposeAsync(facts.Occasion.Purpose, facts.Context, cancellationToken);
                var kept = string.IsNullOrWhiteSpace(line) ? null : line.Trim();
                Write(key, new RememberedLine(facts.Seat, facts.Hash, today, kept));
                return kept;
            default:
                throw new InvalidOperationException("The why-this seat met a voice line action it does not know.");
        }
    }

    private RememberedLine? Read(string key)
    {
        var text = preferences.GetString(key, null);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredLine>(text);
            return stored is null || !Enum.TryParse<VoiceSeat>(stored.Seat, out var seat)
                || !DateOnly.TryParseExact(stored.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? null
                : new RememberedLine(seat, stored.Hash ?? string.Empty, day, stored.Line);
        }
        catch (JsonException exception)
        {
            // A garbled memory is only a forgotten one: the line is composed again.
            Android.Util.Log.Warn("Pulp", $"The remembered why-this line could not be read ({exception.Message}); composing again.");
            return null;
        }
    }

    private void Write(string key, RememberedLine line)
    {
        var stored = new StoredLine(line.Seat.ToString(), line.FactsHash, line.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), line.Line);
        preferences.Edit()!.PutString(key, JsonSerializer.Serialize(stored))!.Apply();
    }

    private sealed record StoredLine(string Seat, string? Hash, string Day, string? Line);
}
