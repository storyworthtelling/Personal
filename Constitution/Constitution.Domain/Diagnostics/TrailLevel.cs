namespace Constitution.Domain.Diagnostics;

/// <summary>
/// Which trail phrases are problems (audit 08 §10). Every Constitution trail line was INFO, so a refused vault, an
/// unreadable household file or Health Connect revoked read at the level of "ok, 2 readings". The trails already say
/// what happened in a small, fixed vocabulary, so the level is read off that vocabulary here, in one place, rather
/// than decided again at a hundred call sites.
/// </summary>
public static class TrailLevel
{
    private static readonly string[] ProblemWords =
    [
        "failed", "unreachable", "unreadable", "denied", "refused", "corrupt", "offline", "unsupported",
        "unexpected shape", "not kept", "not stored", "not restored", "not opened", "not granted", "not written",
        "not deleted", "not found", "threw",
    ];

    public static bool IsProblem(string phrase) =>
        ProblemWords.Any(word => phrase.Contains(word, StringComparison.OrdinalIgnoreCase));
}
