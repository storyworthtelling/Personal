namespace Index.Domain.Worksheet;

/// <summary>D98/D99: the two kinds of set-aside line the Release H worksheet prints — the Snowball's
/// current focus loan's extra payment, and one line per savings goal.</summary>
public enum SetAsideLineKind
{
    ExtraDebtPayment,
    Savings,
}

/// <summary>One set-aside row (mockup 13(a)'s "Set aside" section): a box like a category's, with a
/// bold title and a small detail line underneath ("to Redwood Bank &#8211; Bluebird" / the savings account's own
/// name) and last month's figure printed beside it.</summary>
public sealed record WorksheetSetAsideLine(
    SetAsideLineKind Kind, string Key, string Title, string Detail, decimal LastMonthActual);
