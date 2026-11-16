using Folio.Domain.Analysis;
using Folio.Domain.Imaging;
using Folio.Domain.Markup;
using Folio.Domain.Model;

namespace Folio.Domain.Report;

/// <summary>How a revision-plan item stands against the last draft (R19).</summary>
public enum PlanSince
{
    New,
    StillOpen,
    Regressed,
}

/// <summary>
/// Marks each revision-plan item on a later draft as new, still open or regressed (R19).
///
/// <para>The tracked threads decide first, because they are Folio's own record across drafts rather than a
/// model's impression: an item resting on a regressed finding is regressed, on a carried one still open, and
/// on new ones alone new. An item resting on no tracked finding — the threads cover only the four consolidated
/// lists, and many items draw on the reads — takes the plan pass's word, which was given the last plan to
/// compare against. A word outside the three leaves the item unmarked rather than guessed.</para>
/// </summary>
public static class PlanProgress
{
    public static PlanSince? Since(
        RevisionItem item, IReadOnlyList<FindingEntry> catalogue, IReadOnlyList<ReportFindingView> threads,
        bool laterDraft)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!laterDraft)
        {
            return null;
        }

        var drawn = (item.Draws ?? [])
            .Select(key => (catalogue ?? []).FirstOrDefault(entry => entry.Key == key))
            .OfType<FindingEntry>()
            .Where(entry => entry.Category is not null)
            .SelectMany(entry => (threads ?? []).Where(thread =>
                thread.Category == entry.Category
                && string.Equals(thread.Detail?.Trim(), entry.Said.Trim(), StringComparison.Ordinal)
                && string.Equals(thread.Chapter?.Trim() ?? string.Empty, entry.Chapter.Trim(), StringComparison.Ordinal)))
            .Select(thread => thread.Status)
            .ToList();

        if (drawn.Contains(FindingStatus.Regressed))
        {
            return PlanSince.Regressed;
        }

        if (drawn.Contains(FindingStatus.Carried))
        {
            return PlanSince.StillOpen;
        }

        if (drawn.Count > 0)
        {
            return PlanSince.New;
        }

        return Word(item.Since);
    }

    /// <summary>The plan pass's own word for an item, or null when it is not one of the three.</summary>
    public static PlanSince? Word(string? since) =>
        (since ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "new" => PlanSince.New,
            "open" or "still open" or "stillopen" => PlanSince.StillOpen,
            "regressed" => PlanSince.Regressed,
            _ => null,
        };

    /// <summary>How the mark reads on the plan page.</summary>
    public static string Words(PlanSince since) => since switch
    {
        PlanSince.New => "New since the last draft",
        PlanSince.StillOpen => "Still open from the last draft",
        _ => "Regressed since the last draft",
    };
}
