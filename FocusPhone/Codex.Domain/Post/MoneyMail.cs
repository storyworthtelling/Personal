using System.Globalization;
using System.Text.RegularExpressions;

namespace Codex.Domain.Post;

/// <summary>
/// What a piece of mail says about money. A balance and an amount due are different
/// things and are kept apart deliberately — handing Index an amount due as though it were
/// a balance would put a debt on the wrong side of a ledger.
/// </summary>
public sealed record MoneyFacts(decimal? AmountDue, DateOnly? DueOn, decimal? Balance, DateOnly? AsOf);

/// <summary>
/// Reads money out of a bill or statement. Roadmap #4, narrowed: <b>Post parses, Index
/// consumes</b> — a balance is Index's business and crosses to it, while a bill is Post's
/// and stays, showing its amount and due date on the row.
///
/// The parsing is general rather than written against two particular lenders, because the
/// owner's mail is not, and a parser that only knows two senders quietly stops working the
/// first time a third one matters.
///
/// It errs toward finding nothing. A fabricated balance would reach Index and be believed,
/// which is far worse than a balance the owner has to read themselves — so a number is
/// only money when it is written as money, and only a due date when something nearby says
/// it is due.
/// </summary>
public static partial class MoneyMail
{
    // "$1,234.56" or "$75" — a currency mark is what makes a number money. A bare number
    // is an order id, a time, or a year.
    [GeneratedRegex(@"\$\s?([0-9][0-9,]*(?:\.[0-9]{2})?)", RegexOptions.IgnoreCase)]
    private static partial Regex Money();

    [GeneratedRegex(@"(balance|amount owed|you owe)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BalanceWord();

    [GeneratedRegex(@"(amount due|payment due|minimum payment|due)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DueWord();

    // Enough date shapes to cover how a statement actually writes one, and no more.
    [GeneratedRegex(
        @"\b(\d{4}-\d{2}-\d{2}|\d{1,2}/\d{1,2}/\d{4}|\d{1,2}\s+[A-Za-z]{3,9}\s+\d{4}|[A-Za-z]{3,9}\s+\d{1,2},?\s+\d{4})\b")]
    private static partial Regex DateLike();

    // How far either side of a money figure the qualifying word may sit. A statement writes
    // "your current balance is $2,500.00" or "$75.00 minimum payment"; anything further
    // away is a different sentence talking about something else.
    private const int Window = 40;

    public static MoneyFacts Read(string? subject, string? body)
    {
        var text = $"{subject} {body}".Trim();
        if (text.Length == 0)
        {
            return new MoneyFacts(null, null, null, null);
        }

        decimal? amountDue = null;
        decimal? balance = null;

        foreach (Match match in Money().Matches(text))
        {
            if (!decimal.TryParse(match.Groups[1].Value, NumberStyles.Currency,
                    CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            // What qualifies a figure is the NEAREST money word, not any money word within
            // reach. A statement says "your current balance is $2,500.00. Minimum payment
            // due $75.00" — both words sit near both figures, so a rule that takes either
            // one within a window reads the balance as the payment and then has nothing
            // left to call the balance.
            switch (Qualifier(text, match.Index, match.Length))
            {
                case MoneyWord.Due:
                    amountDue ??= value;
                    break;
                case MoneyWord.Balance:
                    balance ??= value;
                    break;
            }
        }

        return new MoneyFacts(amountDue, DueDate(text), balance, AsOfDate(text));
    }

    private enum MoneyWord { None, Due, Balance }

    // The money word closest to a figure. Words BEFORE it win — "balance is $X" is how
    // money gets labelled in English — and a following word is consulted only when nothing
    // precedes, for the "$75.00 minimum payment" shape.
    private static MoneyWord Qualifier(string text, int index, int length)
    {
        var beforeStart = Math.Max(0, index - Window);
        var before = text[beforeStart..index];
        var afterEnd = Math.Min(text.Length, index + length + Window);
        var after = text[(index + length)..afterEnd];

        var dueBefore = LastIndex(DueWord(), before);
        var balanceBefore = LastIndex(BalanceWord(), before);
        if (dueBefore >= 0 || balanceBefore >= 0)
        {
            return dueBefore > balanceBefore ? MoneyWord.Due : MoneyWord.Balance;
        }

        var dueAfter = FirstIndex(DueWord(), after);
        var balanceAfter = FirstIndex(BalanceWord(), after);
        if (dueAfter < 0 && balanceAfter < 0)
        {
            return MoneyWord.None;
        }
        if (dueAfter < 0)
        {
            return MoneyWord.Balance;
        }
        if (balanceAfter < 0)
        {
            return MoneyWord.Due;
        }
        return dueAfter < balanceAfter ? MoneyWord.Due : MoneyWord.Balance;
    }

    private static int LastIndex(Regex pattern, string text)
    {
        var last = -1;
        foreach (Match match in pattern.Matches(text))
        {
            last = match.Index;
        }
        return last;
    }

    private static int FirstIndex(Regex pattern, string text)
    {
        var match = pattern.Match(text);
        return match.Success ? match.Index : -1;
    }

    private static string Around(string text, int index, int length)
    {
        var start = Math.Max(0, index - Window);
        var end = Math.Min(text.Length, index + length + Window);
        return text[start..end];
    }

    // A date only counts as a due date when something nearby says it is due.
    private static DateOnly? DueDate(string text)
    {
        foreach (Match match in DateLike().Matches(text))
        {
            if (DueWord().IsMatch(Around(text, match.Index, match.Length)) && TryDate(match.Value) is { } date)
            {
                return date;
            }
        }
        return null;
    }

    // The date a balance was true, when the mail bothers to say. Index can stamp arrival
    // instead, so this is a nicety rather than something to invent.
    private static DateOnly? AsOfDate(string text)
    {
        foreach (Match match in DateLike().Matches(text))
        {
            var context = Around(text, match.Index, match.Length);
            if (!DueWord().IsMatch(context)
                && (BalanceWord().IsMatch(context) || context.Contains("as of", StringComparison.OrdinalIgnoreCase))
                && TryDate(match.Value) is { } date)
            {
                return date;
            }
        }
        return null;
    }

    private static DateOnly? TryDate(string value)
    {
        string[] formats =
        [
            "yyyy-MM-dd", "MM/dd/yyyy", "M/d/yyyy",
            "d MMMM yyyy", "d MMM yyyy", "MMMM d yyyy", "MMM d yyyy", "MMMM d, yyyy", "MMM d, yyyy",
        ];

        return DateOnly.TryParseExact(value, formats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed) ? parsed : null;
    }
}

/// <summary>What crosses to Index. Balance and date only, and nothing else.</summary>
public sealed record IndexBalance(decimal Balance, DateOnly? AsOf);

/// <summary>
/// The cross-app handoff, decided. A signature-permission-guarded explicit intent carries
/// a balance to Index, and the payload is deliberately minimal: Index needs to know what
/// is owed and when it was true, and nothing about who the owner banks with belongs in an
/// intent that any signed app could receive.
///
/// There is no outbox. If Index is not installed or the send fails, Post drops it — Index
/// will see the same email on a later sync, so the case self-heals, and a durable retry
/// queue is real machinery to maintain for something that fixes itself.
/// </summary>
public static class IndexHandoff
{
    /// <summary>Index's package. The intent names it explicitly rather than broadcasting.</summary>
    public const string Package = "com.storyworthtelling.index";

    /// <summary>The receiver, named directly so nothing else can intercept the balance.</summary>
    public const string Receiver = "com.storyworthtelling.index.BalanceReceiver";

    public const string Action = "com.storyworthtelling.index.action.BALANCE";

    /// <summary>
    /// The permission Index declares at protectionLevel="signature". It costs nothing —
    /// every head already shares one keystore for the update channel — and without it any
    /// app on the phone could feed Index fabricated balances.
    /// </summary>
    public const string Permission = "com.storyworthtelling.index.RECEIVE_BALANCE";

    public const string BalanceExtra = "balance";
    public const string AsOfExtra = "asOf";

    public static IndexBalance? PayloadFor(MoneyFacts facts) =>
        facts.Balance is { } balance ? new IndexBalance(balance, facts.AsOf) : null;

    /// <summary>
    /// The payload as intent extras. The balance crosses as exact TEXT rather than as a
    /// double: 0.1 + 0.2 is not 0.3 in binary floating point, and a ledger that disagrees
    /// with a bank by a cent is a ledger nobody trusts.
    ///
    /// A dateless balance carries no date key at all, rather than an empty one — Index
    /// stamps arrival in that case, and an empty string would have to be told apart from a
    /// date that failed to parse.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Extras(IndexBalance payload)
    {
        var extras = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [BalanceExtra] = payload.Balance.ToString("0.00", CultureInfo.InvariantCulture),
        };
        if (payload.AsOf is { } asOf)
        {
            extras[AsOfExtra] = asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        return extras;
    }
}
