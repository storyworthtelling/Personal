using System.Globalization;
using Android.Content;
using Codex.Ui.Voice;
using Pulp.Domain.Voice;

namespace Pulp.Watching;

/// <summary>
/// The home's two voiced seats — the greeting under the header (D118) and the empty page's invitation
/// (D264) — composed in the owner's voice through the shared <see cref="VoiceGreeting"/> (Score's
/// path: the shared profile and the vault's key, read with this phone's Google account). What a seat
/// was last given is remembered per seat, facts and day, so <see cref="VoiceLinePlan"/> can say
/// "reuse" instead of billing a model call per look at the home.
///
/// <para>Score's first-open-of-day context is kept the same way Score keeps it: the day is remembered
/// when the home first loads, and the flag holds for the rest of that process's day so the greeting's
/// facts do not change under it between paints.</para>
/// </summary>
public sealed class WatchingVoice
{
    private const string PrefsName = "pulp-watching-voice";
    private const string OpenDayKey = "openDay";
    private const string DayFormat = "yyyy-MM-dd";

    private static DateOnly? firstOpenDay;

    private readonly ISharedPreferences prefs;
    private readonly Lazy<VoiceGreeting> composer;

    public WatchingVoice(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private)
            ?? throw new InvalidOperationException($"The {PrefsName} preferences could not be opened.");
        composer = new Lazy<VoiceGreeting>(() => new VoiceGreeting(new VoiceGoogleAccount().GetAccessToken, "Pulp"));
    }

    /// <summary>Whether a voice could be reached at all: with no Google account on this phone there is no profile to read.</summary>
    public static bool Ready => VoiceGoogleAccount.IsSignedIn;

    /// <summary>True for the first home of the day, and for the rest of this process's day after it.</summary>
    public bool FirstOpenOfDay(DateOnly today)
    {
        if (firstOpenDay == today)
        {
            return true;
        }

        var stored = prefs.GetString(OpenDayKey, null);
        var first = !string.Equals(stored, Format(today), StringComparison.Ordinal);
        prefs.Edit()!.PutString(OpenDayKey, Format(today))!.Apply();
        if (first)
        {
            firstOpenDay = today;
        }

        return first;
    }

    public RememberedLine? Remembered(VoiceSeat seat)
    {
        var hash = prefs.GetString(Key(seat, "hash"), null);
        var day = prefs.GetString(Key(seat, "day"), null);
        if (hash is null || day is null
            || !DateOnly.TryParseExact(day, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return null;
        }

        return new RememberedLine(seat, hash, parsed, prefs.GetString(Key(seat, "line"), null));
    }

    /// <summary>
    /// Composes the seat's line, or the empty string when there is no profile, key or answer — never
    /// throws, as <see cref="VoiceGreeting"/> never does — and remembers the attempt either way, since
    /// an empty answer is still the day's attempt for these facts.
    /// </summary>
    public async Task<string> ComposeAsync(VoiceFacts facts, DateOnly today, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var line = await composer.Value.ComposeAsync(facts.Occasion.Purpose, facts.Context, cancellationToken);
        prefs.Edit()!
            .PutString(Key(facts.Seat, "hash"), facts.Hash)!
            .PutString(Key(facts.Seat, "day"), Format(today))!
            .PutString(Key(facts.Seat, "line"), line)!
            .Apply();
        return line;
    }

    private static string Key(VoiceSeat seat, string part) => $"{seat}.{part}";

    private static string Format(DateOnly day) => day.ToString(DayFormat, CultureInfo.InvariantCulture);
}
