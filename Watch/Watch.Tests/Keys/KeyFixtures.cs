using Watch.Domain.Keys;

namespace Watch.Tests.Keys;

/// <summary>
/// Invented values for the handed-over token's tests (P14 W-4.1.2). Nothing here is a real token: each says it is
/// invented, so the repository's key guards read it as the stand-in it is.
/// </summary>
internal static class KeyFixtures
{
    /// <summary>A token's shape, and a word that must never come out of a log line, a string or an exception.</summary>
    public const string Sentinel = "invented.wrist-token_for~the+tests/0001=";

    public const string Other = "invented.other-wrist-token_for-the-tests";

    public static readonly DateTimeOffset T0 = new(2026, 11, 22, 9, 30, 0, TimeSpan.Zero);

    public static AccessToken Token => AccessToken.From(Sentinel)!;

    public static AccessToken OtherToken => AccessToken.From(Other)!;

    public static TokenHandoff Handoff(TimeSpan life) => new(TokenScope.DriveReadOnly, Token, life);

    public static readonly IReadOnlyDictionary<string, KeyMessage> Messages = new Dictionary<string, KeyMessage>
    {
        ["ask"] = new TokenAsk(TokenScope.DriveReadOnly),
        ["token"] = new TokenHandoff(TokenScope.DriveReadOnly, Token, TimeSpan.FromMinutes(45)),
        ["short token"] = new TokenHandoff(TokenScope.DriveReadOnly, Token, TimeSpan.FromSeconds(1)),
        ["none, sign in"] = new TokenNone(TokenScope.DriveReadOnly, TokenRefusal.SignIn),
        ["none, failed"] = new TokenNone(TokenScope.DriveReadOnly, TokenRefusal.Failed),
    };

    public static TheoryData<string> Names()
    {
        var names = new TheoryData<string>();
        foreach (var name in Messages.Keys)
        {
            names.Add(name);
        }
        return names;
    }
}
