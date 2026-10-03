namespace Index.Domain.Model;

/// <summary>
/// D56/D159: whether an account counts as Savings, for the Savings tab and the post-it's switch. A
/// connected (Plaid) account defaults to Plaid's own reported <see cref="Account.Subtype"/>; the
/// owner's override on <see cref="Account.IsSavingsOverride"/> always wins once set, in either
/// direction — turning the switch off a Plaid-savings account is as much an override as turning it on
/// for one Plaid doesn't call savings.
///
/// <para>Pure so the post-it's switch and any future Savings-tab filter read the same answer without
/// each re-deriving it (and disagreeing) — kept in Domain, not the Android view, because "what counts
/// as savings" is a fact about the account, not about how a screen happens to draw it.</para>
/// </summary>
public static class AccountSavings
{
    // Plaid's own subtype strings that count as savings before any owner override. Extend this set
    // rather than hardcoding a second check elsewhere if a new subtype needs to qualify.
    private static readonly HashSet<string> SavingsSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "savings",
    };

    /// <summary>Plaid's own answer, ignoring any owner override — what the post-it's subtitle quotes
    /// ("Plaid calls it savings"/"Plaid doesn't call it savings").</summary>
    public static bool PlaidSaysSavings(string? subtype) =>
        subtype is not null && SavingsSubtypes.Contains(subtype);

    /// <summary>The effective answer: the owner's override if one is set, otherwise Plaid's own
    /// subtype. A hand-entered account (no Subtype) with no override reads as not-savings, which is
    /// harmless since Release C's Savings tab does not exist yet for manual accounts either.</summary>
    public static bool IsEffectivelySavings(Account account) =>
        account.IsSavingsOverride ?? PlaidSaysSavings(account.Subtype);
}
