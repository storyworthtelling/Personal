namespace Constitution.Domain.Diagnostics;

/// <summary>
/// Constitution's connection ids, the credential ids' own values (one copy, audit 01 §15) plus Google, in the style of the suite's <c>CredentialIds</c>: a
/// stable name a client reports health under and a settings screen reads back. A closed list, and the
/// allow-list <see cref="ConnectionId"/> validates against.
/// </summary>
public static class ConnectionIds
{
    /// <summary>The bed's login.</summary>
    public const string SleepNumber = Credentials.CredentialIds.SleepNumber;

    /// <summary>The tablet's device pairing.</summary>
    public const string ReMarkable = Credentials.CredentialIds.Remarkable;

    /// <summary>The shared Claude key.</summary>
    public const string Claude = Credentials.CredentialIds.Claude;

    /// <summary>The shared Gemini key.</summary>
    public const string Gemini = Credentials.CredentialIds.Gemini;

    /// <summary>The Google account behind the calendar and Drive.</summary>
    public const string Google = "google";

    /// <summary>Every id, in the order the export lists them.</summary>
    public static IReadOnlyList<string> All { get; } = [SleepNumber, ReMarkable, Claude, Gemini, Google];
}

/// <summary>A connection id that is one of <see cref="ConnectionIds.All"/>; nothing else can be one.</summary>
public sealed record ConnectionId
{
    private ConnectionId(string value)
    {
        Value = value;
    }

    /// <summary>The id.</summary>
    public string Value { get; }

    /// <summary>The id for this text, which must be exactly one of <see cref="ConnectionIds.All"/>. A refusal does not repeat the text.</summary>
    public static ConnectionId Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!ConnectionIds.All.Contains(text, StringComparer.Ordinal))
        {
            throw new ArgumentException("That is not one of Constitution's connection ids.", nameof(text));
        }

        return new ConnectionId(text);
    }
}
