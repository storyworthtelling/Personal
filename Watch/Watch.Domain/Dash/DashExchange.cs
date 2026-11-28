namespace Watch.Domain.Dash;

/// <summary>
/// What an ask from the wrist waits for, and what an answer from the phone is for: its path, and the key or the id
/// that tells one conversation's or one send's answer from another's (none for People waiting and the mail lines).
/// </summary>
public readonly record struct DashTicket(string Path, string? Id)
{
    /// <summary>Whether this ticket waits on <paramref name="path"/>: a refused answer there is the answer to it.</summary>
    public bool IsOn(string path) => string.Equals(Path, path, StringComparison.Ordinal);
}

/// <summary>
/// How long the wrist waits for each step (P14 B4), so no step holds a screen for good. Chosen, not measured, against
/// Post's own waits (B2's <c>DashReplies</c>): Post carries a send out within a turn of 60 s and waits 15 s for its
/// answer to land, so a send's answer is waited for longer than both together.
/// </summary>
public static class DashWaits
{
    /// <summary>Play services' own record of which phone carries the relay: local, so quick.</summary>
    public static readonly TimeSpan Find = TimeSpan.FromSeconds(5);

    /// <summary>One message handed to the phone, over Bluetooth or the cloud.</summary>
    public static readonly TimeSpan Deliver = TimeSpan.FromSeconds(10);

    /// <summary>People waiting, a conversation or the mail lines, answered.</summary>
    public static readonly TimeSpan Answer = TimeSpan.FromSeconds(20);

    /// <summary>A send carried out and answered.</summary>
    public static readonly TimeSpan SendAnswer = TimeSpan.FromSeconds(80);

    /// <summary>A photo fetched by the phone, shrunk and answered (B5).</summary>
    public static readonly TimeSpan Photo = TimeSpan.FromSeconds(30);
}

/// <summary>
/// The wrist's half of Dash's exchange (W-5.1.1, W-5.1.2). Post answers on the path it was asked on, once, to the node
/// that asked, and nothing else (B2): so the wrist matches each answer to the ask it is waiting for, People waiting by
/// its path, a conversation by its key, a send by the wrist's own name for it, a photo by its attachment id (B5), a page
/// of the Rolodex by its number (B6), the call that just ended by its moment (B7). A read,
/// a file, a delete, an undo and a note are told, and nothing answers them.
/// </summary>
public static class DashExchange
{
    public static DashTicket? Awaits(DashMessage asked) => asked switch
    {
        DashPeopleAsk ask => new DashTicket(ask.Path, null),
        DashThreadAsk ask => new DashTicket(ask.Path, ask.Key),
        DashSend send => new DashTicket(send.Path, send.Id),
        DashMailAsk ask => new DashTicket(ask.Path, null),
        DashPhotoAsk ask => new DashTicket(ask.Path, ask.Id),
        DashRolodexAsk ask => new DashTicket(ask.Path, PageOf(ask.Page)),
        DashCallAsk ask => new DashTicket(ask.Path, MomentOf(ask.Ended)),
        _ => null,
    };

    public static DashTicket? Answers(DashMessage answer) => answer switch
    {
        DashPeople people => new DashTicket(people.Path, null),
        DashThread thread => new DashTicket(thread.Path, thread.Key),
        DashSent sent => new DashTicket(sent.Path, sent.Id),
        DashMail mail => new DashTicket(mail.Path, null),
        DashPhoto photo => new DashTicket(photo.Path, photo.Id),
        DashRolodex rolodex => new DashTicket(rolodex.Path, PageOf(rolodex.Page)),
        DashCall call => new DashTicket(call.Path, MomentOf(call.Ended)),
        _ => null,
    };

    public static TimeSpan WaitFor(DashMessage asked) => asked switch
    {
        DashSend => DashWaits.SendAnswer,
        DashPhotoAsk => DashWaits.Photo,
        _ => DashWaits.Answer,
    };

    // The call that just ended is told from another by the moment it ended, to the millisecond the wire carries (B7).
    private static string MomentOf(DateTimeOffset ended) => ended.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

    // A page of the Rolodex is told from another by its number (B6).
    private static string PageOf(int page) => page.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
