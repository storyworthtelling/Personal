using System.Globalization;
using Index.Domain.Time;

namespace Index.Domain.Model;

/// <summary>What an institution's heading says on its right, and whether it is said in the problem colour.</summary>
public sealed record InstitutionHeadingNote(string Text, bool IsProblem);

/// <summary>
/// The small words on the Accounts page (mockups 2, 3 and 15, remediation 3.3), decided here rather than
/// inside the page: a card or loan with nothing due yet, a hand-entered bank's heading, a broken
/// connection's absolute "As of", and the account-type label under each name.
/// </summary>
public static class AccountCardText
{
    /// <summary>Mockup 15: what a card or loan says in place of its due line until it has a due date.</summary>
    public const string NoPaymentYet = "No payment yet";

    /// <summary>D08 / mockups 2 and 15: a hand-entered bank says so on its heading.</summary>
    public const string ByHand = "By hand";

    /// <summary>
    /// "of $5,000.00" — a card's limit on its Accounts card (D125), drawn the same for a hand-entered card as
    /// for a Plaid one. <paramref name="formattedLimit"/> is the already-formatted amount; with none there is
    /// no line.
    /// </summary>
    public static string LimitLine(string? formattedLimit) =>
        string.IsNullOrEmpty(formattedLimit) ? string.Empty : $"of {formattedLimit}";

    /// <summary>
    /// "Due Oct 3 · $85.00", or "No payment yet" when there is no due date. The due date is a date-only
    /// value read through <see cref="CalendarDate"/>. <paramref name="paymentText"/> is the already-formatted
    /// amount (money formatting belongs to the page); with no due date it is not shown — a payment with no
    /// day to pay it on is "no payment yet", the same sentence mockup 15 gives a loan awaiting its figures.
    /// </summary>
    public static string DueLine(DateTimeOffset? due, string? paymentText)
    {
        if (due is not { } dueDate)
        {
            return NoPaymentYet;
        }

        var line = $"Due {CalendarDate.Short(dueDate)}";
        return string.IsNullOrEmpty(paymentText) ? line : $"{line} · {paymentText}";
    }

    /// <summary>
    /// What an institution's heading says: "As of Sep 23" (D32, in the problem colour) for a broken
    /// connection that still shows its last figures, otherwise "By hand" for a hand-entered bank, otherwise
    /// nothing. The "As of" is an absolute date — mockup 2 — and is an INSTANT (the last good refresh), so it
    /// is shown in the phone's local day; a date from another year carries its year.
    /// </summary>
    public static InstitutionHeadingNote? HeadingNote(
        bool failed, DateTimeOffset? lastRefreshed, bool handEntered, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        if (failed && lastRefreshed is { } at)
        {
            var day = CalendarDate.PhoneDay(at, zone);
            var format = day.Year == CalendarDate.PhoneDay(now, zone).Year ? "MMM d" : "MMM d, yyyy";
            return new InstitutionHeadingNote("As of " + day.ToString(format, CultureInfo.InvariantCulture), IsProblem: true);
        }

        return handEntered ? new InstitutionHeadingNote(ByHand, IsProblem: false) : null;
    }

    /// <summary>
    /// The account-type word under an account's name: a deposit account is named by Plaid's own subtype
    /// ("Checking", "Savings", "CD"); an unknown or missing subtype is just "Account". Cards and loans keep
    /// their own words whatever their subtype.
    /// </summary>
    public static string KindLabel(AccountKind kind, string? subtype) => kind switch
    {
        AccountKind.Depository => DepositoryLabel(subtype),
        AccountKind.Credit => "Credit card",
        AccountKind.Loan => "Loan",
        AccountKind.Investment => "Investment",
        _ => "Account",
    };

    /// <summary>"Checking ····7301" — the label, then the mask Plaid exposes; the label alone with no mask.</summary>
    public static string MaskedLabel(AccountKind kind, string? subtype, string? mask)
    {
        var label = KindLabel(kind, subtype);
        return string.IsNullOrEmpty(mask) ? label : $"{label} ····{mask}";
    }

    // Plaid's depository subtypes (https://plaid.com/docs/api/accounts/#account-type-schema), lower-case
    // strings. Anything Plaid adds later is "Account" until it is named here.
    private static string DepositoryLabel(string? subtype) => subtype?.Trim().ToLowerInvariant() switch
    {
        "checking" => "Checking",
        "savings" => "Savings",
        "cd" => "CD",
        "money market" => "Money market",
        "cash management" => "Cash management",
        "hsa" => "HSA",
        "paypal" => "PayPal",
        "prepaid" => "Prepaid",
        "ebt" => "EBT",
        _ => "Account",
    };
}
