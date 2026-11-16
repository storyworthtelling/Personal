using Android.Content;
using Codex.Ui.Voice;
using Pulp.Domain.Voice;
using SeatMemoryFile = Storyworthtelling.CallSheet.SeatMemoryFile;
using SeatPlan = Storyworthtelling.CallSheet.SeatPlan;

namespace Pulp.Title;

/// <summary>
/// The why-this seat (D36, D38, D41): composed in the owner's hand at most once per title, facts and day
/// (<see cref="VoiceLinePlan"/>, the suite's one rule), kept in the suite's one memory file with the title as
/// its subject, so a second look at the page is not a second model call. Hidden until composed and hidden
/// when there is no profile or key (<see cref="VoiceFallback.Hide"/>): the composer degrades to an empty line,
/// which is believed for a short while and then asked again. Errors are never voiced and never reach the page.
/// </summary>
internal sealed class TitleVoiceLine
{
    private static readonly Lazy<VoiceGreeting> Composer =
        new(() => new VoiceGreeting(new VoiceGoogleAccount().GetAccessToken, "Pulp"));

    private readonly SeatMemoryFile memory;

    public TitleVoiceLine(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        memory = new SeatMemoryFile(Path.Combine(context.FilesDir!.AbsolutePath, "voice-seats.json"));
    }

    /// <summary>
    /// The line to show for <paramref name="facts"/> today, composing it when the plan says so; null
    /// when the seat stays hidden. Runs the model call, so call it off the UI thread.
    /// </summary>
    public async Task<string?> LineAsync(string titleKey, VoiceFacts facts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var now = DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var line = facts.Occasion.Line;
        var remembered = memory.Get(line, titleKey);
        switch (VoiceLinePlan.Decide(facts, voiceReady: true, remembered, now, today))
        {
            case VoiceLineAction.Reuse:
                return remembered!.Line;
            case VoiceLineAction.Hide:
            case VoiceLineAction.ShowNeutral:
                return null;
            case VoiceLineAction.Compose:
                var composed = await Composer.Value.ComposeAsync(line, facts.Context, cancellationToken);
                var kept = SeatPlan.Remember(facts.Hash, today, composed, now);
                memory.Put(line, titleKey, kept);
                return kept.Line;
            default:
                throw new InvalidOperationException("The why-this seat met a voice line action it does not know.");
        }
    }
}
