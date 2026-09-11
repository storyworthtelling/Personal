namespace Storyworthtelling.Maui.State;

/// <summary>
/// Which option a segmented control has picked, and the rule for what that means.
///
/// <para>Separate from the view because the rule is easy to get subtly wrong and impossible to see
/// going wrong: a segmented control with <b>nothing</b> picked looks exactly like one whose picked
/// option is off screen, and a control with <b>two</b> picked looks like a rendering bug rather than
/// a state bug. Both are decisions, so both are decided here and tested.</para>
///
/// <para>DESIGN.md §8 calls for a segmented control rather than a switch wherever the choice has
/// names — "Print" and "Pause" say what will happen; a switch says only on or off, and the owner has
/// to remember which way round it was written.</para>
/// </summary>
public sealed class SegmentedChoice
{
    private readonly List<string> options;

    public SegmentedChoice(IEnumerable<string> options, string? picked = null)
    {
        this.options = [.. options];

        if (this.options.Count == 0)
        {
            throw new ArgumentException("A segmented control with no options is not a control.", nameof(options));
        }

        // Always exactly one. An unrecognised value falls back to the first rather than leaving
        // nothing picked — a control showing no answer reads as one nobody has set, and this is a
        // setting that always has a value.
        Picked = picked is not null && this.options.Contains(picked, StringComparer.Ordinal)
            ? picked
            : this.options[0];
    }

    public IReadOnlyList<string> Options => options;

    public string Picked { get; private set; }

    public int PickedIndex => options.IndexOf(Picked);

    public bool IsPicked(string option) => string.Equals(option, Picked, StringComparison.Ordinal);

    /// <summary>
    /// Picks an option. Returns whether anything changed, so a caller can commit only on a real
    /// change — this control has no Save, and writing on every tap would store the same value
    /// repeatedly for someone tapping the option already picked.
    /// </summary>
    public bool Pick(string option)
    {
        if (!options.Contains(option, StringComparer.Ordinal) || IsPicked(option))
        {
            return false;
        }

        Picked = option;
        return true;
    }
}
