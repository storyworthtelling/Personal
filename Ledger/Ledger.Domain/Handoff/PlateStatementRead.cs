using System.Globalization;

namespace Index.Domain.Handoff;

/// <summary>How a statement read ended.</summary>
public enum PlateReadOutcome
{
    Read,
    /// <summary>The owner backed out of Plate. Nothing to say.</summary>
    Cancelled,
    NotInstalled,
    /// <summary>Index does not hold Plate's reading permission, so Plate refused to start.</summary>
    NotPermitted,
    /// <summary>Plate answered, but with no balance Index could read.</summary>
    Unreadable,
}

/// <summary>A statement Plate read: the balance as exact decimal, and the statement's date when it gave one.</summary>
public sealed record PlateStatementAnswer(decimal Balance, DateOnly? AsOf);

/// <summary>
/// Index's end of Plate's statement read (audit W2, 09 M3). From a hand-entered account's post-it the owner asks
/// Plate to read the statement; Plate answers with Post's balance extras, and the balance goes on the account that
/// asked. Index cannot reference Codex.Domain, so these are its own literals, held equal to Plate's
/// <c>ReadRequest</c> and <c>PolicyContext</c> by <c>PlateStatementReadTests</c>, which reads those files by content.
/// </summary>
public static class PlateStatementRead
{
    /// <summary>Plate's package, frozen so devices upgrade in place.</summary>
    public const string PlatePackage = "com.storyworthtelling.focusphone.camera";

    public const string Action = "com.storyworthtelling.plate.action.READ_STATEMENT";

    /// <summary>Plate's guarded reading screen, named outright so nothing else can answer.</summary>
    public const string Activity = PlatePackage + ".PaperReadActivity";

    /// <summary>The signature permission Plate's reading screen requires of its caller.</summary>
    public const string Permission = PlatePackage + ".READ_PAPER";

    public const string BalanceExtra = "balance";
    public const string AsOfExtra = "asOf";

    /// <summary>
    /// The answer, or null when it carries no readable balance. The balance crosses as text so it is read as
    /// decimal, never double; an unreadable date is left out rather than guessed.
    /// </summary>
    public static PlateStatementAnswer? Parse(string? balanceText, string? asOfText)
    {
        if (string.IsNullOrWhiteSpace(balanceText)
            || !decimal.TryParse(balanceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var balance))
        {
            return null;
        }

        DateOnly? asOf = DateOnly.TryParseExact(
            asOfText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;
        return new PlateStatementAnswer(balance, asOf);
    }

    /// <summary>The line the post-it shows when a read did not change the balance, or null when there is nothing to say.</summary>
    public static string? Describe(PlateReadOutcome outcome) => outcome switch
    {
        PlateReadOutcome.NotInstalled => "Plate is not on this phone, so it cannot read the statement.",
        PlateReadOutcome.NotPermitted => "Plate would not read for Index. Updating both apps puts that right.",
        PlateReadOutcome.Unreadable => "Plate found no balance on the statement, so nothing changed.",
        _ => null,
    };
}
