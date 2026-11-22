using System.Security.Cryptography;
using System.Text;

namespace Watch.Domain.Dash;

/// <summary>
/// The ids on Dash's contracts. Post names a correspondence, a message and an attachment by a digest of its own key
/// (a letter, then sixteen hex digits of a SHA-256), so a number, an address or Post's own source id is never what the
/// watch holds, logs or sends back, and the same thing keeps the same id from one answer to the next. It is not secrecy:
/// a number's digest can be searched for. The watch names its own acts and sends (<see cref="IsAct"/>).
/// </summary>
public static class DashIds
{
    public const int DigestLength = 16;

    public const int MaxActLength = 64;

    private const char CorrespondenceLetter = 'c';
    private const char MessageLetter = 'm';
    private const char AttachmentLetter = 'a';

    public static string Correspondence(string threadKey) => Id(CorrespondenceLetter, threadKey);

    public static string Message(string sourceId) => Id(MessageLetter, sourceId);

    public static string Attachment(string sourceId, string attachmentId) => Id(AttachmentLetter, sourceId + "\n" + attachmentId);

    public static bool IsCorrespondence(string? id) => Is(id, CorrespondenceLetter);

    public static bool IsMessage(string? id) => Is(id, MessageLetter);

    public static bool IsAttachment(string? id) => Is(id, AttachmentLetter);

    /// <summary>An id the watch chose for an act or a send: 1 to 64 lowercase letters, digits or hyphens.</summary>
    public static bool IsAct(string? id) =>
        id is { Length: > 0 and <= MaxActLength } && id.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    // The letter goes into the hash too, so a thread key that happens to equal a source id never shares its digest.
    private static string Id(char letter, string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{letter}\n{value}"));
        return letter + Convert.ToHexString(hash)[..DigestLength].ToLowerInvariant();
    }

    private static bool Is(string? id, char letter) =>
        id is { Length: DigestLength + 1 } && id[0] == letter && id.Skip(1).All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
