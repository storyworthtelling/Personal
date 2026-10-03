namespace Index.Domain.Model;

/// <summary>
/// D124: a card or loan with nothing left owed disappears from Accounts entirely — not a crossed-out
/// row (that treatment moved to Snowball only, D12), just gone, and a bank with nothing open left
/// goes with it. A depository or investment account never counts: a $0 checking account is still an
/// account you hold, not something you finished paying off.
/// </summary>
public static class AccountStatus
{
    public static bool IsPaidOff(Account account) =>
        account.Kind is AccountKind.Credit or AccountKind.Loan
        && account.CurrentBalance is 0m;

    /// <summary>
    /// Mockup 6 names the mortgage's panel "(mortgage)". The app knows it from Plaid's own account
    /// subtype (<c>loan</c>/<c>mortgage</c>, stored on <see cref="Account.Subtype"/>), never from the
    /// loan's name. A hand-entered loan has no subtype to read, so it is never called a mortgage.
    /// </summary>
    public static bool IsMortgage(Account account) =>
        account.Kind is AccountKind.Loan
        && string.Equals(account.Subtype, "mortgage", StringComparison.OrdinalIgnoreCase);
}
