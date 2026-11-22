using Watch.Domain.Updates;

namespace Watch.Domain.Keys;

/// <summary>Which end of the keys contract reads a message.</summary>
public enum KeyEnd
{
    Phone,
    Watch,
}

/// <summary>What became of an ask, an answer or the token held.</summary>
public enum KeyOutcome
{
    /// <summary>The watch asked the phone for a token.</summary>
    Asked,

    /// <summary>The phone answered an ask.</summary>
    Answered,

    /// <summary>The watch holds a handed-over token.</summary>
    Held,

    /// <summary>The phone has no token until the owner signs in again there.</summary>
    SignIn,

    /// <summary>The phone has no token, for any other reason.</summary>
    Failed,

    /// <summary>The token held came to the end of its life.</summary>
    Expired,

    /// <summary>The token held was let go because Google refused it.</summary>
    Forgotten,
}

/// <summary>
/// The keys contract's log lines, for both ends: the contract and the outcome, a version and an exception's type name,
/// never a token (plan §2.5, §5). Every method takes the keys path, checked against
/// <see cref="WatchIdentity.GoogleKeysPath"/>, so a token cannot be passed in its place; none takes a message.
/// </summary>
public static class KeyLogLines
{
    /// <summary>The contract's name for the path: <c>/keys/google</c> is <c>keys/google</c>.</summary>
    public static string Contract(string path) =>
        string.Equals(path, WatchIdentity.GoogleKeysPath, StringComparison.Ordinal)
            ? path[1..]
            : throw new ArgumentException("Not the keys path.", nameof(path));

    public static string Receipt(string path, ContractRead<KeyMessage> read, KeyEnd reader)
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
        return $"CONTRACT {contract}: sent v{KeyCodec.Version}";
    }

    /// <param name="failure">What stopped the send: only its type's name is written, never its message, which can hold
    /// anything a call was given.</param>
    public static string NotSent(string path, Exception failure)
    {
        var contract = Contract(path);
        var typeName = failure.GetType().Name;
        return $"CONTRACT {contract}: not sent, {typeName}";
    }

    public static string Outcome(string path, KeyOutcome outcome)
    {
        var contract = Contract(path);
        return $"KEYS {contract}: {outcome}";
    }
}
