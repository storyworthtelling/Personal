using Watch.Domain.Updates;

namespace Watch.Domain.Dash;

/// <summary>Which end of Dash's contracts reads a message.</summary>
public enum DashEnd
{
    Post,
    Dash,
}

/// <summary>What Post did with a message from the wrist, or what it answered.</summary>
public enum DashOutcome
{
    /// <summary>People waiting, a conversation or the mail lines were answered.</summary>
    Answered,

    /// <summary>The key, the messages, the call or the act named nothing Post has.</summary>
    Unknown,

    /// <summary>A send has a route to try.</summary>
    Ready,

    /// <summary>A send has nobody to reach.</summary>
    Nowhere,

    /// <summary>Nothing can carry a text right now.</summary>
    NoRoute,

    /// <summary>Messages filed.</summary>
    Filed,

    /// <summary>Messages held for their undo window before they are deleted.</summary>
    Held,

    /// <summary>The same act a second time: done once.</summary>
    Again,

    /// <summary>A file or a delete taken back inside its window.</summary>
    TakenBack,

    /// <summary>An undo that came after its window: the act stands.</summary>
    TooLate,

    /// <summary>A correspondence marked read.</summary>
    Marked,

    /// <summary>A call note kept.</summary>
    Kept,

    /// <summary>A message that waited past its turn behind the one before it: dropped, and the wrist asks or acts again.</summary>
    Busy,

    /// <summary>An answer the wrist did not take: dropped and counted, never queued or tried again.</summary>
    Unreached,
}

/// <summary>
/// Dash's log lines, for both ends: the contract and the outcome, a version, a count, an exception's type name, and
/// never anything from a message (the suite's contract rule, plan §2.5). Every method takes a Dash path, checked
/// against <see cref="WatchIdentity.DashPaths"/>, so a payload cannot be passed in its place.
/// </summary>
public static class DashLogLines
{
    /// <summary>The contract's name for a path: <c>/dash/send</c> is <c>dash/send</c>.</summary>
    public static string Contract(string path) =>
        WatchIdentity.DashPaths.Contains(path, StringComparer.Ordinal)
            ? path[1..]
            : throw new ArgumentException("Not one of Dash's paths.", nameof(path));

    public static string Receipt(string path, ContractRead<DashMessage> read, DashEnd reader)
    {
        var contract = Contract(path);
        return read.Refusal switch
        {
            ContractRefusal.None => $"CONTRACT {contract}: received v{read.Version}",
            ContractRefusal.Newer => $"CONTRACT {contract}: refused v{read.Version}, newer than {reader} reads",
            _ => $"CONTRACT {contract}: refused, unreadable",
        };
    }

    public static string Sent(string path)
    {
        var contract = Contract(path);
        return $"CONTRACT {contract}: sent v{DashCodec.Version}";
    }

    /// <param name="failure">The exception's type name; never its message. Anything else is written as <c>Exception</c>.</param>
    public static string NotSent(string path, string failure)
    {
        var contract = Contract(path);
        var typeName = IsTypeName(failure) ? failure : "Exception";
        return $"CONTRACT {contract}: not sent, {typeName}";
    }

    public static string Outcome(string path, DashOutcome outcome, int count = 0)
    {
        var contract = Contract(path);
        return count > 0 ? $"DASH {contract}: {outcome} {count}" : $"DASH {contract}: {outcome}";
    }

    /// <summary>How a send from the wrist went.</summary>
    public static string Send(DashSendOutcome outcome)
    {
        var contract = Contract(WatchIdentity.DashSendPath);
        return $"DASH {contract}: {outcome}";
    }

    // A type name as the platform writes one (Java.IO.IOException), and nothing that could be a message's words.
    private static bool IsTypeName(string failure) =>
        failure is { Length: > 0 and <= 120 }
        && char.IsAsciiLetter(failure[0])
        && failure.All(character => char.IsAsciiLetterOrDigit(character) || character == '.');
}
