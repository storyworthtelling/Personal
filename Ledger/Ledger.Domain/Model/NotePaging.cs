namespace Index.Domain.Model;

/// <summary>
/// How many rows a post-it shows after the owner has tapped its trailing "n more" / "Earlier months"
/// line N times (mockup 7/8). Both labels used to be printed and dead; tapping now lengthens the note in
/// place — no navigation, no dialog — so what the page needs to know is arithmetic, and it lives here
/// where it can be tested rather than inside the page's layout code.
///
/// <para>The first page and the step differ per note: a transaction post-it opens on four rows and a tap
/// reveals ten more (a month rarely has more than a few dozen); the tracking-only history opens on six
/// months and a tap goes a year further back.</para>
/// </summary>
public static class NotePaging
{
    public const int TransactionsInitial = 4;
    public const int TransactionsStep = 10;
    public const int HistoryInitial = 6;

    /// <summary>Mockup 3: an ACCOUNT's post-it shows five transactions ("Five show, and 17 more lengthens the
    /// note in place"); the Budget post-it above opens on four. A tap shows <see cref="TransactionsStep"/> more.</summary>
    public const int AccountTransactionsInitial = 5;

    /// <summary>The last 30 days (D19) — the window the account post-it asks Plaid for.</summary>
    public const int TransactionWindowDays = 30;

    /// <summary>How many transactions the account post-it asks Plaid for in that window: 500, the most
    /// /transactions/get returns in one call (it pages by offset beyond that). The fetch used to ask for 30,
    /// which capped "n more" at 25 whatever the account had done. Thirty days of one account is far inside 500.</summary>
    public const int TransactionFetchCap = 500;
    public const int HistoryStep = 12;

    /// <summary>Rows showing: the first page, plus one step per tap, never more than the total.</summary>
    public static int Shown(int total, int initial, int step, int taps) =>
        Math.Clamp(initial + Math.Max(taps, 0) * step, 0, Math.Max(total, 0));

    /// <summary>Rows still hidden — zero means there is nothing left to tap and the label goes away.</summary>
    public static int Remaining(int total, int initial, int step, int taps) =>
        Math.Max(total, 0) - Shown(total, initial, step, taps);

    /// <summary>The transaction post-it's trailing line, in mockup 7's words.</summary>
    public static string MoreLabel(int remaining) => $"{remaining} more";
}
