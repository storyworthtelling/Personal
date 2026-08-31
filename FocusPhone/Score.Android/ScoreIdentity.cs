namespace Score;

/// <summary>
/// Whose install this is.
///
/// There is deliberately no setting for it. The bound Google account <i>is</i> the
/// identity — the Drive music folder, the taste profile and the personal vault are all
/// keyed to it already — so a stored flag could only ever agree with the account or be
/// wrong, and the screen that let you change it would be the worst screen in the app.
///
/// What it decides: whether this install may write the household's shared vault. The
/// owner's phone does; the second phone reads the shared keys and keeps its own
/// credentials in its own vault instead.
/// </summary>
public static class ScoreIdentity
{
    /// <summary>The signed-in account, or null before sign-in — which reads as "cannot
    /// tell", not as "somebody else".</summary>
    public static string? Account => MusicSettingsStore.GoogleAccountEmail;
}
