using Watch.Domain.Touch;

namespace Watch.Domain.Dash;

/// <summary>What the wrist does with a text that arrived: the plan's decision, and how the buzz is to be played.</summary>
public sealed record TextBuzz(HapticDecision Decision, BuzzUsage Usage);

/// <summary>
/// The wrist's two short taps for a text (P14 B8; WD §7, A12, A35): each arrival Post tells it of buzzes once, by
/// <see cref="HapticPlan.ForText"/>. The same arrival told again stays still, and so does one that crossed too late to
/// mean anything now. Overnight a favourite's text is played as someone asking for the owner, the kind of buzz the watch's
/// own Do Not Disturb lets through when it lets starred and repeat callers ring; everyone else's is still. Kept in memory
/// only, a few dozen at most.
/// </summary>
public sealed class TextArrivals
{
    /// <summary>An arrival older than this by the wrist's clock is old news: it doesn't buzz.</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromMinutes(2);

    /// <summary>How many arrivals the wrist remembers, to buzz each once.</summary>
    public const int Remembered = 32;

    private readonly object gate = new();
    private readonly LinkedList<(string Key, DateTimeOffset At)> heard = new();

    public static TextArrivals Shared { get; } = new();

    public TextBuzz Heard(DashArrived arrived, DateTimeOffset now, bool overnight, SessionState state = SessionState.None)
    {
        var quiet = new TextBuzz(HapticDecision.Silent, BuzzUsage.Notification);
        lock (gate)
        {
            if (heard.Any(entry => entry.Key == arrived.Key && entry.At == arrived.At))
            {
                return quiet;
            }
            heard.AddLast((arrived.Key, arrived.At));
            while (heard.Count > Remembered)
            {
                heard.RemoveFirst();
            }
        }

        if (now - arrived.At > Fresh)
        {
            return quiet;
        }
        var decision = HapticPlan.ForText(state, overnight, arrived.Favourite);
        var usage = overnight && arrived.Favourite ? BuzzUsage.CommunicationRequest : BuzzUsage.Notification;
        return new TextBuzz(decision, usage);
    }
}
