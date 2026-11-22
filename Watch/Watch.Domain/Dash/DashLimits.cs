namespace Watch.Domain.Dash;

/// <summary>
/// How much one Dash message carries. Both ends hold to these: Post cuts to them before it encodes, and
/// <see cref="DashCodec"/> refuses a message past any of them. They are chosen, not measured: a wrist shows a few rows
/// and a few lines, and every message stays under <see cref="MaxBytes"/>, well inside the 100 KB the Data Layer documents
/// for a data item (a message's own limit was not found documented; the device run says).
/// </summary>
public static class DashLimits
{
    /// <summary>Correspondences on People waiting.</summary>
    public const int People = 24;

    /// <summary>The last messages of a conversation.</summary>
    public const int Messages = 16;

    /// <summary>Mail lines.</summary>
    public const int MailLines = 16;

    /// <summary>Members named for a group.</summary>
    public const int Members = 12;

    /// <summary>Numbers a new text goes to.</summary>
    public const int Recipients = 20;

    /// <summary>Attachments named on one message.</summary>
    public const int Attachments = 8;

    /// <summary>A name, a sender, a title: characters.</summary>
    public const int Name = 60;

    /// <summary>The one line under a name: characters.</summary>
    public const int Line = 120;

    /// <summary>A message's words in a conversation: characters.</summary>
    public const int MessageText = 640;

    /// <summary>A mail's subject: characters.</summary>
    public const int Subject = 120;

    /// <summary>A text or a note from the wrist: characters.</summary>
    public const int SendText = 4000;

    /// <summary>A number as typed or dictated: characters.</summary>
    public const int Number = 40;

    /// <summary>The longest undo window the wrist may ask for: Android's longest time to take action.</summary>
    public static readonly TimeSpan MaxWindow = TimeSpan.FromMinutes(2);

    /// <summary>The most one encoded message may be.</summary>
    public const int MaxBytes = 64 * 1024;
}
