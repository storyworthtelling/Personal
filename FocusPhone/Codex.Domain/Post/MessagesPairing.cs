namespace Codex.Domain.Post;

/// <summary>Why a pairing did not finish, as the bridge reports it (bridge.go's pairingReason).</summary>
public enum PairingFailure
{
    WrongEmoji,
    Cancelled,
    TimedOut,
    NoPhone,
    PhoneNotResponding,
    SignedOut,
    Other,
}

/// <summary>
/// The words of the pairing screen. Static and neutral like every error in Post (never voiced),
/// and each failure says what to do next.
/// </summary>
public static class MessagesPairing
{
    public const string SigningIn = "Sign in with the Google account your phone's Messages uses.";

    public const string Starting = "Asking Google Messages to pair with Post…";

    public const string Paired = "Post is connected to Google Messages.";

    public static string PickEmoji(string emoji) =>
        $"Google Messages will ask you to confirm. Pick {emoji} there.";

    public static string SignInIncomplete(IReadOnlyList<string> missing) =>
        "The sign-in did not finish, so there is nothing to pair with yet (missing "
        + string.Join(", ", missing) + "). Sign in again.";

    public static string Describe(PairingFailure failure) => failure switch
    {
        PairingFailure.WrongEmoji => "That was not the emoji Post showed. Try again and pick the one shown here.",
        PairingFailure.Cancelled => "The pairing was turned down in Google Messages. Try again when you are ready.",
        PairingFailure.TimedOut => "Google Messages did not get an answer in time. Try again and confirm a little sooner.",
        PairingFailure.NoPhone => "Google could not find Google Messages on any phone for this account. "
            + "Sign in with the account Messages uses.",
        PairingFailure.PhoneNotResponding => "Google Messages on this phone did not answer. "
            + "Check the phone is online, then try again.",
        PairingFailure.SignedOut => "Google did not accept the sign-in. Sign in again.",
        _ => "The pairing did not finish. Try again; the diagnostics log has the details.",
    };

    public static bool WorthRetrying(PairingFailure failure) => failure != PairingFailure.NoPhone;
}

/// <summary>The one line Settings shows about texting.</summary>
public static class TextsSetting
{
    public static string Describe(bool paired, bool connected) =>
        !paired ? "Texts are not connected yet. Connect Post to Google Messages to send and read every text here, RCS included."
        : connected ? "Texts go through Google Messages, RCS included."
        : "Paired with Google Messages; Post reconnects the next time a text arrives or Post syncs.";
}
