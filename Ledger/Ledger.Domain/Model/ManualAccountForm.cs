namespace Index.Domain.Model;

[Flags]
public enum ManualField
{
    None = 0,
    Name = 1,
    Bank = 2,
    Kind = 4,
    Balance = 8,
    CreditLimit = 16,
    Apr = 32,
    Payment = 64,
    Every = 128,
    NextDue = 256,
}

/// <summary>What a hand-entered account is saved with once its Kind has decided which fields count.</summary>
public sealed record ManualAccountValues(
    decimal? Apr, decimal? Payment, decimal? CreditLimit, PaymentCadence? Cadence, DateTimeOffset? NextDue);

/// <summary>
/// Mockup 4: "Kind decides the card's second line: a card asks for its limit, a bank account needs no payment,
/// and a loan (as here) needs neither a limit nor a mask." The one place that decision lives, so the Add page
/// (which rows to show), the service (what to store) and the post-it (what to offer) cannot disagree.
///
/// <para>A field that is not on show is not saved — <see cref="Resolve"/> drops it — so a figure typed before
/// the Kind was switched cannot ride along unseen. The page keeps the typed text meanwhile (switching Kind
/// only hides a row), so switching back brings it back.</para>
/// </summary>
public static class ManualAccountForm
{
    public const string KindLoan = "Loan";
    public const string KindCard = "Card";
    public const string KindBank = "Bank account";

    /// <summary>Mockup 4's right-aligned caption on "Bank or lender". A label, not a placeholder: it must stay
    /// visible while the owner types.</summary>
    public const string BankHint = "Blank: its own name";

    // The form's hints are EXAMPLE content (obviously invented, never a real account), shown as placeholders and never as defaults. Each
    // money/percent one is in the exact shape a blur writes back (FigureParser.FormatForBox), which a test pins.
    public const string NameExample = "Car loan";
    public const string BankExample = "Example Credit Union";
    public const string BalanceExample = "$12,345.67";
    public const string LimitExample = "$5,000.00";
    public const string AprExample = "6.25%";
    public const string PaymentExample = "$250.00";

    private const ManualField Always = ManualField.Name | ManualField.Bank | ManualField.Kind | ManualField.Balance;

    private const ManualField Payable =
        ManualField.Apr | ManualField.Payment | ManualField.Every | ManualField.NextDue;

    /// <summary>The fields on show for a Kind. Only the three Kinds the form offers are drawn; anything else
    /// (a Plaid-only Kind never reaches this form) is treated as a plain account that needs no payment.</summary>
    public static ManualField FieldsFor(AccountKind kind) => kind switch
    {
        AccountKind.Loan => Always | Payable,
        AccountKind.Credit => Always | ManualField.CreditLimit | Payable,
        _ => Always,
    };

    /// <summary>The Kind a segmented option names; the first option, Loan, is also the fallback.</summary>
    public static AccountKind KindOf(string option) => option switch
    {
        KindCard => AccountKind.Credit,
        KindBank => AccountKind.Depository,
        _ => AccountKind.Loan,
    };

    /// <summary>What is saved: every optional value the Kind keeps, and null for the rest.</summary>
    public static ManualAccountValues Resolve(
        AccountKind kind, decimal? apr, decimal? payment, decimal? creditLimit,
        PaymentCadence? cadence, DateTimeOffset? nextDue)
    {
        var shown = FieldsFor(kind);
        return new ManualAccountValues(
            shown.HasFlag(ManualField.Apr) ? apr : null,
            shown.HasFlag(ManualField.Payment) ? payment : null,
            shown.HasFlag(ManualField.CreditLimit) ? creditLimit : null,
            shown.HasFlag(ManualField.Every) ? cadence : null,
            shown.HasFlag(ManualField.NextDue) ? nextDue : null);
    }

    /// <summary>
    /// The cadence to store. The segmented control always has an option picked (Month, as drawn), so reading
    /// its value as "chosen" stored Month on every account saved without a payment. A cadence belongs with a
    /// payment, or with an Every the owner actually tapped; otherwise nothing was chosen and nothing is stored.
    /// </summary>
    public static PaymentCadence? CadenceToStore(decimal? payment, bool touched, PaymentCadence picked) =>
        payment is not null || touched ? picked : null;
}
