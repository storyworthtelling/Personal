namespace Codex.Domain.Music;

/// <summary>
/// What to put on the pairing screen when TIDAL refuses to start a sign-in.
///
/// The screen used to say "Couldn't reach TIDAL. Try again in a moment." to every
/// failure, which on the real one was wrong twice over: TIDAL had been reached and
/// had answered, and no amount of trying again would have changed the answer — the
/// app in TIDAL's developer portal is not registered for the device-code grant.
///
/// DESIGN.md §8 asks an error for "helper text that states the fix". A retry offered
/// for something a retry cannot fix is worse than no helper text at all: it sends the
/// owner round a loop instead of to the one screen that can settle it.
///
/// Pure and here rather than in the activity so the sentence for each refusal is
/// decided once and testable, and so the head keeps only the rendering.
/// </summary>
public static class TidalSignInFailure
{
    /// <summary>
    /// One sentence for the pairing screen, given whatever the sign-in threw. The
    /// stack trace goes to Diagnostics; this is the part a person reads.
    /// </summary>
    public static string Describe(string? failure)
    {
        var text = failure ?? string.Empty;

        // TIDAL's own words for "this client may not use the device-code grant".
        //
        // Deliberately does NOT send the owner to the developer portal to switch
        // something on, which is what this first said. There is nothing there to
        // switch: TIDAL's Android SDK documents the device flow as "(Only available
        // for TIDAL internally developed applications for now)". Helper text that
        // states a fix which does not exist is the same dead end as offering a retry
        // that cannot work — it just takes longer to discover.
        if (Mentions(text, "Limited Input Device"))
        {
            return "TIDAL only allows this kind of sign-in for its own apps. Score needs a different one.";
        }

        if (Mentions(text, "invalid_client") || Mentions(text, "unauthorized_client"))
        {
            return "TIDAL rejected the app's credentials. Check the client id and secret in the vault.";
        }

        if (Mentions(text, "invalid_scope"))
        {
            return "TIDAL won't grant this app the permissions Score asks for.";
        }

        // Everything else — no network, a timeout, a shape nobody has seen — is the
        // kind of thing that genuinely may work on the next try.
        return "Couldn't reach TIDAL. Try again in a moment.";
    }

    /// <summary>
    /// The sentence shown while the owner is on TIDAL's page. A refusal there — TIDAL's
    /// "Error 1002" — happens where Score cannot see it and never comes back, so this is
    /// the only place to say what fixes it: the redirect URI and the scopes registered on
    /// the app in TIDAL's developer portal, which the page checks before anything else.
    /// </summary>
    public const string WhileAtTidal =
        "Finish signing in, and TIDAL will send you back here. If TIDAL's page shows an " +
        "error instead, the app's redirect URI or scopes in TIDAL's developer portal don't " +
        "match what Score asks for.";

    /// <summary>
    /// Whether a failure is credential-shaped — the app's client id/secret being wrong or
    /// missing rather than a transient network problem — so a caller can decide whether it is
    /// worth flagging on the credential health seam. Deliberately narrower than every branch
    /// <see cref="Describe"/> handles: a "Limited Input Device" or scope refusal is TIDAL
    /// refusing what the app asked for, not evidence the stored credentials themselves are bad.
    /// </summary>
    public static bool IsCredentialRejection(string? failure) =>
        Mentions(failure ?? string.Empty, "invalid_client") || Mentions(failure ?? string.Empty, "unauthorized_client");

    private static bool Mentions(string text, string term) =>
        text.Contains(term, StringComparison.OrdinalIgnoreCase);
}
