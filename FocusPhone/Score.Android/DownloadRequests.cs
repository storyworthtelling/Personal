using Android.Views;
using Android.Widget;
using Codex.Domain.Audio;
using Codex.Ui;

namespace Score;

/// <summary>
/// Asking for a download, and asking about it when it does not fit.
///
/// Over budget <b>asks every time</b> rather than refusing or silently making room.
/// Refusing would make the allowance a wall the owner cannot get past on the one
/// evening it matters; making room would throw away something they chose. Asking
/// keeps the number meaningful and leaves the decision where it belongs — and the
/// question has to name the cost, or it is not a question.
/// </summary>
public static class DownloadRequests
{
    /// <summary>
    /// Downloads these, asking first if they will not fit. Anything already held is
    /// dropped from the request, so downloading an album you have most of asks about
    /// the part that is actually new.
    /// </summary>
    public static void Ask(
        Activity host,
        FrameLayout overlay,
        DownloadStore store,
        string what,
        IReadOnlyList<(DownloadedItem Item, string Uri)> wanted)
    {
        var budget = store.Load();
        var fresh = wanted.Where(entry => !budget.Holds(entry.Item.MediaId)).ToList();
        if (fresh.Count == 0)
        {
            return; // already downloaded; nothing to say and nothing to do
        }

        var cost = fresh.Aggregate(TimeSpan.Zero, (sum, entry) => sum + entry.Item.Duration);
        if (budget.Fits(cost))
        {
            Start(host, overlay, store, budget, what, fresh);
            return;
        }

        var over = budget.Overage(cost);
        ActionCard.Show(host, overlay, what,
            $"This is {Describe(over)} more than the 12-hour allowance.",
            new CardAction("Download anyway",
                () => Start(host, overlay, store, store.Load(), what, fresh), Lead: true));
    }

    private static void Start(
        Activity host,
        FrameLayout overlay,
        DownloadStore store,
        DownloadBudget budget,
        string what,
        IReadOnlyList<(DownloadedItem Item, string Uri)> wanted)
    {
        foreach (var (item, uri) in wanted)
        {
            Downloads.Add(host, item.MediaId, uri);
            budget = budget.With(item);
        }

        store.Save(budget);

        // Undo-first, like every other act with a cost. Undoing stops the fetch rather
        // than deleting something finished, which is the point of offering it here
        // instead of making the owner go to the downloads screen.
        UndoBar.Show(host, overlay, $"Downloading {what}.", onUndo: () =>
        {
            var current = store.Load();
            foreach (var (item, _) in wanted)
            {
                Downloads.Remove(host, item.MediaId);
                current = current.Without(item.MediaId);
            }

            store.Save(current);
        });
    }

    private static string Describe(TimeSpan span) => span.TotalHours >= 1
        ? $"{(int)span.TotalHours}h {span.Minutes:D2}m"
        : $"{Math.Max(1, (int)Math.Round(span.TotalMinutes))} min";
}
