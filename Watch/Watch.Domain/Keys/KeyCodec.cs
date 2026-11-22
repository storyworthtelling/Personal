using System.Text.Json;
using Watch.Domain.Updates;

namespace Watch.Domain.Keys;

/// <summary>
/// The handed-over token's codec (plan §2.4, §2.5, P14 W-4.1.2): UTF-8 JSON with an integer <c>v</c> and a <c>kind</c>,
/// read only on <see cref="WatchIdentity.GoogleKeysPath"/>. A newer <c>v</c> is refused before anything else is read.
/// Stricter than Dash's: each kind carries exactly its own fields, once each, so nothing rides beside a token by any name,
/// and a scope other than read-only Drive, a token of the wrong shape or a life past the longest hold refuses the whole
/// message. Nothing here logs, and a refusal says why without the token.
/// </summary>
public static class KeyCodec
{
    /// <summary>The version both ends write and read.</summary>
    public const int Version = 1;

    // Wire words, frozen: the envelope and the kinds.
    public const string VersionField = "v";
    public const string KindField = "kind";
    public const string AskKind = "ask";
    public const string TokenKind = "token";
    public const string NoneKind = "none";

    // Wire words, frozen: the fields.
    public const string ScopeField = "scope";
    public const string TokenField = "token";
    public const string LifeField = "life";
    public const string WhyField = "why";

    // Wire words, frozen, in each enum's order.
    private static readonly string[] ScopeWords = ["drive.readonly"];
    private static readonly string[] RefusalWords = ["sign-in", "failed"];

    // Each kind's whole set of fields.
    private static readonly string[] AskFields = [VersionField, KindField, ScopeField];
    private static readonly string[] TokenFields = [VersionField, KindField, ScopeField, TokenField, LifeField];
    private static readonly string[] NoneFields = [VersionField, KindField, ScopeField, WhyField];

    private static readonly long LongestLife = (long)KeyLimits.LongestHold.TotalSeconds;

    /// <summary>The message as the wire carries it.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A handoff whose life is under a second or past <see cref="KeyLimits.LongestHold"/>.</exception>
    public static byte[] Encode(KeyMessage message)
    {
        using var json = new MemoryStream();
        using (var writer = new Utf8JsonWriter(json))
        {
            writer.WriteStartObject();
            writer.WriteNumber(VersionField, Version);
            switch (message)
            {
                case TokenAsk ask:
                    writer.WriteString(KindField, AskKind);
                    writer.WriteString(ScopeField, Word(ask.Scope));
                    break;
                case TokenHandoff handoff:
                    var seconds = (long)Math.Floor(handoff.Life.TotalSeconds);
                    if (seconds < 1 || seconds > LongestLife)
                    {
                        throw new ArgumentOutOfRangeException(nameof(message),
                            $"A handed-over token's life is 1 to {LongestLife} whole seconds; this one's is {seconds}.");
                    }
                    writer.WriteString(KindField, TokenKind);
                    writer.WriteString(ScopeField, Word(handoff.Scope));
                    writer.WriteString(TokenField, handoff.Token.Value);
                    writer.WriteNumber(LifeField, seconds);
                    break;
                case TokenNone none:
                    writer.WriteString(KindField, NoneKind);
                    writer.WriteString(ScopeField, Word(none.Scope));
                    writer.WriteString(WhyField, Word(none.Why));
                    break;
                default:
                    throw new ArgumentException($"{message.GetType().Name} is not a keys message.", nameof(message));
            }
            writer.WriteEndObject();
        }
        return json.ToArray();
    }

    /// <summary>The message that arrived on <paramref name="path"/>, or why it was refused.</summary>
    public static ContractRead<KeyMessage> Decode(string? path, byte[]? bytes)
    {
        if (!string.Equals(path, WatchIdentity.GoogleKeysPath, StringComparison.Ordinal)
            || bytes is null || bytes.Length == 0 || bytes.Length > KeyLimits.MaxBytes)
        {
            return ContractRead<KeyMessage>.Refused(ContractRefusal.Unreadable, 0);
        }
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ContractRead<KeyMessage>.Refused(ContractRefusal.Unreadable, 0);
            }

            var version = ContractVersion.Of(root, VersionField);
            if (version <= 0)
            {
                return ContractRead<KeyMessage>.Refused(ContractRefusal.Unreadable, 0);
            }
            if (version > Version)
            {
                return ContractRead<KeyMessage>.Refused(ContractRefusal.Newer, version);
            }

            var kind = root.TryGetProperty(KindField, out var said) && said.ValueKind == JsonValueKind.String ? said.GetString() : null;
            KeyMessage? message = kind switch
            {
                AskKind => ReadAsk(root),
                TokenKind => ReadHandoff(root),
                NoneKind => ReadNone(root),
                _ => null,
            };
            return message is null
                ? ContractRead<KeyMessage>.Refused(ContractRefusal.Unreadable, version)
                : ContractRead<KeyMessage>.Read(message, version);
        }
        catch (JsonException)
        {
            // Not JSON: nothing in it can be trusted, so it is refused as unreadable, and the line says so.
            return ContractRead<KeyMessage>.Refused(ContractRefusal.Unreadable, 0);
        }
    }

    /// <summary>A scope's wire word.</summary>
    public static string Word(TokenScope scope) => ScopeWords[(int)scope];

    /// <summary>A refusal's wire word.</summary>
    public static string Word(TokenRefusal why) => RefusalWords[(int)why];

    // ---- reading ----

    private static TokenAsk? ReadAsk(JsonElement root) =>
        Exactly(root, AskFields) && Scope(root, out var scope) ? new TokenAsk(scope) : null;

    private static TokenHandoff? ReadHandoff(JsonElement root) =>
        Exactly(root, TokenFields) && Scope(root, out var scope) && Token(root, out var token) && Life(root, out var life)
            ? new TokenHandoff(scope, token, life)
            : null;

    private static TokenNone? ReadNone(JsonElement root) =>
        Exactly(root, NoneFields) && Scope(root, out var scope) && Pick(root, WhyField, RefusalWords, out var why)
            ? new TokenNone(scope, (TokenRefusal)why)
            : null;

    // The kind's own fields, each once, and no other.
    private static bool Exactly(JsonElement root, string[] fields)
    {
        var names = root.EnumerateObject().Select(property => property.Name).ToList();
        return names.Count == fields.Length && names.Distinct(StringComparer.Ordinal).Count() == names.Count
            && names.All(name => fields.Contains(name, StringComparer.Ordinal));
    }

    private static bool Scope(JsonElement root, out TokenScope scope)
    {
        var found = Pick(root, ScopeField, ScopeWords, out var index);
        scope = found ? (TokenScope)index : default;
        return found;
    }

    private static bool Token(JsonElement root, out AccessToken token)
    {
        token = null!;
        if (!root.TryGetProperty(TokenField, out var said) || said.ValueKind != JsonValueKind.String
            || AccessToken.From(said.GetString()) is not { } read)
        {
            return false;
        }
        token = read;
        return true;
    }

    // Whole seconds, at least one and no more than the longest hold.
    private static bool Life(JsonElement root, out TimeSpan life)
    {
        life = TimeSpan.Zero;
        if (!root.TryGetProperty(LifeField, out var said) || said.ValueKind != JsonValueKind.Number
            || !said.TryGetInt64(out var seconds) || seconds < 1 || seconds > LongestLife)
        {
            return false;
        }
        life = TimeSpan.FromSeconds(seconds);
        return true;
    }

    private static bool Pick(JsonElement root, string field, string[] words, out int index)
    {
        index = -1;
        if (!root.TryGetProperty(field, out var said) || said.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        index = Array.IndexOf(words, said.GetString());
        return index >= 0;
    }
}
