using System.Globalization;

namespace Index.Domain.Model;

/// <summary>
/// Where one bank sits among the banks the Accounts page is showing (D123, mockup 2 frame 2): the action card
/// reads "4 accounts · first on the page", dims Move up on the first bank and Move down on the last, and the
/// page used to pass <c>true</c> for both so a tap on the first bank's Move up did nothing and said nothing.
///
/// <para>The position is among the banks that are SHOWING (a bank whose accounts are all paid off is gone from
/// the page, D124), because "on the page" is what the owner sees. Mockup 2 draws only "first on the page";
/// "last on the page", "2nd of 5 on the page" and "the only bank on the page" are this type's minimal extension,
/// in the same words and the same order.</para>
/// </summary>
public readonly record struct InstitutionPlacement(int Index, int Count)
{
    /// <summary>A bank that is not on the page: nothing can move, and it has no place to describe.</summary>
    private static readonly InstitutionPlacement NotShown = new(-1, 0);

    public static InstitutionPlacement Of(IReadOnlyList<Guid> shownInOrder, Guid institutionId)
    {
        for (var i = 0; i < shownInOrder.Count; i++)
        {
            if (shownInOrder[i] == institutionId)
            {
                return new InstitutionPlacement(i, shownInOrder.Count);
            }
        }

        return NotShown;
    }

    private bool IsShown => Index >= 0 && Index < Count;

    public bool CanMoveUp => IsShown && Index > 0;

    public bool CanMoveDown => IsShown && Index < Count - 1;

    /// <summary>The last bank is already at the bottom, so "Move to the bottom" would do nothing there either.</summary>
    public bool CanMoveToBottom => CanMoveDown;

    public string Phrase
    {
        get
        {
            if (!IsShown)
            {
                return string.Empty;
            }

            if (Count == 1)
            {
                return "the only bank on the page";
            }

            if (Index == 0)
            {
                return "first on the page";
            }

            return Index == Count - 1
                ? "last on the page"
                : $"{Ordinal(Index + 1)} of {Count} on the page";
        }
    }

    /// <summary>"4 accounts · first on the page" (mockup 2, verbatim for the first bank).</summary>
    public string Subtitle(int openAccounts)
    {
        var accounts = $"{openAccounts} account{(openAccounts == 1 ? string.Empty : "s")}";
        return string.IsNullOrEmpty(Phrase) ? accounts : $"{accounts} · {Phrase}";
    }

    private static string Ordinal(int number)
    {
        var lastTwo = number % 100;
        var suffix = lastTwo is >= 11 and <= 13
            ? "th"
            : (number % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return number.ToString(CultureInfo.InvariantCulture) + suffix;
    }
}
