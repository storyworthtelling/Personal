namespace Storyworthtelling.Folds;

/// <summary>
/// Notices a page raised as it was leaving, held for the next page that has a corner to fold them into.
/// A page that finishes by navigating away (Index's Plaid link pops back to Accounts) would otherwise have
/// nowhere to say a backup did not land except a platform alert, which DESIGN.md §8 rules out.
///
/// <para>Held in memory for the process, by key, so carrying the same thing twice keeps only the latest;
/// each notice is delivered once. <see cref="Shared"/> is the app's one board.</para>
/// </summary>
public sealed class CarriedNotices
{
    private readonly object gate = new();
    private readonly Dictionary<string, DogEarNotice> held = new(StringComparer.Ordinal);

    public static CarriedNotices Shared { get; } = new();

    public void Carry(string key, DogEarNotice notice)
    {
        lock (gate)
        {
            held[key] = notice;
        }
    }

    /// <summary>Hands every held notice to <paramref name="page"/> and forgets them.</summary>
    public void DeliverTo(DogEarState page)
    {
        KeyValuePair<string, DogEarNotice>[] taken;
        lock (gate)
        {
            taken = [.. held];
            held.Clear();
        }

        foreach (var (key, notice) in taken)
        {
            page.SetNotice(key, notice);
        }
    }
}
