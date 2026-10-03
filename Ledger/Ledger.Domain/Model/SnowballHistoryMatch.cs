namespace Index.Domain.Model;

/// <summary>One real account the one-time history import (D03, D78, Guard G8) can match a history
/// loan name against — an account's own name, and the institution it sits under (a history name like a
/// lender's is as likely to be the institution's name as the account's own).</summary>
public sealed record SnowballHistoryCandidate(Guid AccountId, string AccountName, string InstitutionName);

/// <summary>
/// D03/D78: matches one of the sheet's loan names (from the owner's history file in their Drive) to a
/// real connected account, so the one-time history import can <b>log and skip — never guess or
/// crash</b> — any history name that doesn't correspond to an account this install actually has (a
/// fresh, test, or partially-connected database is the ordinary case until the owner's own real loans
/// are connected). Pure and Plaid-free, matching the same separation-of-concerns
/// <c>Index.Domain.Design.PostItTint</c> already draws between "the rule" and "the HTTP call that feeds
/// it".
///
/// <para>Tried in order, exact before fuzzy, account name before institution name: an exact
/// case-insensitive match is preferred over a substring one, since a substring rule alone could match
/// the wrong loan when one name is a prefix of another — a bank-name institution would otherwise happily
/// match two different cards of that bank, and whichever account happened to come first in the list
/// would silently win both.</para>
/// </summary>
public static class SnowballHistoryMatch
{
    public static Guid? Resolve(string historyLoanName, IReadOnlyList<SnowballHistoryCandidate> candidates) =>
        ExactAccountName(historyLoanName, candidates)
        ?? ExactInstitutionName(historyLoanName, candidates)
        ?? SubstringAccountName(historyLoanName, candidates)
        ?? SubstringInstitutionName(historyLoanName, candidates);

    private static Guid? ExactAccountName(string name, IReadOnlyList<SnowballHistoryCandidate> candidates) =>
        candidates.FirstOrDefault(c => string.Equals(c.AccountName, name, StringComparison.OrdinalIgnoreCase))
            ?.AccountId;

    private static Guid? ExactInstitutionName(string name, IReadOnlyList<SnowballHistoryCandidate> candidates) =>
        candidates.FirstOrDefault(c => string.Equals(c.InstitutionName, name, StringComparison.OrdinalIgnoreCase))
            ?.AccountId;

    private static Guid? SubstringAccountName(string name, IReadOnlyList<SnowballHistoryCandidate> candidates) =>
        candidates.FirstOrDefault(c =>
                c.AccountName.Contains(name, StringComparison.OrdinalIgnoreCase)
                || name.Contains(c.AccountName, StringComparison.OrdinalIgnoreCase))
            ?.AccountId;

    private static Guid? SubstringInstitutionName(string name, IReadOnlyList<SnowballHistoryCandidate> candidates) =>
        candidates.FirstOrDefault(c =>
                c.InstitutionName.Contains(name, StringComparison.OrdinalIgnoreCase)
                || name.Contains(c.InstitutionName, StringComparison.OrdinalIgnoreCase))
            ?.AccountId;
}
