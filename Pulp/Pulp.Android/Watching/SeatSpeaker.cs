using Android.App;
using Pulp.App;
using Pulp.Components;
using Pulp.Domain.Voice;

namespace Pulp.Watching;

/// <summary>
/// Any of Pulp's voiced seats, spoken on a page: a child's (the greeting, the Favorites line and the three empty
/// pages, from facts <see cref="ChildVoiceFacts"/> built at the child's reading level) and, since audit W10.4, the
/// adult empty pages and the playlist line, each composed through the home's own <see cref="WatchingVoice"/>. <see cref="VoiceLinePlan"/>
/// decides whether to compose, reuse or stay quiet; a seat that hides is never shown before its line
/// arrives, and a voice is never an error on the page (DESIGN.md §14).
/// </summary>
public sealed class SeatSpeaker(Activity host, PulpServices services)
{
    private readonly Lazy<WatchingVoice> voice = new(() => new WatchingVoice(host));

    /// <summary>
    /// Decides and, when due, composes <paramref name="facts"/>' seat. <paramref name="present"/> is
    /// called on the UI thread with the plan's action and the line to show (null unless reused).
    /// </summary>
    public async Task SpeakAsync(VoiceFacts facts, Action<VoiceLineAction, string?> present, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(present);
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var remembered = voice.Value.Remembered(facts);
            var action = VoiceLinePlan.Decide(facts, WatchingVoice.Ready, remembered, DateTimeOffset.Now, today);
            host.RunOnUiThread(() => present(action, action == VoiceLineAction.Reuse ? remembered?.Line : null));
            if (action != VoiceLineAction.Compose)
            {
                return;
            }

            var line = await voice.Value.ComposeAsync(facts, today, cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                host.RunOnUiThread(() => present(string.IsNullOrWhiteSpace(line) ? VoiceLinePlan.Quiet(facts.Seat) : VoiceLineAction.Reuse, line));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The page moved on before the line came back; the next paint asks again.
        }
        catch (Exception exception)
        {
            services.Diagnostics.Warn("voice", $"The {facts.Seat} line could not be decided.", exception);
        }
    }

    /// <summary>
    /// An empty page that speaks (audit W10.4): its neutral line at once where the plan says so, the owner's line once
    /// it is composed. The page needs nothing else from the caller.
    /// </summary>
    public EmptyState Empty(VoiceFacts facts, Action? primary = null)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var empty = new EmptyState(host, facts.Seat, primary);
        _ = SpeakAsync(facts, empty.Present, CancellationToken.None);
        return empty;
    }

    /// <summary>The line a seat that hides shows: only a composed one, else nothing.</summary>
    public static string? Shown(VoiceLineAction action, string? line) =>
        action == VoiceLineAction.Reuse && !string.IsNullOrWhiteSpace(line) ? line.Trim() : null;

}
