using Android.Content;
using Android.Widget;
using Codex.Ui;
using Pulp.Domain.Actions;

namespace Pulp.Components;

/// <summary>
/// What one card entry does. <see cref="Undo"/> is required for an undo-first entry: the act happens
/// at once and the dog-ear folds with this as its back-turn (§13) — never a confirmation first.
/// </summary>
public sealed record CardHandler(Action Do, Action? Undo = null);

/// <summary>
/// Pulp's action card (mockup 10, D256): <see cref="ActionCardPlan"/> decides the entries by where the
/// title was pressed, and the shared kit's <see cref="ActionCard"/> draws them — a centred card over a
/// PaperDeep haze, tap outside to dismiss. Every entry is plain ink, as drawn: nothing is Lead and
/// nothing is Destructive red, because a removal here is undone from the corner rather than warned
/// about.
/// </summary>
public static class PulpActionCard
{
    /// <param name="overlay">The page's <c>Overlay</c> frame, which carries both the card and the undo fold.</param>
    /// <param name="handlers">One handler per entry the plan offers; a missing one fails here, naming it.</param>
    public static void Show(
        Context context,
        FrameLayout overlay,
        CardItem item,
        IReadOnlyDictionary<CardActionKind, CardHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(handlers);

        var plan = ActionCardPlan.For(item);
        var missing = plan.Entries.Where(entry => !handlers.ContainsKey(entry.Kind)).Select(entry => entry.Kind).ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException(
                $"The action card for '{plan.Title}' ({item.Place}) offers {string.Join(", ", missing)} with no handler.",
                nameof(handlers));
        }

        var undoless = plan.Entries.Where(entry => entry.UndoFirst && handlers[entry.Kind].Undo is null).ToList();
        if (undoless.Count > 0)
        {
            throw new ArgumentException(
                $"'{string.Join("', '", undoless.Select(entry => entry.Label))}' on '{plan.Title}' is undo-first (§13) "
                + "and was given no undo.",
                nameof(handlers));
        }

        var actions = plan.Entries
            .Select(entry => new CardAction(entry.Label, () => Invoke(context, overlay, plan.Title, entry, handlers[entry.Kind])))
            .ToArray();

        ActionCard.Show(context, overlay, plan.Title, plan.Subtitle, actions);
    }

    private static void Invoke(Context context, FrameLayout overlay, string title, CardEntry entry, CardHandler handler)
    {
        handler.Do();
        if (entry.UndoFirst)
        {
            DogEar.Show(context, overlay, $"Undo {entry.Label.ToLowerInvariant()}, {title}.", handler.Undo!);
        }
    }
}
