namespace Constitution.Domain.ReadBack;

/// <summary>
/// How one document came out of a morning's read-back. A closed list, so a caller has to say what it does
/// for each; none of them is "nothing happened" in disguise.
/// </summary>
public abstract record DocumentReadBack(string Name)
{
    /// <summary>Read, resolved, saved, and its page trashed.</summary>
    public sealed record Settled(string Name, int PagesRead, int Questions) : DocumentReadBack(Name);

    /// <summary>The digest was three days old and its page was trashed (R4).</summary>
    public sealed record DigestTrashed(string Name) : DocumentReadBack(Name);

    /// <summary>The digest is not yet three days old.</summary>
    public sealed record DigestKept(string Name) : DocumentReadBack(Name);

    /// <summary>
    /// The document was found and read, but no pen ink shows anywhere on it and no earlier document has ever
    /// shown any (R3). It might be an unannotated cloud copy or an owner who did nothing; the page is kept and
    /// nothing is recorded as a skip until a mark has proved the ink comes through.
    /// </summary>
    public sealed record NoMarksInCloud(string Name) : DocumentReadBack(Name);

    /// <summary>No document with that title is on the tablet. Never a skip: an absent page is not an empty box.</summary>
    public sealed record NotOnTablet(string Name) : DocumentReadBack(Name);

    /// <summary>The read could not settle it (a page or a box was not read); the page is kept and tried again.</summary>
    public sealed record Unfinished(string Name, string Why) : DocumentReadBack(Name);

    /// <summary>A week has passed without it settling, so it is left alone on the tablet and no longer tried.</summary>
    public sealed record GaveUp(string Name) : DocumentReadBack(Name);

    /// <summary>A step failed; the original exception is kept for the caller. The page is kept and tried again.</summary>
    public sealed record Failed(string Name, ReadBackStage Stage, Exception Failure) : DocumentReadBack(Name);
}
