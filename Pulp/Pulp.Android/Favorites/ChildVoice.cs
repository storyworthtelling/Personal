using Android.App;
using Pulp.App;
using Pulp.Domain.Voice;
using Pulp.Watching;

namespace Pulp.Favorites;

/// <summary>
/// A child's voiced seats (D39, D232, D233, D294): the greeting, the Favorites line, and the three
/// empty pages, each composed through the home's own <see cref="WatchingVoice"/> from facts that
/// <see cref="ChildVoiceFacts"/> built at the child's reading level. <see cref="VoiceLinePlan"/>
/// decides whether to compose, reuse or stay quiet; a seat that hides is never shown before its line
/// arrives, and a voice is never an error on the page (DESIGN.md §14).
/// </summary>
public sealed class ChildVoice(Activity host, PulpServices services)
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
            var remembered = voice.Value.Remembered(facts.Seat);
            var action = VoiceLinePlan.Decide(facts, WatchingVoice.Ready, remembered, today);
            host.RunOnUiThread(() => present(action, action == VoiceLineAction.Reuse ? remembered?.Line : null));
            if (action != VoiceLineAction.Compose)
            {
                return;
            }

            var line = await voice.Value.ComposeAsync(facts, today, cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                host.RunOnUiThread(() => present(string.IsNullOrWhiteSpace(line) ? Quiet(facts.Seat) : VoiceLineAction.Reuse, line));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The page moved on before the line came back; the next paint asks again.
        }
        catch (Exception exception)
        {
            services.Diagnostics.Warn("child", $"The child's {facts.Seat} line could not be decided.", exception);
        }
    }

    /// <summary>The line a seat that hides shows: only a composed one, else nothing.</summary>
    public static string? Shown(VoiceLineAction action, string? line) =>
        action == VoiceLineAction.Reuse && !string.IsNullOrWhiteSpace(line) ? line.Trim() : null;

    private static VoiceLineAction Quiet(VoiceSeat seat) =>
        VoicedOccasions.For(seat).Fallback == VoiceFallback.Hide ? VoiceLineAction.Hide : VoiceLineAction.ShowNeutral;
}
