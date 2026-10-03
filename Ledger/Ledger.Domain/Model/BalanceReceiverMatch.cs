namespace Index.Domain.Model;

/// <summary>Which side of D87/D91's precedence a candidate account is on.</summary>
public enum BalanceMatchSource
{
    /// <summary>D87/D15: a hand-entered account, matched by its servicer's name — the primary target
    /// of a BalanceReceiver handoff, since a manual account has no other automatic way to update.</summary>
    Manual,

    /// <summary>D91: a Plaid-connected institution whose own refresh has broken (gone <see
    /// cref="WorkStatus.Failed"/>) — offered only as a staleness fallback, never over a Plaid
    /// connection that is still refreshing on its own.</summary>
    Plaid,
}

/// <summary>One account a receipt's merchant name COULD refer to, reduced to just what the match
/// needs: the name to compare against and which side of the precedence it is on.</summary>
public sealed record BalanceMatchCandidate(Guid AccountId, string MatchName, BalanceMatchSource Source);

/// <summary>
/// D87/D91: which single account a Post/<c>BalanceReceiver</c> balance handoff updates, when a
/// receipt's merchant name could plausibly refer to more than one account. Exactly one match wins per
/// receipt — never both, never neither when at least one candidate qualifies.
///
/// <para><b>Manual before Plaid, always.</b> D87 makes a hand-entered account's servicer the receiver's
/// primary target (it has no other automatic update path); D91 only ever offers a Plaid candidate as a
/// staleness fallback when that Plaid connection has already broken. So if a merchant name happens to
/// match both a manual account and a broken Plaid institution of the same name, the manual account is
/// the one that keeps updating from every future receipt (a Plaid reconnect would otherwise start
/// silently competing with it for the same balance) — the manual side is "fresher" in the sense D91's
/// own wording uses: it has no other way to become current, where a reconnected Plaid Item's own
/// refresh will simply overwrite whatever a receipt wrote once it succeeds again.</para>
///
/// <para>Pure and case-insensitive — Post's own merchant-name text (from its <c>MoneyMail</c> reader)
/// is whatever the mail happened to say, so the match cannot assume canonical capitalization.</para>
/// </summary>
public static class BalanceReceiverMatch
{
    public static Guid? Resolve(string merchantName, IReadOnlyList<BalanceMatchCandidate> candidates)
    {
        var manual = candidates.FirstOrDefault(c =>
            c.Source == BalanceMatchSource.Manual &&
            string.Equals(c.MatchName, merchantName, StringComparison.OrdinalIgnoreCase));
        if (manual is not null)
        {
            return manual.AccountId;
        }

        var plaid = candidates.FirstOrDefault(c =>
            c.Source == BalanceMatchSource.Plaid &&
            string.Equals(c.MatchName, merchantName, StringComparison.OrdinalIgnoreCase));
        return plaid?.AccountId;
    }
}
