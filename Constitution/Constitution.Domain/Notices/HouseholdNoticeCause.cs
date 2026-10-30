namespace Constitution.Domain.Notices;

/// <summary>
/// The cause keys of <see cref="NoticeKind.HouseholdFileUnreadable"/> and the plain words each one gets on the Waiting page.
/// The key is <c>household:blocked:&lt;word&gt;</c> where the word names the reason and nothing else: never the passphrase, an
/// exception message or anything read from the file.
/// </summary>
public static class HouseholdNoticeCause
{
    public const string Prefix = "household:blocked:";

    public const string MissingKeyWord = "nopassphrase";
    public const string MismatchWord = "wrongpassphrase";
    public const string DamagedWord = "damaged";
    public const string NewerWord = "newerformat";

    /// <summary>True when the cause is one of this kind's keys.</summary>
    public static bool Owns(string causeKey) => causeKey.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>What happened and what to do, for the reason the key names; a plain general sentence for a key with no known word.</summary>
    public static string Explain(string causeKey)
    {
        var word = causeKey.StartsWith(Prefix, StringComparison.Ordinal) ? causeKey[Prefix.Length..] : string.Empty;
        return word switch
        {
            MissingKeyWord =>
                "This phone has no vault passphrase, and the household file is locked with one. Enter it under Vault in Settings. Until then recipe edits are paused and nothing in the file is changed.",
            MismatchWord =>
                "The vault passphrase on this phone doesn’t match the one the household file is locked with, or the signed-in Google account can’t open it. Check the passphrase under Vault in Settings. Until then recipe edits are paused and nothing in the file is changed.",
            DamagedWord =>
                "The household file is damaged and can’t be read. Recipe edits are paused and nothing in the file is changed; the owner needs to restore it from Drive’s version history.",
            NewerWord =>
                "The household file was saved by a newer version of Constitution than this phone has. Update the app. Until then recipe edits are paused and nothing in the file is changed.",
            _ =>
                "The household file can’t be opened on this phone. Recipe edits are paused and nothing in the file is changed. Check the vault passphrase and the signed-in account in Settings.",
        };
    }
}
