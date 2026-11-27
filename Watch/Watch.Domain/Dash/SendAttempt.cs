namespace Watch.Domain.Dash;

/// <summary>Whether some words can go as one text from the wrist.</summary>
public enum SendCheck
{
    Fits,
    Empty,
    TooLong,
}

/// <summary>
/// A reply from the wrist (W-5.1.2, A10): dictated or typed, the same on the wire, into one correspondence (a person or
/// a group; Post works out who and how, B1's <c>ReplyTarget</c>), or a new text to one contact of the Rolodex (B6), whose
/// <see cref="Key"/> is then the contact's id and whose number Post finds on the phone. The watch names each send
/// (<see cref="Id"/>), and Post keeps every send in its book under that name and answers it again from there (B2), so a
/// send asked again under the same name is never sent twice. Hence <see cref="Again"/>: with no answer the same name goes again, since the
/// text may already have gone; once Post has answered, trying again is a new send.
/// </summary>
public sealed record SendAttempt(string Id, string Key, string Text)
{
    private const string Prefix = "send-";

    public DashSend Message => IsNewText ? new DashSend(Id, null, [], Text) { Contact = Key } : new DashSend(Id, Key, [], Text);

    /// <param name="name">A fresh name for the send; any Guid, written as 32 lowercase hex digits.</param>
    public static SendAttempt Reply(string key, string words, Guid name)
    {
        if (Check(words) != SendCheck.Fits)
        {
            throw new ArgumentException($"A reply must have words, and at most {DashLimits.SendText} characters; these are {Check(words)}.", nameof(words));
        }
        return new SendAttempt(NameOf(name), key, words.Trim());
    }

    /// <summary>A new text to a contact of the Rolodex (B6), by its id, named by the watch as a reply is.</summary>
    public static SendAttempt NewText(string contact, string words, Guid name)
    {
        if (!DashIds.IsContact(contact))
        {
            throw new ArgumentException("A new text goes to a contact of the Rolodex, named by its id.", nameof(contact));
        }
        if (Check(words) != SendCheck.Fits)
        {
            throw new ArgumentException($"A text must have words, and at most {DashLimits.SendText} characters; these are {Check(words)}.", nameof(words));
        }
        return new SendAttempt(NameOf(name), contact, words.Trim());
    }

    /// <summary>Whether this is a new text to a contact rather than a reply into a correspondence.</summary>
    public bool IsNewText => DashIds.IsContact(Key);

    public static SendCheck Check(string? words)
    {
        var trimmed = words?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return SendCheck.Empty;
        }
        return trimmed.Length > DashLimits.SendText ? SendCheck.TooLong : SendCheck.Fits;
    }

    /// <summary>The send to make when the owner tries again after <paramref name="answered"/> (null: no answer came).</summary>
    public SendAttempt Again(DashSendOutcome? answered, Guid name) => answered is null ? this : this with { Id = NameOf(name) };

    // "send-" and 32 lowercase hex digits: one of the watch's own ids (DashIds.IsAct).
    private static string NameOf(Guid name) => Prefix + name.ToString("N");
}
