namespace Index.Domain.Model;

/// <summary>
/// How many times "n more" has been tapped on the account post-it that is open (mockup 3). One post-it is open
/// at a time (D128), so there is one count, and it belongs to the card that is open: opening another card —
/// or closing this one — starts it again, while a re-render of the same open card keeps it (a render is not an
/// opening). Pure, so the "resets when another card opens" rule is tested rather than remembered.
/// </summary>
public sealed class OpenNoteTaps
{
    private Guid? open;
    private int taps;

    /// <summary>The card the post-it is now on (null when none is open). Changing it resets the count.</summary>
    public void Opened(Guid? accountId)
    {
        if (open == accountId)
        {
            return;
        }

        open = accountId;
        taps = 0;
    }

    public void Tapped(Guid accountId)
    {
        Opened(accountId);
        taps++;
    }

    /// <summary>Taps for this card; zero for any card but the open one.</summary>
    public int For(Guid accountId) => open == accountId ? taps : 0;
}
