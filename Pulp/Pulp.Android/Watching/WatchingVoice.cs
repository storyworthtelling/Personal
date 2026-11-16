using System.Globalization;
using Android.Content;
using Codex.Ui.Voice;
using Pulp.Domain.Voice;
using SeatMemory = Storyworthtelling.CallSheet.SeatMemory;
using SeatMemoryFile = Storyworthtelling.CallSheet.SeatMemoryFile;
using SeatPlan = Storyworthtelling.CallSheet.SeatPlan;

namespace Pulp.Watching;

/// <summary>
/// The home's two voiced seats — the greeting under the header (D118) and the empty page's invitation
/// (D264) — composed in the owner's voice through the shared <see cref="VoiceGreeting"/> (Score's
/// path: the shared profile and the vault's key, read with this phone's Google account). What a seat
/// was last given is kept in the suite's one memory file (audit W10.3), so <see cref="VoiceLinePlan"/>
/// can say "reuse" instead of billing a model call per look at the home.
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
    private readonly SeatMemoryFile memory;

    public WatchingVoice(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private)
            ?? throw new InvalidOperationException($"The {PrefsName} preferences could not be opened.");
        // What each seat was last given, in the suite's one memory file (audit W10.3).
        memory = new SeatMemoryFile(Path.Combine(context.FilesDir!.AbsolutePath, "voice-seats.json"));
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

    /// <summary>What the seat was last given, for this subject (a child's own line is their own).</summary>
    public SeatMemory? Remembered(VoiceFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return memory.Get(facts.Occasion.Line, facts.Subject);
    }

    /// <summary>
    /// Composes the seat's line, or the empty string when there is no profile, key or answer — never
    /// throws, as <see cref="VoiceGreeting"/> never does — and remembers the attempt either way, so an empty
    /// answer is believed for <see cref="SeatPlan.RetryAfter"/> and then asked again.
    /// </summary>
    public async Task<string> ComposeAsync(VoiceFacts facts, DateOnly today, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var line = await composer.Value.ComposeAsync(facts.Occasion.Line, facts.Context, cancellationToken);
        memory.Put(facts.Occasion.Line, facts.Subject, SeatPlan.Remember(facts.Hash, today, line, DateTimeOffset.Now));
        return line;
    }


    private static string Format(DateOnly day) => day.ToString(DayFormat, CultureInfo.InvariantCulture);
}
